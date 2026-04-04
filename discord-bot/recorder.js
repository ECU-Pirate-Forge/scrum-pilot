const { joinVoiceChannel, VoiceConnectionStatus, EndBehaviorType } = require('@discordjs/voice');
const ffmpegStatic = require('ffmpeg-static');
const { spawn } = require('child_process');
const path = require('path');
const fs = require('fs');
const prism = require('prism-media');
const Anthropic = require('@anthropic-ai/sdk');
const OpenAI = require('openai');

const MEETING_VOICE_CHANNEL = 'voice Scrum Pilot';
const BOT_COMMANDS_CHANNEL = 'bot-commands';
const RECAP_CHANNEL = 'standup-recap';
const QUORUM = 2

const anthropic = new Anthropic({ apiKey: process.env.ANTHROPIC_API_KEY });
const openai = new OpenAI({ apiKey: process.env.OPENAI_API_KEY });

let connection = null;
let recording = false;
let starting = false;
let stopping = false;
let recordingTimestamp = null;
let userRecordings = new Map();

function getChannel(guild, name, type = 'text') {
  return guild.channels.cache.find(c =>
    c.name === name && (type === 'text' ? c.isTextBased() : c.type === 2)
  );
}

function formatDate(timestamp) {
  const date = new Date(parseInt(timestamp));
  return date.toLocaleString('en-US', {
    weekday: 'long',
    year: 'numeric',
    month: 'long',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    timeZone: 'America/New_York',
    timeZoneName: 'short',
  });
}

function getDisplayName(guild, userId) {
  const member = guild.members.cache.get(userId);
  return member?.displayName || member?.user?.username || `User-${userId}`;
}

function subscribeUser(receiver, userId) {
  if (userRecordings.has(userId)) return;
 
  const userOutputPath = path.join(__dirname, `recording-${recordingTimestamp}-${userId}.ogg`);
 
  const userFfmpeg = spawn(ffmpegStatic, [
    '-f', 's16le',
    '-ar', '48000',
    '-ac', '2',
    '-i', 'pipe:0',
    userOutputPath
  ]);
 
  userFfmpeg.stderr.on('data', (data) => {
    console.log(`[ffmpeg:${userId}]`, data.toString());
  });
 
  userRecordings.set(userId, {
    process: userFfmpeg,
    outputPath: userOutputPath,
    startTime: Date.now(),
  });
 
  console.log(`[Recorder] Subscribed to user ${userId}`);
 
  const opusStream = receiver.subscribe(userId, {
    end: { behavior: EndBehaviorType.Manual }
  });
 
  const decoder = new prism.opus.Decoder({
    frameSize: 960,
    channels: 2,
    rate: 48000
  });
 
  decoder.on('error', (err) => {
    console.warn(`[Recorder] Opus decode error (user ${userId}):`, err.message);
  });
  opusStream.on('error', (err) => {
    console.warn(`[Recorder] Opus stream error (user ${userId}):`, err.message);
  });
 
  opusStream.pipe(decoder).pipe(userFfmpeg.stdin, { end: false });
}

async function startRecording(guild) {
  const voiceChannel = getChannel(guild, MEETING_VOICE_CHANNEL, 'voice');
  const controlChannel = getChannel(guild, BOT_COMMANDS_CHANNEL);
  if (!voiceChannel || !controlChannel) return;

  connection = joinVoiceChannel({
    channelId: voiceChannel.id,
    guildId: guild.id,
    adapterCreator: guild.voiceAdapterCreator,
    selfDeaf: false,
    selfMute: true,
  });

  connection.on(VoiceConnectionStatus.Ready, async () => {
    console.log('[Recorder] Connected to voice channel.');

    recordingTimestamp = Date.now();
    userRecordings = new Map();
    const receiver = connection.receiver;
 
    // Subscribe to anyone already in the channel
    voiceChannel.members.forEach(member => {
      if (!member.user.bot) subscribeUser(receiver, member.id);
    });
 
    // Subscribe to anyone who starts speaking after connection
    receiver.speaking.on('start', (userId) => {
      subscribeUser(receiver, userId);
    });
 
    starting = false;
    recording = true;
    await controlChannel.send('🔴 Scrumlord is listening.');
    console.log('[Recorder] Recording started. Timestamp:', recordingTimestamp);
  });
}

async function stopRecording(guild) {
  const controlChannel = getChannel(guild, BOT_COMMANDS_CHANNEL);
  const recapChannel = getChannel(guild, RECAP_CHANNEL);

  if (userRecordings.size > 0) {
    await Promise.all([...userRecordings.values()].map(({ process }) =>
      new Promise((resolve) => {
        process.on('close', resolve);
        if (process.stdin && !process.stdin.destroyed) {
          process.stdin.end();
        }
      })
    ));
  }

  if (connection) {
    connection.destroy();
    connection = null;
  }

  recording = false;
  starting = false;
  await controlChannel.send('⏹️ Meeting ended — processing recording...');
  console.log('[Recorder] Recording stopped. Processing...');

  if (userRecordings.size > 0) {
    await transcribeMultiTrack(guild, userRecordings, recordingTimestamp, recapChannel, controlChannel);
  }
 
  userRecordings = new Map();
  recordingTimestamp = null;
  stopping = false;
}

async function compressAudio(inputPath, controlChannel = null) {
  const outputPath = inputPath.replace('.ogg', '.mp3');
  const silenceRemovedPath = inputPath.replace('.ogg', '-trimmed.ogg');
  const MAX_WHISPER_BYTES = 25 * 1024 * 1024;

  const runFfmpeg = (args) => new Promise((resolve, reject) => {
    const ffmpeg = spawn(ffmpegStatic, args);
    ffmpeg.on('error', (err) => reject(new Error(`ffmpeg spawn error: ${err.message}`)));
    ffmpeg.on('close', (code) => code === 0 ? resolve() : reject(new Error(`ffmpeg exited with code ${code}`)));
  });

  // Step 1: Remove silence
  console.log('[Recorder] Removing silence...');
  try {
    await runFfmpeg([
      '-i', inputPath,
      '-af', 'silenceremove=stop_periods=-1:stop_duration=0.5:stop_threshold=-40dB',
      silenceRemovedPath
    ]);
  } catch (err) {
    // If silence removal fails, fall back to original file
    if (fs.existsSync(silenceRemovedPath)) fs.unlinkSync(silenceRemovedPath);
    console.warn('[Recorder] Silence removal failed, proceeding with original file.');
  }

  const fileToCompress = fs.existsSync(silenceRemovedPath) ? silenceRemovedPath : inputPath;

  // Step 2: Compress to MP3 at 32k
  console.log('[Recorder] Compressing audio...');
  try {
    await runFfmpeg(['-i', fileToCompress, '-codec:a', 'libmp3lame', '-b:a', '32k', outputPath]);
  } catch (err) {
    // Corrupted file — retry with error recovery at 16k
    if (fs.existsSync(outputPath)) fs.unlinkSync(outputPath);
    console.warn('[Recorder] Compression failed, attempting error recovery at 16kbps...');
    if (controlChannel) await controlChannel.send('⚠️ Audio appears corrupted — attempting recovery at reduced quality...');
    await runFfmpeg(['-err_detect', 'ignore_err', '-i', fileToCompress, '-codec:a', 'libmp3lame', '-b:a', '16k', outputPath]);
  }

  // Clean up silence-removed intermediate file
  if (fs.existsSync(silenceRemovedPath)) fs.unlinkSync(silenceRemovedPath);

  // Step 3: Size check — if still over 25MB, recompress at 16k
  const { size } = fs.statSync(outputPath);
  if (size > MAX_WHISPER_BYTES) {
    console.warn(`[Recorder] Compressed file is ${(size / 1024 / 1024).toFixed(1)} MB, retrying at 16kbps...`);
    if (controlChannel) await controlChannel.send('⚠️ Recording is very long — recompressing at 16kbps to fit within transcription limits...');
    fs.unlinkSync(outputPath);
    await runFfmpeg(['-i', inputPath, '-codec:a', 'libmp3lame', '-b:a', '16k', outputPath]);

    const { size: newSize } = fs.statSync(outputPath);
    if (newSize > MAX_WHISPER_BYTES) {
      fs.unlinkSync(outputPath);
      throw new Error(`Recording too large even at 16kbps (${(newSize / 1024 / 1024).toFixed(1)} MB)`);
    }
  }

  return outputPath;
}

async function transcribeMultiTrack(guild, userRecordings, timestamp, recapChannel, controlChannel) {
  const dateString = formatDate(timestamp);
  const sorted = [...userRecordings.entries()].sort((a, b) => a[1].startTime - b[1].startTime);
  const speakerTranscripts = [];

  for (const [userId, { outputPath }] of sorted) {
    const displayName = getDisplayName(guild, userId);
 
    if (!fs.existsSync(outputPath)) {
      console.warn(`[Recorder] No file found for ${displayName}, skipping.`);
      continue;
    }

    let compressedPath;
    try {
      compressedPath = await compressAudio(outputPath, controlChannel);

      console.log(`[Whisper] Transcribing ${displayName}...`);
      const transcription = await openai.audio.transcriptions.create({
        file: fs.createReadStream(compressedPath),
        model: 'whisper-1',
      });

      fs.unlinkSync(compressedPath);
      fs.unlinkSync(outputPath);

      if (transcription.text.trim()) {
        speakerTranscripts.push({ displayName, text: transcription.text.trim() });
        console.log(`[Whisper] Transcribed ${displayName}.`);
      } else {
        console.log(`[Whisper] ${displayName} had no speech, skipping.`);
      }
    } catch (err) {
      if (compressedPath && fs.existsSync(compressedPath)) fs.unlinkSync(compressedPath);
      const isCorrupt = err.message.includes('ffmpeg');
      console.error(`[Recorder] Failed to process ${displayName}'s track:`, err.message);
      await controlChannel.send(
        isCorrupt
          ? `⚠️ ${displayName}'s audio track appears corrupted and could not be processed.`
          : `⚠️ Something went wrong processing ${displayName}'s audio track.`
      );
    }
  }

  if (speakerTranscripts.length === 0) {
    await controlChannel.send('⚠️ No usable audio was recorded from this meeting.');
    return;
  }



  const labeledTranscript = speakerTranscripts
    .map(({ displayName, text }) => `[${displayName}]: ${text}`)
    .join('\n\n');
 
  const summary = await summarize(labeledTranscript);
 
  const markdownContent = summary
    ? `# 📋 Meeting Recap\n*Recorded ${dateString}*\n\n## Summary\n${summary}\n\n## Full Transcript\n${labeledTranscript}`
    : `# 📋 Meeting Transcript\n*Recorded ${dateString}*\n\n${labeledTranscript}`;
 
  const markdownPath = path.join(__dirname, `recording-${timestamp}.md`);
  fs.writeFileSync(markdownPath, markdownContent);
 
  await recapChannel.send({
    content: `📋 Meeting recap from ${dateString}`,
    files: [markdownPath]
  });
 
  await controlChannel.send(`✅ Recap from ${dateString} has been posted in <#${recapChannel.id}>.`);
 
  fs.unlinkSync(markdownPath);
  console.log('[Recorder] Recap posted.');
}

async function summarize(transcript) {
  // Try Anthropic first
  try {

    const message = await anthropic.messages.create({
      model: 'claude-opus-4-6',
      max_tokens: 1024,
      messages: [
        {
          role: 'user',
          content: `You are a Scrum assistant. Summarize the following standup meeting transcript. Each speaker is labeled in brackets. Extract and clearly list: blockers, decisions made, and action items with owners if mentioned.\n\nTranscript:\n${transcript}`
        }
      ]
    });

    console.log('[Claude] Summary received.');
    return message.content[0].text;
  } catch (err) {
    console.warn('[Claude] Failed, falling back to OpenAI:', err.message);
  }

  // Fall back to OpenAI
  try {
    const completion = await openai.chat.completions.create({
      model: 'gpt-4o-mini',
      max_tokens: 1024,
      messages: [
        {
          role: 'user',
          content: `You are a Scrum assistant. Summarize the following standup meeting transcript. Each speaker is labeled in brackets. Extract and clearly list: blockers, decisions made, and action items with owners if mentioned.\n\nTranscript:\n${transcript}`
        }
      ]
    });

    console.log('[OpenAI] Summary received.');
    return completion.choices[0].message.content;
  } catch (err) {
    console.warn('[OpenAI] Failed, falling back to raw transcript:', err.message);
  }

  return null;
}

async function handleVoiceStateUpdate(oldState, newState) {
  const guild = newState.guild;
  const voiceChannel = getChannel(guild, MEETING_VOICE_CHANNEL, 'voice');
  if (!voiceChannel) return;

  const humanCount = voiceChannel.members.filter(m => !m.user.bot).size;

  if (!recording && !starting && humanCount >= QUORUM) {
    starting = true;
    await startRecording(guild);
  }

  if (recording && humanCount === 0) {
    stopping = true;
    await stopRecording(guild);
  }
}

module.exports = { handleVoiceStateUpdate, compressAudio, summarize, formatDate, getDisplayName, transcribeMultiTrack };
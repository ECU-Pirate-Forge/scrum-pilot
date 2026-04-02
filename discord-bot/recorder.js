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
let outputPath = null;
let ffmpegProcess = null;

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

function subscribeUser(receiver, userId) {
  if (receiver.subscriptions.has(userId)) return;

  const opusStream = receiver.subscribe(userId, {
    end: { behavior: EndBehaviorType.AfterSilence, duration: 500 }
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
  opusStream.pipe(decoder).pipe(ffmpegProcess.stdin, { end: false });
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

    outputPath = path.join(__dirname, `recording-${Date.now()}.ogg`);
    const receiver = connection.receiver;

    ffmpegProcess = spawn(ffmpegStatic, [
      '-f', 's16le',
      '-ar', '48000',
      '-ac', '2',
      '-i', 'pipe:0',
      outputPath
    ]);

    ffmpegProcess.stderr.on('data', (data) => {
      console.log('[ffmpeg]', data.toString());
    });

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
    console.log('[Recorder] Recording started:', outputPath);
  });
}

async function stopRecording(guild) {
  const controlChannel = getChannel(guild, BOT_COMMANDS_CHANNEL);
  const recapChannel = getChannel(guild, RECAP_CHANNEL);

  if (ffmpegProcess) {
    await new Promise((resolve) => {
      ffmpegProcess.on('close', resolve);
      if (ffmpegProcess.stdin && !ffmpegProcess.stdin.destroyed) {
        ffmpegProcess.stdin.end();
      }
    });
    ffmpegProcess = null;
  }

  if (connection) {
    connection.destroy();
    connection = null;
  }

  recording = false;
  starting = false;
  await controlChannel.send('⏹️ Meeting ended — processing recording...');
  console.log('[Recorder] Recording stopped. Processing...');

  if (outputPath && fs.existsSync(outputPath)) {
    await transcribeAndSummarize(outputPath, recapChannel, controlChannel);
  }
}

async function compressAudio(inputPath) {
  const outputPath = inputPath.replace('.ogg', '.mp3');
  console.log('[Recorder] Compressing audio...');
  await new Promise((resolve, reject) => {
    const ffmpeg = spawn(ffmpegStatic, [
      '-i', inputPath,
      '-codec:a', 'libmp3lame',
      '-b:a', '32k',
      outputPath
    ]);
    ffmpeg.on('error', (err) => reject(new Error(`ffmpeg spawn error: ${err.message}`)));
    ffmpeg.on('close', (code) => code === 0 ? resolve() : reject(new Error(`ffmpeg exited with code ${code}`)));
  });
  return outputPath;
}

async function transcribeAndSummarize(filePath, recapChannel, controlChannel) {
  const filename = path.basename(filePath);
  const timestamp = filename.replace('recording-', '').replace('.ogg', '');
  const dateString = formatDate(timestamp);

  let compressedPath;
  try {
    compressedPath = await compressAudio(filePath);

    const MAX_WHISPER_BYTES = 25 * 1024 * 1024;
    const { size } = fs.statSync(compressedPath);
    if (size > MAX_WHISPER_BYTES) {
      fs.unlinkSync(compressedPath);
      await controlChannel.send(`⚠️ Recording is too large to transcribe (${(size / 1024 / 1024).toFixed(1)} MB). Try shorter meetings or contact an admin.`);
      return;
    }

    console.log('[Whisper] Sending to Whisper...');
    const transcription = await openai.audio.transcriptions.create({
      file: fs.createReadStream(compressedPath),
      model: 'whisper-1',
    });

    fs.unlinkSync(compressedPath);

    const transcript = transcription.text;
    console.log('[Whisper] Transcript received.');

    const summary = await summarize(transcript);

    const markdownContent = summary
      ? `# 📋 Meeting Recap\n*Recorded ${dateString}*\n\n## Summary\n${summary}\n\n## Full Transcript\n${transcript}`
      : `# 📋 Meeting Transcript\n*Recorded ${dateString}*\n\n${transcript}`;

    const markdownPath = filePath.replace('.ogg', '.md');
    fs.writeFileSync(markdownPath, markdownContent);

    await recapChannel.send({
      content: `📋 Meeting recap from ${dateString}`,
      files: [markdownPath]
    });

    await controlChannel.send(`✅ Recap from ${dateString} has been posted in <#${recapChannel.id}>.`);

    fs.unlinkSync(filePath);
    fs.unlinkSync(markdownPath);

    console.log('[Recorder] Recap posted.');
  } catch (err) {
    if (compressedPath && fs.existsSync(compressedPath)) fs.unlinkSync(compressedPath);
    const isCorrupt = err.message.includes('ffmpeg');
    const userMessage = isCorrupt
      ? `⚠️ Recording from ${dateString} appears to be corrupted and could not be processed.`
      : `⚠️ Something went wrong processing the recording from ${dateString}.`;
    console.error(`[Recorder] Error during transcription/summary:`, err);
    await controlChannel.send(userMessage); 
  }
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
          content: `You are a Scrum assistant. Summarize the following standup meeting transcript. Extract and clearly list: blockers, decisions made, and action items with owners if mentioned.\n\nTranscript:\n${transcript}`
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
          content: `You are a Scrum assistant. Summarize the following standup meeting transcript. Extract and clearly list: blockers, decisions made, and action items with owners if mentioned.\n\nTranscript:\n${transcript}`
        }
      ]
    });

    console.log('[OpenAI] Summary received.');
    return completion.choices[0].message.content;
  } catch (err) {
    console.warn('[OpenAI] Failed, falling back to raw transcript:', err.message);
  }

  // Final fallback
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
    await stopRecording(guild);
  }
}

module.exports = { handleVoiceStateUpdate, compressAudio, summarize };
'use strict';
const { joinVoiceChannel, VoiceConnectionStatus, EndBehaviorType } = require('@discordjs/voice');
const ffmpegStatic = require('ffmpeg-static');
const { spawn }   = require('child_process');
const path        = require('path');
const fs          = require('fs');
const prism       = require('prism-media');

const { callAI, openai }                              = require('./ai');
const { formatDate, getDisplayName, getChannel } = require('./utils');
const { VOICE_CHANNEL_IDS, BOT_COMMANDS_CHANNEL, RECAP_CHANNEL, QUORUM } = require('./config');

// ── Session state ─────────────────────────────────────────────────────────────

const activeSessions = new Map(); // voiceChannelId → session object
const warnedChannels = new Set(); // channels that have received the "already recording" warning

// ── Per-user track management ─────────────────────────────────────────────────

function subscribeUser(receiver, userId, session) {
  if (session.userRecordings.has(userId)) return;

  const outputPath = path.join(
    __dirname,
    `recording-${session.recordingTimestamp}-${session.channelName}-${session.categoryName}-${userId}.ogg`
  );

  const userFfmpeg = spawn(ffmpegStatic, [
    '-f', 's16le', '-ar', '48000', '-ac', '2',
    '-i', 'pipe:0',
    outputPath,
  ]);

  userFfmpeg.stderr.on('data', d => console.log(`[ffmpeg:${userId}]`, d.toString()));

  session.userRecordings.set(userId, {
    process:    userFfmpeg,
    outputPath,
    startTime:  Date.now(),
  });

  console.log(`[Recorder] Subscribed to user ${userId}`);

  const opusStream = receiver.subscribe(userId, { end: { behavior: EndBehaviorType.Manual } });
  const decoder    = new prism.opus.Decoder({ frameSize: 960, channels: 2, rate: 48000 });

  decoder.on('error',    err => console.warn(`[Recorder] Opus decode error (${userId}):`, err.message));
  opusStream.on('error', err => console.warn(`[Recorder] Opus stream error (${userId}):`, err.message));

  opusStream.pipe(decoder).pipe(userFfmpeg.stdin, { end: false });
}

// ── Recording lifecycle ───────────────────────────────────────────────────────

async function startRecording(guild, voiceChannel) {
  const controlChannel = getChannel(guild, BOT_COMMANDS_CHANNEL);
  if (!voiceChannel || !controlChannel) return;

  const connection = joinVoiceChannel({
    channelId:       voiceChannel.id,
    guildId:         guild.id,
    adapterCreator:  guild.voiceAdapterCreator,
    selfDeaf:        false,
    selfMute:        true,
  });

  connection.on('error', err => console.warn('[Recorder] Voice connection error:', err.message));

  connection.once(VoiceConnectionStatus.Ready, async () => {
    console.log('[Recorder] Connected to voice channel.');

    const session = {
      recordingTimestamp: Date.now(),
      userRecordings:     new Map(),
      connection,
      channelName:  voiceChannel.name.replace(/\s+/g, '-'),
      categoryName: (voiceChannel.parent?.name || 'uncategorized').replace(/\s+/g, '-'),
    };
    activeSessions.set(voiceChannel.id, session);

    const receiver = connection.receiver;

    // Subscribe to members already present in the channel.
    voiceChannel.members.forEach(member => {
      if (!member.user.bot) subscribeUser(receiver, member.id, session);
    });

    // Subscribe to anyone who starts speaking after we connect.
    receiver.speaking.on('start', userId => subscribeUser(receiver, userId, session));

    await controlChannel.send(`🔴 Scrumlord is listening to **${voiceChannel.name}**.`);
    console.log('[Recorder] Recording started. Timestamp:', session.recordingTimestamp);
  });
}

async function stopRecording(guild, voiceChannel) {
  const session = activeSessions.get(voiceChannel.id);
  if (!session || session.pending) return;

  // Wait for all per-user ffmpeg processes to finish writing.
  if (session.userRecordings.size > 0) {
    await Promise.all(
      [...session.userRecordings.values()].map(({ process }) =>
        new Promise(resolve => {
          process.on('close', resolve);
          if (process.stdin && !process.stdin.destroyed) process.stdin.end();
        })
      )
    );
  }

  try {
    session.connection?.destroy();
  } catch (err) {
    console.warn('[Recorder] Connection already destroyed:', err.message);
  }
  session.connection = null;

  const controlChannel = getChannel(guild, BOT_COMMANDS_CHANNEL);
  const recapChannel   = getChannel(guild, RECAP_CHANNEL);

  await controlChannel.send(`⏹️ Meeting ended — processing **${voiceChannel.name}** recording...`);
  console.log('[Recorder] Recording stopped. Processing...');

  if (session.userRecordings.size > 0) {
    await transcribeMultiTrack(
      guild,
      session.userRecordings,
      session.recordingTimestamp,
      recapChannel,
      controlChannel,
      voiceChannel
    );
  }

  session.userRecordings     = new Map();
  session.recordingTimestamp = null;
  activeSessions.delete(voiceChannel.id);
  warnedChannels.delete(voiceChannel.id);

  // If another watched channel was waiting, start recording there now.
  for (const channelId of VOICE_CHANNEL_IDS) {
    const waiting    = guild.channels.cache.get(channelId);
    if (!waiting) continue;
    const humanCount = waiting.members.filter(m => !m.user.bot).size;
    if (humanCount >= QUORUM && !activeSessions.has(channelId)) {
      warnedChannels.delete(channelId);
      activeSessions.set(channelId, { pending: true });
      await startRecording(guild, waiting);
      break;
    }
  }
}

// ── Audio processing ──────────────────────────────────────────────────────────

async function compressAudio(inputPath, controlChannel = null) {
  const outputPath        = inputPath.replace('.ogg', '.mp3');
  const silenceRemovedPath = inputPath.replace('.ogg', '-trimmed.ogg');
  const MAX_BYTES         = 25 * 1024 * 1024; // Whisper limit

  const runFfmpeg = args => new Promise((resolve, reject) => {
    const proc = spawn(ffmpegStatic, args);
    proc.on('error', err  => reject(new Error(`ffmpeg spawn error: ${err.message}`)));
    proc.on('close', code => code === 0 ? resolve() : reject(new Error(`ffmpeg exited with code ${code}`)));
  });

  // Step 1: Remove silence.
  console.log('[Recorder] Removing silence...');
  try {
    await runFfmpeg([
      '-i', inputPath,
      '-af', 'silenceremove=stop_periods=-1:stop_duration=0.5:stop_threshold=-40dB',
      silenceRemovedPath,
    ]);
  } catch {
    if (fs.existsSync(silenceRemovedPath)) fs.unlinkSync(silenceRemovedPath);
    console.warn('[Recorder] Silence removal failed — proceeding with original file.');
  }

  const fileToCompress = fs.existsSync(silenceRemovedPath) ? silenceRemovedPath : inputPath;

  // Step 2: Compress to MP3 at 32kbps.
  console.log('[Recorder] Compressing audio...');
  try {
    await runFfmpeg(['-i', fileToCompress, '-codec:a', 'libmp3lame', '-b:a', '32k', outputPath]);
  } catch {
    if (fs.existsSync(outputPath)) fs.unlinkSync(outputPath);
    console.warn('[Recorder] Compression failed — attempting error recovery at 16kbps...');
    if (controlChannel) {
      await controlChannel.send('⚠️ Audio appears corrupted — attempting recovery at reduced quality...');
    }
    await runFfmpeg(['-err_detect', 'ignore_err', '-i', fileToCompress, '-codec:a', 'libmp3lame', '-b:a', '16k', outputPath]);
  }

  if (fs.existsSync(silenceRemovedPath)) fs.unlinkSync(silenceRemovedPath);

  // Step 3: If still over 25 MB, recompress at 16kbps.
  const { size } = fs.statSync(outputPath);
  if (size > MAX_BYTES) {
    const mb = (size / 1024 / 1024).toFixed(1);
    console.warn(`[Recorder] Compressed file is ${mb} MB — retrying at 16kbps...`);
    if (controlChannel) {
      await controlChannel.send('⚠️ Recording is very long — recompressing at 16kbps to fit within transcription limits...');
    }
    fs.unlinkSync(outputPath);
    await runFfmpeg(['-i', inputPath, '-codec:a', 'libmp3lame', '-b:a', '16k', outputPath]);

    const { size: newSize } = fs.statSync(outputPath);
    if (newSize > MAX_BYTES) {
      fs.unlinkSync(outputPath);
      throw new Error(`Recording too large even at 16kbps (${(newSize / 1024 / 1024).toFixed(1)} MB)`);
    }
  }

  return outputPath;
}

// ── Multi-track transcription ─────────────────────────────────────────────────

async function transcribeMultiTrack(guild, userRecordings, timestamp, recapChannel, controlChannel, voiceChannel) {
  const dateString         = formatDate(timestamp);
  const sorted             = [...userRecordings.entries()].sort((a, b) => a[1].startTime - b[1].startTime);
  const speakerTranscripts = [];

  for (const [userId, { outputPath }] of sorted) {
    const displayName = getDisplayName(guild, userId);

    if (!fs.existsSync(outputPath)) {
      console.warn(`[Recorder] No file found for ${displayName}, skipping.`);
      continue;
    }

    // Skip tracks that are entirely silent.
    const isSilent = await new Promise(resolve => {
      const probe = spawn(ffmpegStatic, [
        '-i', outputPath,
        '-af', 'silencedetect=noise=-40dB:duration=0.5',
        '-f', 'null', '-',
      ]);
      let stderr = '';
      probe.stderr.on('data', d => { stderr += d.toString(); });
      probe.on('close', () => resolve(!stderr.includes('silence_end')));
    });

    if (isSilent) {
      console.log(`[Recorder] ${displayName}'s track is silent, skipping.`);
      fs.unlinkSync(outputPath);
      continue;
    }

    let compressedPath;
    try {
      compressedPath = await compressAudio(outputPath, controlChannel);

      console.log(`[Whisper] Transcribing ${displayName}...`);
      const transcription = await openai.audio.transcriptions.create({
        file:  fs.createReadStream(compressedPath),
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
      if (fs.existsSync(outputPath)) {
        fs.renameSync(outputPath, outputPath + '.corrupted');
        console.log(`[Recorder] Renamed to ${outputPath}.corrupted`);
      }
    }
  }

  if (speakerTranscripts.length === 0) {
    await controlChannel.send(`⚠️ No usable audio was recorded from the meeting in **${voiceChannel.name}**.`);
    return;
  }

  const labeledTranscript = speakerTranscripts
    .map(({ displayName, text }) => `[${displayName}]: ${text}`)
    .join('\n\n');

  const summary = await summarize(labeledTranscript);

  const markdownContent = summary
    ? `# 📋 Meeting Recap\n*Recorded ${dateString}*\n\n## Summary\n${summary}\n\n## Full Transcript\n${labeledTranscript}`
    : `# 📋 Meeting Transcript\n*Recorded ${dateString}*\n\n${labeledTranscript}`;

  const dateStamp = new Date(parseInt(timestamp, 10))
    .toLocaleString('en-CA', {
      timeZone: 'America/New_York',
      year: 'numeric', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit', second: '2-digit',
      hour12: false,
    })
    .replace(/[/,: ]/g, '-').replace(/--+/g, '-');

  const channelName  = voiceChannel?.name?.replace(/\s+/g, '-') || 'unknown-channel';
  const markdownPath = path.join(__dirname, `recap-${channelName}-${dateStamp}.md`);
  fs.writeFileSync(markdownPath, markdownContent);

  await recapChannel.send({
    content: `📋 Meeting recap from ${dateString} [${voiceChannel.parent?.name || 'uncategorized'}]`,
    files:   [markdownPath],
  });
  await controlChannel.send(
    `✅ Recap of **${voiceChannel.name}**'s ${dateString} meeting has been posted in <#${recapChannel.id}>.`
  );

  fs.unlinkSync(markdownPath);
  console.log('[Recorder] Recap posted.');
}

// ── Summarization ─────────────────────────────────────────────────────────────

async function summarize(transcript) {
  const prompt =
    `You are a Scrum assistant. Summarize the following standup meeting transcript. ` +
    `Each speaker is labeled in brackets. Extract and clearly list: blockers, decisions made, ` +
    `and action items with owners if mentioned.\n\nTranscript:\n${transcript}`;
  return callAI(prompt, 1024);
}

// ── Voice state event handler ─────────────────────────────────────────────────

async function handleVoiceStateUpdate(oldState, newState) {
  const guild = newState.guild;

  // Member left a watched channel.
  if (oldState.channelId && VOICE_CHANNEL_IDS.has(oldState.channelId)) {
    const leftChannel = guild.channels.cache.get(oldState.channelId);
    if (leftChannel) {
      const humanCount = leftChannel.members.filter(m => !m.user.bot).size;
      const session    = activeSessions.get(oldState.channelId);
      if (session && !session.pending && humanCount === 0) {
        await stopRecording(guild, leftChannel);
      }
    }
  }

  // Member joined a watched channel.
  if (newState.channelId && VOICE_CHANNEL_IDS.has(newState.channelId)) {
    const joinedChannel = guild.channels.cache.get(newState.channelId);
    if (!joinedChannel) return;

    const session = activeSessions.get(newState.channelId);

    if (!session && newState.member && !newState.member.user.bot) {
      if (activeSessions.size > 0) {
        // Another session is already active — warn once per channel.
        if (!warnedChannels.has(newState.channelId)) {
          warnedChannels.add(newState.channelId);
          const controlChannel = getChannel(guild, BOT_COMMANDS_CHANNEL);
          if (controlChannel) {
            await controlChannel.send(
              `⚠️ A meeting in **${joinedChannel.name}** is starting, but Scrumlord is already recording another meeting. **Please take notes manually!**`
            );
          }
        }
        return;
      }
      activeSessions.set(newState.channelId, { pending: true });
      await startRecording(guild, joinedChannel);
    } else if (session && !session.pending && newState.member && !newState.member.user.bot) {
      // Late joiner: subscribe them to the existing session.
      subscribeUser(session.connection.receiver, newState.member.id, session);
    }
  }
}

// ─────────────────────────────────────────────────────────────────────────────

module.exports = {
  handleVoiceStateUpdate,
  compressAudio,
  summarize,
  transcribeMultiTrack,
};

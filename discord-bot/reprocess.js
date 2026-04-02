require('dotenv').config();
const { Client, GatewayIntentBits } = require('discord.js');
const { compressAudio, summarize } = require('./recorder');
const fs = require('fs');
const path = require('path');
const OpenAI = require('openai');

const openai = new OpenAI({ apiKey: process.env.OPENAI_API_KEY });

const BOT_COMMANDS_CHANNEL = 'bot-commands';
const RECAP_CHANNEL = 'standup-recap';

const client = new Client({
  intents: [
    GatewayIntentBits.Guilds,
  ]
});

function getChannel(guild, name) {
  return guild.channels.cache.find(c => c.name === name && c.isTextBased());
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

async function reprocessFile(filePath, recapChannel, controlChannel) {
  const filename = path.basename(filePath);
  const timestamp = filename.replace('recording-', '').replace('.ogg', '');
  const dateString = formatDate(timestamp);

  console.log(`[Reprocess] Processing: ${filename} (recorded ${dateString})`);
  await controlChannel.send(`🔄 Reprocessing recording from ${dateString}...`);

  let compressedPath;
  try {
    compressedPath = await compressAudio(filePath);

    const MAX_WHISPER_BYTES = 25 * 1024 * 1024;
    const { size } = fs.statSync(compressedPath);
    if (size > MAX_WHISPER_BYTES) {
      fs.unlinkSync(compressedPath);
      await controlChannel.send(`⚠️ Recording from ${dateString} is too large to transcribe (${(size / 1024 / 1024).toFixed(1)} MB).`);
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
    console.log(`[Reprocess] Done: ${filename}`);
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

client.once('clientReady', async () => {
  console.log(`[Reprocess] Logged in as ${client.user.tag}`);

  const guild = client.guilds.cache.first();
  const recapChannel = getChannel(guild, RECAP_CHANNEL);
  const controlChannel = getChannel(guild, BOT_COMMANDS_CHANNEL);

  if (!recapChannel || !controlChannel) {
    console.error('[Reprocess] Could not find required channels.');
    process.exit(1);
  }

  // Find all .ogg files in the discord-bot directory
  const files = fs.readdirSync(__dirname)
    .filter(f => f.startsWith('recording-') && f.endsWith('.ogg'))
    .map(f => path.join(__dirname, f))
    .sort(); // process in chronological order

  if (files.length === 0) {
    console.log('[Reprocess] No recordings found to reprocess.');
    process.exit(0);
  }

  console.log(`[Reprocess] Found ${files.length} recording(s) to process.`);

  for (const file of files) {
    await reprocessFile(file, recapChannel, controlChannel);
  }

  console.log('[Reprocess] All done.');
  process.exit(0);
});

client.login(process.env.DISCORD_TOKEN);
'use strict';
// Standalone script — run directly with `node reprocess.js`.
// Scans the bot directory for leftover .ogg recordings and reprocesses them
// through the same transcription and summarization pipeline used during live sessions.
//
// Usage: node reprocess.js

require('dotenv').config();

const { Client, GatewayIntentBits } = require('discord.js');
const { openai }                    = require('./ai');
const { compressAudio, transcribeMultiTrack, summarize } = require('./recorder');
const fs   = require('fs');
const path = require('path');

const client = new Client({
  intents: [
    GatewayIntentBits.Guilds,
    GatewayIntentBits.GuildMembers,
  ],
});

// ── Legacy single-track handler ───────────────────────────────────────────────
// Handles the old `recording-<timestamp>.ogg` format recorded before per-user tracks.

async function reprocessSingleFile(filePath, recapChannel, controlChannel) {
  const filename    = path.basename(filePath);
  const timestamp   = filename.replace('recording-', '').replace('.ogg', '');
  const dateString  = formatDate(timestamp);

  console.log(`[Reprocess] Single-track: ${filename} (recorded ${dateString})`);
  await controlChannel.send(`🔄 Reprocessing recording from ${dateString}...`);

  let compressedPath;
  try {
    compressedPath = await compressAudio(filePath, controlChannel);

    console.log('[Whisper] Sending to Whisper...');
    const transcription = await openai.audio.transcriptions.create({
      file:  fs.createReadStream(compressedPath),
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
      files:   [markdownPath],
    });

    fs.unlinkSync(filePath);
    fs.unlinkSync(markdownPath);
    console.log(`[Reprocess] Done: ${filename}`);
  } catch (err) {
    if (compressedPath && fs.existsSync(compressedPath)) fs.unlinkSync(compressedPath);
    const isCorrupt = err.message.includes('ffmpeg');
    console.error('[Reprocess] Error:', err);
    await controlChannel.send(
      isCorrupt
        ? `⚠️ Recording from ${dateString} appears corrupted and could not be processed.`
        : `⚠️ Something went wrong processing the recording from ${dateString}.`
    );
    if (fs.existsSync(filePath)) {
      fs.renameSync(filePath, filePath + '.corrupted');
      console.log(`[Reprocess] Renamed to ${filePath}.corrupted`);
    }
  }
}

// ── Multi-track handler ───────────────────────────────────────────────────────
// Handles the current `recording-<timestamp>-<channel>-<category>-<userId>.ogg` format.

async function reprocessMultiTrack(guild, timestamp, userFiles, recapChannel, controlChannel) {
  const firstFilename = path.basename(userFiles[0].filePath);
  const nameMatch = firstFilename.match(/^recording-\d+-(.+?)-(.+?)-\d+\.ogg$/);
  const mockVoiceChannel = nameMatch
    ? { name: nameMatch[1].replace(/-/g, ' '), parent: { name: nameMatch[2].replace(/-/g, ' ') } }
    : { name: 'unknown-channel', parent: { name: 'uncategorized' } };

  const dateString = formatDate(timestamp);
  console.log(`[Reprocess] Multi-track session from ${dateString} (${userFiles.length} track(s))`);
  await controlChannel.send(`🔄 Reprocessing ${userFiles.length}-track recording from ${dateString}...`);

  // Build the userRecordings Map in the shape transcribeMultiTrack expects.
  const userRecordings = new Map();
  userFiles.forEach(({ userId, filePath }, index) => {
    userRecordings.set(userId, {
      process:    null,
      outputPath: filePath,
      startTime:  index, // no timing info from filenames — preserve sort order
    });
  });

  await transcribeMultiTrack(
    guild,
    userRecordings,
    timestamp,
    recapChannel,
    controlChannel,
    mockVoiceChannel
  );
}

// ── Main ──────────────────────────────────────────────────────────────────────

client.once('ready', async () => {
  console.log(`[Reprocess] Logged in as ${client.user.tag}`);

  const guild = client.guilds.cache.first();
  await guild.members.fetch(); // populate cache so getDisplayName resolves correctly

  const recapChannel   = getChannel(guild, RECAP_CHANNEL);
  const controlChannel = getChannel(guild, BOT_COMMANDS_CHANNEL);

  if (!recapChannel || !controlChannel) {
    console.error('[Reprocess] Could not find required channels.');
    process.exit(1);
  }

  const allOggs = fs.readdirSync(__dirname)
    .filter(f => f.endsWith('.ogg'))
    .map(f => path.join(__dirname, f))
    .sort();

  if (allOggs.length === 0) {
    console.log('[Reprocess] No recordings found.');
    process.exit(0);
  }

  // Classify recordings by filename format.
  const multiTrackGroups = new Map(); // timestamp → [{ userId, filePath }]
  const singleTrackFiles = [];

  for (const filePath of allOggs) {
    const filename = path.basename(filePath);
    // New format: recording-<ts>-<channel>-<category>-<userId>.ogg
    // Old format: recording-<ts>-<userId>.ogg
    const multiMatch  = filename.match(/^recording-(\d+)-(.+?)-(.+?)-(\d+)\.ogg$/) ||
                        filename.match(/^recording-(\d+)-(\d+)\.ogg$/);
    const singleMatch = filename.match(/^recording-(\d+)\.ogg$/);

    if (multiMatch) {
      const isNewFormat = multiMatch[0].split('-').length > 3;
      const timestamp   = multiMatch[1];
      const userId      = isNewFormat ? multiMatch[4] : multiMatch[2];
      if (!multiTrackGroups.has(timestamp)) multiTrackGroups.set(timestamp, []);
      multiTrackGroups.get(timestamp).push({ userId, filePath });
    } else if (singleMatch) {
      singleTrackFiles.push(filePath);
    }
  }

  console.log(
    `[Reprocess] Found ${singleTrackFiles.length} single-track and ` +
    `${multiTrackGroups.size} multi-track session(s).`
  );

  for (const file of singleTrackFiles) {
    await reprocessSingleFile(file, recapChannel, controlChannel);
  }

  for (const [timestamp, userFiles] of [...multiTrackGroups.entries()].sort()) {
    await reprocessMultiTrack(guild, timestamp, userFiles, recapChannel, controlChannel);
  }

  console.log('[Reprocess] All done.');
  process.exit(0);
});

client.login(process.env.DISCORD_TOKEN);

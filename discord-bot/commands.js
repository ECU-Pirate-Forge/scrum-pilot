'use strict';
// All ! command handlers, extracted from index.js.
// The main message listener in index.js calls dispatchCommand() and nothing else.

const { PermissionsBitField, AttachmentBuilder } = require('discord.js');
const { parseTimeRange, extractChannelId, fetchMessagesInRange, formatMessagesToJson } = require('./utils');
const { runSummarizer } = require('./chat-summarizer');

const READ_CHANNEL_ENV_KEY  = 'SCRUMLORD_READ_CHANNEL_ID';
const SPEAK_CHANNEL_ENV_KEY = 'SCRUMLORD_SPEAK_CHANNEL_ID';

// ── Routing helpers ───────────────────────────────────────────────────────────

function getRoutingConfig() {
  const readChannelId  = process.env[READ_CHANNEL_ENV_KEY]  || null;
  const speakChannelId = process.env[SPEAK_CHANNEL_ENV_KEY] || null;
  return {
    readChannelId,
    speakChannelId,
    isConfigured: Boolean(readChannelId && speakChannelId),
  };
}

async function resolveChannel(guild, channelId) {
  if (!guild || !channelId) return null;
  try { return await guild.channels.fetch(channelId); }
  catch { return null; }
}

/** Sends a reply to the configured speak channel, falling back to message.reply(). */
async function reply(message, content, routing) {
  if (message.guild && routing?.speakChannelId) {
    const ch = await resolveChannel(message.guild, routing.speakChannelId);
    if (ch?.send) return ch.send(content);
  }
  return message.reply(content);
}

// ── Handlers ─────────────────────────────────────────────────────────────────

async function handlePing(message, routing) {
  await reply(message, 'Pong! Scrumlord is watching. 👑', routing);
}

async function handleSetChannels(message) {
  if (!message.guild) {
    return message.reply('❌ `!setchannels` can only be used inside a server channel.');
  }
  if (!message.member?.permissions?.has(PermissionsBitField.Flags.ManageGuild)) {
    return message.reply('❌ You need the **Manage Server** permission to run this command.');
  }

  const args = message.content.trim().split(/\s+/);
  if (args.length < 3) {
    return message.reply(
      '⚠️ Usage: `!setchannels <read_channel> <speak_channel>`\n' +
      'Example: `!setchannels #team-chat #scrumlord-updates`'
    );
  }

  const readId  = extractChannelId(args[1]);
  const speakId = extractChannelId(args[2]);
  if (!readId || !speakId) {
    return message.reply('❌ Use a channel mention (`#channel`) or a raw channel ID.');
  }

  const [readCh, speakCh] = await Promise.all([
    resolveChannel(message.guild, readId),
    resolveChannel(message.guild, speakId),
  ]);

  if (!readCh || !speakCh) {
    return message.reply('❌ One or both channels could not be found in this server.');
  }
  if (!readCh.isTextBased() || !speakCh.isTextBased()) {
    return message.reply('❌ Both channels must be text-based channels.');
  }

  return message.reply(
    '✅ Add these to `discord-bot/.env` and restart the bot:\n' +
    `\`${READ_CHANNEL_ENV_KEY}=${readCh.id}\`\n` +
    `\`${SPEAK_CHANNEL_ENV_KEY}=${speakCh.id}\``
  );
}

async function handleExport(message, routing) {
  const timeArg = message.content.split(' ')[1];

  if (!timeArg) {
    return reply(message,
      '⚠️ Usage: `!export <time_range>`\n' +
      'Examples: `!export 7d` · `!export 24h` · `!export 30m`',
      routing
    );
  }

  const sinceDate = parseTimeRange(timeArg);
  if (!sinceDate) {
    return reply(message, '❌ Invalid format. Use `<number><d|h|m>` — e.g. `7d`, `24h`, `30m`.', routing);
  }

  const speakCh = (routing?.speakChannelId
    ? await resolveChannel(message.guild, routing.speakChannelId)
    : null) ?? message.channel;

  const readCh = (routing?.readChannelId
    ? await resolveChannel(message.guild, routing.readChannelId)
    : null) ?? message.channel;

  if (routing?.isConfigured && !readCh) {
    return reply(message, `❌ Configured read channel (${READ_CHANNEL_ENV_KEY}) is unavailable.`, routing);
  }

  const statusMsg = await speakCh.send(
    `⏳ Fetching messages from <#${readCh.id}> for the last ${timeArg}...`
  );

  try {
    const messages = await fetchMessagesInRange(readCh, sinceDate);

    if (messages.length === 0) {
      return statusMsg.edit(`No messages found in the last ${timeArg}.`);
    }

    const jsonBuffer = Buffer.from(formatMessagesToJson(messages), 'utf-8');
    const sizeMB     = jsonBuffer.length / (1024 * 1024);
    if (sizeMB > 8) {
      return statusMsg.edit(
        `⚠️ Export too large (${sizeMB.toFixed(2)} MB). Try a shorter time range.`
      );
    }

    const filename   = `messages-export-${readCh.id}-${new Date().toISOString().replace(/[:.]/g, '-')}.json`;
    const attachment = new AttachmentBuilder(jsonBuffer, { name: filename });

    await statusMsg.edit(`✅ Exported **${messages.length}** messages from the last ${timeArg}.`);
    await speakCh.send({ files: [attachment] });
    console.log(`[Export] ${message.author.tag} exported ${messages.length} messages from #${readCh.name}`);
  } catch (err) {
    console.error('[Export]', err);
    let msg = '❌ Failed to export messages.';
    if (err.message.includes('Missing Permissions')) msg += ' The bot lacks read permission for this channel.';
    else if (err.message.includes('Missing Access'))  msg += ' The bot cannot access this channel.';
    else msg += ` ${err.message}`;
    await reply(message, msg, routing);
  }
}

async function handleSummarize(message, client, routing) {
  // The time range is validated and surfaced in usage/errors for UX consistency,
  // but runSummarizer() always resumes from the per-channel saved cursor rather
  // than an arbitrary cutoff. To add range-limited summarization, update
  // runSummarizer() to accept a sinceDate parameter.
  const timeArg = message.content.split(' ')[1];

  if (!timeArg) {
    return reply(message,
      '⚠️ Usage: `!summarize <time_range>`\n' +
      'Examples: `!summarize 7d` · `!summarize 24h` · `!summarize 30m`',
      routing
    );
  }
  if (!parseTimeRange(timeArg)) {
    return reply(message, '❌ Invalid format. Use `<number><d|h|m>` — e.g. `7d`, `24h`, `30m`.', routing);
  }

  try {
    const statusMsg = await message.channel.send('⏳ Running summarization sweep...');
    await runSummarizer(client);
    await statusMsg.edit('✅ Summarization sweep complete.');
    console.log(`[Summarize] ${message.author.tag} triggered full sweep.`);
  } catch (err) {
    console.error('[Summarize]', err);
    await reply(message, `❌ Failed to summarize: ${err.message}`, routing);
  }
}

// ── Dispatcher ────────────────────────────────────────────────────────────────

/**
 * Routes a !command message to the appropriate handler.
 * Returns true if handled, false if the caller should continue processing.
 */
async function dispatchCommand(message, client) {
  const routing = getRoutingConfig();

  // !setchannels is allowed from any channel (no routing gate).
  if (message.content.startsWith('!setchannels')) {
    await handleSetChannels(message);
    return true;
  }

  // All other commands are restricted to the configured read channel once set.
  if (routing.isConfigured && message.guild && message.channel.id !== routing.readChannelId) {
    return false;
  }

  if (message.content === '!ping') {
    await handlePing(message, routing);
    return true;
  }
  if (message.content.startsWith('!export')) {
    await handleExport(message, routing);
    return true;
  }
  if (message.content.startsWith('!summarize')) {
    await handleSummarize(message, client, routing);
    return true;
  }

  return false;
}

module.exports = { dispatchCommand, getRoutingConfig, resolveChannel };

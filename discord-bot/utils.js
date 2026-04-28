'use strict';
// Pure utility functions — no side effects, no Discord state, no AI calls.
// Every module that needs these helpers imports from here.

const { ChannelType } = require('discord.js');

// ── Discord helpers ───────────────────────────────────────────────────────────

/** Formats a Unix millisecond timestamp into a human-readable EST date string. */
function formatDate(timestamp) {
  return new Date(parseInt(timestamp, 10)).toLocaleString('en-US', {
    weekday:      'long',
    year:         'numeric',
    month:        'long',
    day:          'numeric',
    hour:         '2-digit',
    minute:       '2-digit',
    timeZone:     'America/New_York',
    timeZoneName: 'short',
  });
}

/**
 * Returns a guild member's display name, falling back through
 * username and a User-{id} placeholder.
 */
function getDisplayName(guild, userId) {
  const member = guild.members.cache.get(userId);
  return member?.displayName || member?.user?.username || `User-${userId}`;
}

/**
 * Finds a channel in a guild by name.
 * @param {'text'|'voice'} type - Whether to look for a text or voice channel.
 */
function getChannel(guild, name, type = 'text') {
  return guild.channels.cache.find(c =>
    c.name === name &&
    (type === 'text' ? c.isTextBased() : c.type === ChannelType.GuildVoice)
  );
}

// ── Command argument parsing ──────────────────────────────────────────────────

/**
 * Parses a time range string (e.g. "7d", "24h", "30m") into a past Date.
 * Returns null if the format is invalid.
 */
function parseTimeRange(rangeStr) {
  const match = rangeStr?.match(/^(\d+)([dhm])$/);
  if (!match) return null;
  const value = parseInt(match[1], 10);
  const since = new Date();
  switch (match[2]) {
    case 'd': since.setDate(since.getDate() - value);       break;
    case 'h': since.setHours(since.getHours() - value);     break;
    case 'm': since.setMinutes(since.getMinutes() - value); break;
    default:  return null;
  }
  return since;
}

/**
 * Extracts a raw channel ID from a mention (<#123...>) or plain digit string.
 * Returns null if the input is unrecognizable.
 */
function extractChannelId(input) {
  if (!input) return null;
  const mention = input.trim().match(/^<#(\d+)>$/);
  if (mention) return mention[1];
  return /^\d+$/.test(input.trim()) ? input.trim() : null;
}

// ── Message fetching & formatting ─────────────────────────────────────────────

/**
 * Paginates through a channel and returns all messages at or after sinceDate,
 * sorted oldest-first.
 */
async function fetchMessagesInRange(channel, sinceDate) {
  const collected = [];
  let lastId = null;

  while (true) {
    const opts = { limit: 100 };
    if (lastId) opts.before = lastId;

    let batch;
    try {
      batch = await channel.messages.fetch(opts);
    } catch (err) {
      throw new Error(`Failed to fetch messages from #${channel.name}: ${err.message}`);
    }

    if (batch.size === 0) break;

    collected.push(...batch.filter(m => m.createdAt >= sinceDate).values());

    const oldest = batch.last();
    if (oldest.createdAt < sinceDate || batch.size < 100) break;
    lastId = oldest.id;
  }

  return collected.sort((a, b) => a.createdAt - b.createdAt);
}

/** Serializes an array of Discord messages to a pretty-printed JSON string. */
function formatMessagesToJson(messages) {
  return JSON.stringify(
    messages.map(m => ({
      author:    { id: m.author.id, username: m.author.username },
      content:   m.content,
      timestamp: m.createdAt.toISOString(),
    })),
    null,
    2
  );
}

// ── Date helpers ──────────────────────────────────────────────────────────────

/** Returns true if str is a valid YYYY-MM-DD date. */
function isValidDate(str) {
  return /^\d{4}-\d{2}-\d{2}$/.test(str) && !isNaN(Date.parse(str));
}

/** Returns the EST calendar date string (YYYY-MM-DD) for a Discord message. */
function estDateOf(message) {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'America/New_York' })
    .format(new Date(message.createdTimestamp));
}

/** Returns true if dateStr falls within [start, end] inclusive (ISO date strings). */
function isInRange(dateStr, start, end) {
  return dateStr >= start && dateStr <= end;
}

/**
 * Groups an array of messages by their EST calendar date.
 * @returns {Map<string, Message[]>} Keys are YYYY-MM-DD strings.
 */
function groupByDay(messages) {
  const groups = new Map();
  const fmt    = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/New_York' });
  for (const msg of messages) {
    const key = fmt.format(new Date(msg.createdTimestamp));
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(msg);
  }
  return groups;
}

// ── Network helpers ───────────────────────────────────────────────────────────

const https = require('https');

/** Downloads the text body of an HTTPS URL (e.g. a Discord attachment). */
function downloadAttachment(url) {
  return new Promise((resolve, reject) => {
    https.get(url, res => {
      let data = '';
      res.on('data',  chunk => { data += chunk; });
      res.on('end',   ()    => resolve(data));
      res.on('error', reject);
    }).on('error', reject);
  });
}

// ─────────────────────────────────────────────────────────────────────────────

module.exports = {
  formatDate,
  getDisplayName,
  getChannel,
  parseTimeRange,
  extractChannelId,
  fetchMessagesInRange,
  formatMessagesToJson,
  isValidDate,
  estDateOf,
  isInRange,
  groupByDay,
  downloadAttachment,
};

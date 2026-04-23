'use strict';
// Central configuration — all tunables live here.
// Channel names and IDs, schedule, recording parameters, sprint definitions.
// Import from this module rather than reading process.env directly in feature files.

const path = require('path');

// ── Channel names ─────────────────────────────────────────────────────────────

const BOT_COMMANDS_CHANNEL   = 'bot-commands';
const RECAP_CHANNEL          = process.env.RECAP_CHANNEL          || 'standup-recap';
const CHAT_SUMMARIES_CHANNEL = process.env.CHAT_SUMMARIES_CHANNEL || 'chat-summaries';
const SPRINT_CHANNEL         = process.env.SPRINT_CHANNEL         || 'scrumlord-generated-sprint-recaps';

// Channels the chat summarizer skips when running its sweep.
const SKIP_CHANNELS = new Set([
  BOT_COMMANDS_CHANNEL,
  RECAP_CHANNEL,
  CHAT_SUMMARIES_CHANNEL,
  SPRINT_CHANNEL,
]);

// ── Recording ─────────────────────────────────────────────────────────────────

const QUORUM = 2; // minimum humans in a voice channel to trigger recording

const VOICE_CHANNEL_IDS = new Set(
  (process.env.VOICE_CHANNELS || '').split(',').map(id => id.trim()).filter(Boolean)
);

// ── Summarization ─────────────────────────────────────────────────────────────

const SUMMARIES_DIR      = path.resolve(process.env.SUMMARIES_DIR || './summaries');
const STATE_FILE         = path.join(__dirname, 'chat-state.json');
const CHAT_SUMMARY_CRON  = process.env.CHAT_SUMMARY_CRON  || '0 2 * * *';
const SPRINT_SUMMARY_CRON = process.env.SPRINT_SUMMARY_CRON || '30 2 * * *';

// Discord category names to skip when building per-team sprint summaries.
const SPRINT_EXCLUDE_TEAMS = new Set(
  (process.env.SPRINT_EXCLUDE_TEAMS || '').split(',').map(s => s.trim()).filter(Boolean)
);

// ── Sprint schedule ───────────────────────────────────────────────────────────
// Update this array each semester — the cron and !sprintbackfill both derive from it.

const SPRINT_SCHEDULE = [
  { name: 'Sprint 1', start: '2026-02-15', end: '2026-02-28' },
  { name: 'Sprint 2', start: '2026-03-01', end: '2026-03-21' },
  { name: 'Sprint 3', start: '2026-03-22', end: '2026-04-04' },
  { name: 'Sprint 4', start: '2026-04-05', end: '2026-04-18' },
  { name: 'Sprint 5', start: '2026-04-19', end: '2026-05-02' },
];

// ─────────────────────────────────────────────────────────────────────────────

module.exports = {
  // Channels
  BOT_COMMANDS_CHANNEL,
  RECAP_CHANNEL,
  CHAT_SUMMARIES_CHANNEL,
  SPRINT_CHANNEL,
  SKIP_CHANNELS,
  // Recording
  QUORUM,
  VOICE_CHANNEL_IDS,
  // Summarization
  SUMMARIES_DIR,
  STATE_FILE,
  CHAT_SUMMARY_CRON,
  SPRINT_SUMMARY_CRON,
  SPRINT_EXCLUDE_TEAMS,
  SPRINT_SCHEDULE,
};

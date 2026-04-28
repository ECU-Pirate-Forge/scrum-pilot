'use strict';
require('dotenv').config();

const { Client, GatewayIntentBits, Events } = require('discord.js');
const cron = require('node-cron');

const { handleVoiceStateUpdate }        = require('./recorder');
const { dispatchCommand }               = require('./commands');
const { handleAgentQuery, isScrumlordThread } = require('./agent/conversate');
const { handleAIICommand }              = require('./aii/aii');
const {
  handleSprintCommand,
  runSummarizer,
  runSprintSummary,
  getTeamNames,
} = require('./chat-summarizer');
const {
  CHAT_SUMMARY_CRON,
  SPRINT_SUMMARY_CRON,
  SPRINT_SCHEDULE,
  SPRINT_EXCLUDE_TEAMS,
} = require('./config');

// ── Client ────────────────────────────────────────────────────────────────────

const client = new Client({
  intents: [
    GatewayIntentBits.Guilds,
    GatewayIntentBits.GuildMembers,
    GatewayIntentBits.GuildPresences,
    GatewayIntentBits.GuildMessages,
    GatewayIntentBits.MessageContent,
    GatewayIntentBits.GuildVoiceStates,
    GatewayIntentBits.GuildScheduledEvents,
  ],
});

// ── Ready ─────────────────────────────────────────────────────────────────────

client.once(Events.ClientReady, (readyClient) => {
  console.log(`Scrumlord is online. Logged in as ${readyClient.user.tag}`);

  // Daily chat summarization sweep.
  cron.schedule(CHAT_SUMMARY_CRON, () => {
    console.log('[ChatSummarizer] Cron fired.');
    runSummarizer(client).catch(err =>
      console.error('[ChatSummarizer] Cron error:', err)
    );
  });
  console.log(`[ChatSummarizer] Scheduled at: ${CHAT_SUMMARY_CRON}`);

  // Sprint-end detection — runs after each day's summary cron.
  cron.schedule(SPRINT_SUMMARY_CRON, async () => {
    const yesterday = new Date();
    yesterday.setDate(yesterday.getDate() - 1);
    const dateStr = yesterday.toISOString().slice(0, 10);
    const sprint  = SPRINT_SCHEDULE.find(s => s.end === dateStr);
    if (!sprint) return;

    console.log(`[SprintSummarizer] Sprint end detected: ${sprint.name}`);
    const guild = client.guilds.cache.first();
    const teams = getTeamNames(guild).filter(t => !SPRINT_EXCLUDE_TEAMS.has(t));

    for (const team of teams) {
      await runSprintSummary(guild, sprint.start, sprint.end, team).catch(err =>
        console.error(`[SprintSummarizer] Error for ${team}:`, err)
      );
      await new Promise(r => setTimeout(r, 2000));
    }
  });
  console.log(`[SprintSummarizer] Scheduled at: ${SPRINT_SUMMARY_CRON}`);
});

// ── Voice state ───────────────────────────────────────────────────────────────

client.on(Events.VoiceStateUpdate, (oldState, newState) => {
  handleVoiceStateUpdate(oldState, newState);
});

// ── Messages ──────────────────────────────────────────────────────────────────

client.on(Events.MessageCreate, async (message) => {
  if (message.author.bot) return;

  // Scrumlord-managed threads: reply without requiring a mention.
  if (message.channel.isThread()) {
    if (await isScrumlordThread(message.channel, client.user.id)) {
      await handleAgentQuery(message, client);
      return;
    }
  }

  // Direct @mention in any channel: open/continue a thread and reply.
  if (message.mentions.has(client.user)) {
    await handleAgentQuery(message, client);
    return;
  }

  // Feature-module command handlers (return true when they handle the message).
  if (await handleSprintCommand(message)) return;
  if (await handleAIICommand(message, SPRINT_SCHEDULE, getTeamNames)) return;

  // Core !commands — only processed if the message starts with !.
  if (!message.content.startsWith('!')) return;
  await dispatchCommand(message, client);
});

// ── Boot ──────────────────────────────────────────────────────────────────────

client.login(process.env.DISCORD_TOKEN);

// Export for Jest tests.
if (process.env.NODE_ENV === 'test') {
  module.exports = { client };
}

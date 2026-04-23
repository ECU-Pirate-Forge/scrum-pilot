'use strict';
// agent/conversate.js
// Conversational AI interface for Scrumlord.
//
// Triggered two ways:
//   1. @Scrumlord mention in any channel → creates a thread, responds there
//   2. Any message in a thread Scrumlord owns → responds without needing @mention
//
// Uses Claude tool use to query live server data before answering.

const { anthropic }                           = require('../ai');
const { downloadAttachment, fetchMessagesInRange } = require('../utils');
const {
  SUMMARIES_DIR,
  RECAP_CHANNEL,
  SPRINT_CHANNEL,
  SPRINT_SCHEDULE,
  STATE_FILE,
  SKIP_CHANNELS,
} = require('../config');

const fs   = require('fs');
const path = require('path');

const AII_STORE = path.join(__dirname, '../data/aii-scores.json');

const MAX_TOOL_ROUNDS  = 6;
const MAX_HISTORY_MSGS = 10;
const MAX_RESULT_CHARS = 700;
const MAX_RESULTS      = 6;

// ── Comedy ────────────────────────────────────────────────────────────────────

const NICKNAME_CHANCE = 0.05;
const NICKNAMES = [
  'Scrumdog Millionaire', 'Scrumantha', 'Scrumbrero', 'Scrumbelina',
  'Scrummy McScrumface', 'Scrum & Coke', 'Scrumplestiltskin',
  'Scrumblebee', 'Scrum & Bass', 'Scrumbled Eggs',
  'Scrum Bucket', 'Scrumbo', 'Scrumbrella-ella-ella',
];

function getBotName() {
  return Math.random() < NICKNAME_CHANCE
    ? NICKNAMES[Math.floor(Math.random() * NICKNAMES.length)]
    : 'Scrumlord';
}

// ── Managed threads ───────────────────────────────────────────────────────────
// Tracks threads Scrumlord created so it responds to all messages without
// requiring a re-mention. Survives bot restarts via the Discord API fallback.

const managedThreads = new Set();

async function isScrumlordThread(thread, botUserId) {
  if (managedThreads.has(thread.id)) return true;
  try {
    const starter = await thread.fetchStarterMessage().catch(() => null);
    if (starter?.mentions?.users.has(botUserId)) {
      managedThreads.add(thread.id);
      return true;
    }
  } catch {}
  return false;
}

// ── System prompt ─────────────────────────────────────────────────────────────

const SYSTEM_PROMPT =
  `You are Scrumlord, the AI assistant scrum master for this Discord server. ` +
  `Think of yourself as a well-informed team member who has been around for everything — ` +
  `you know the sprints, the standups, the decisions, the blockers, the scores, and when the next meeting is.\n\n` +
  `You have tools to look up real data from the server. Always use them before answering. Do not guess.\n\n` +
  `You can help with:\n` +
  `- Upcoming meetings, events, and Scrum ceremonies\n` +
  `- Sprint progress, history, and summaries\n` +
  `- Standup recaps and what the team has been up to\n` +
  `- Blockers, decisions, and action items\n` +
  `- Agile Integrity Index scores and team health\n` +
  `- Anything that has been discussed in the server channels\n\n` +
  `Since you are a Discord bot, you are also an expert on the following:\n` +
  `- The Discord platform; its features, and how to use them, especially channels and threads\n` +
  `- The server itself; its structure, and all the channels, members, teams, and so on\n` +
  `Tone: casual and friendly. You are part of the team, not a corporate helpdesk ` +
  `(though a little tongue-in-cheek corporate lingo is ok, if used for humorous effect). ` +
  `Keep answers short when the question is simple. Use bullet points for lists. ` +
  `Do not over-explain. If you are not sure about something, say so.\n\n` +
  `If someone asks you something totally unrelated to the team or project ` +
  `(like a recipe, homework, or general trivia) just let them know that is a bit outside your lane, ` +
  `and offer to help with something project-related instead. Keep it light.\n\n` +
  `When answering about times and dates, always specify the timezone (Eastern).\n\n` +
  `DATA FRESHNESS — IMPORTANT: Daily summaries are generated nightly around 2 AM ET and do not ` +
  `include messages posted since then. For any question about recent activity — what someone just ` +
  `posted, what happened today, the most recent anything — always call get_unsummarized_messages ` +
  `first. It reads directly from the summary cursor and returns everything the team has said since ` +
  `the last summary run. Only fall back to search_daily_summaries for older historical context. ` +
  `Never assume the summaries are current.`;

// ── Tool definitions ──────────────────────────────────────────────────────────

const TOOLS = [
  {
    name: 'get_server_events',
    description: 'Get upcoming (and optionally recent past) scheduled events in this Discord server. Use this for any question about meetings, ceremonies, or "when is the next X".',
    input_schema: {
      type: 'object',
      properties: {
        include_past: { type: 'boolean', description: 'Include recently completed events too. Default false.' },
      },
      required: [],
    },
  },
  {
    name: 'list_channels',
    description: 'List all text channels in the server grouped by category. Use this when you need to know what channels exist before searching one.',
    input_schema: { type: 'object', properties: {}, required: [] },
  },
  {
    name: 'get_unsummarized_messages',
    description: 'Fetch all messages posted since the last daily summary run, across every ' +
      'summarized channel. This is the definitive tool for questions about what happened today, ' +
      'what someone just posted or committed, recent PRs, or anything not yet in the summaries. ' +
      'Returns full message content — do your own semantic matching on the results. ' +
      'Always use channel_id (from the user context) when the question is about this channel ' +
      'to avoid matching the wrong channel when multiple channels share the same name.',
    input_schema: {
      type: 'object',
      properties: {
        channel_id:   { type: 'string', description: 'Preferred. Discord channel ID from user context. Unambiguous even when multiple channels share a name.' },
        channel_name: { type: 'string', description: 'Fallback if channel_id is unavailable. Channel name without #.' },
      },
      required: [],
    },
  },
  {
    name: 'search_channel_messages',
    description: 'Fetch recent messages from a specific channel, optionally filtered by keyword. ' +
      'Searches both the author name and message content. ' +
      'Use channel_id (from user context) instead of channel_name whenever possible — ' +
      'multiple channels can share the same name across different categories. ' +
      'For recent activity questions, prefer get_unsummarized_messages instead.',
    input_schema: {
      type: 'object',
      properties: {
        channel_id:   { type: 'string', description: 'Preferred. Discord channel ID. Unambiguous when multiple channels share a name.' },
        channel_name: { type: 'string', description: 'Fallback channel name (without #) if channel_id is unavailable.' },
        query:        { type: 'string', description: 'Optional keyword filter. Matches against author name and message content.' },
        limit:        { type: 'number', description: 'Max messages to return (default 20, max 50).' },
      },
      required: [],
    },
  },
  {
    name: 'search_daily_summaries',
    description: 'Search the nightly-generated channel activity summaries stored on disk. ' +
      'These are generated once per day (around 2 AM ET) and DO NOT include today\'s messages ' +
      'or anything posted since the last run. Use this for historical questions about past days, ' +
      'decisions, and discussions. For anything recent or current, use search_channel_messages instead.',
    input_schema: {
      type: 'object',
      properties: {
        query:        { type: 'string', description: 'Keywords or phrase to search for.' },
        start_date:   { type: 'string', description: 'Optional. YYYY-MM-DD.' },
        end_date:     { type: 'string', description: 'Optional. YYYY-MM-DD.' },
        channel_name: { type: 'string', description: 'Optional. Restrict to one channel.' },
      },
      required: ['query'],
    },
  },
  {
    name: 'get_sprint_summary',
    description: 'Get sprint summary content — accomplishments, blockers, decisions, carry-over.',
    input_schema: {
      type: 'object',
      properties: {
        sprint_name: { type: 'string', description: 'Optional. E.g. "Sprint 3".' },
        team_name:   { type: 'string', description: 'Optional. Team name.' },
      },
      required: [],
    },
  },
  {
    name: 'get_standup_recaps',
    description: 'Get standup meeting recaps — who attended, what was discussed, blockers, action items.',
    input_schema: {
      type: 'object',
      properties: {
        team_name:  { type: 'string', description: 'Optional. Filter by team.' },
        start_date: { type: 'string', description: 'Optional. YYYY-MM-DD.' },
        end_date:   { type: 'string', description: 'Optional. YYYY-MM-DD.' },
        limit:      { type: 'number', description: 'Max recaps to return. Default 5.' },
      },
      required: [],
    },
  },
  {
    name: 'get_aii_scores',
    description: 'Get Agile Integrity Index scores — overall score, section breakdowns, anti-patterns, grade.',
    input_schema: {
      type: 'object',
      properties: {
        team_name:   { type: 'string', description: 'Optional. Filter by team.' },
        sprint_name: { type: 'string', description: 'Optional. E.g. "Sprint 3".' },
      },
      required: [],
    },
  },
];

// ── Helpers ───────────────────────────────────────────────────────────────────

function trunc(str, max = MAX_RESULT_CHARS) {
  if (!str) return '';
  return str.length > max ? str.slice(0, max).trimEnd() + ' ...' : str;
}

function formatEST(date) {
  if (!date) return 'TBD';
  return new Date(date).toLocaleString('en-US', {
    weekday: 'long', month: 'short', day: 'numeric',
    hour: 'numeric', minute: '2-digit',
    timeZone: 'America/New_York', timeZoneName: 'short',
  });
}

// ── Tool implementations ──────────────────────────────────────────────────────

async function toolGetServerEvents(guild, input) {
  const include_past = input.include_past || false;
  try {
    const events  = await guild.scheduledEvents.fetch();
    const results = [];
    events.forEach(ev => {
      const isPast = ev.scheduledEndAt && new Date(ev.scheduledEndAt) < new Date();
      if (isPast && !include_past) return;
      results.push({
        name:        ev.name,
        description: ev.description || null,
        starts:      formatEST(ev.scheduledStartAt),
        ends:        ev.scheduledEndAt ? formatEST(ev.scheduledEndAt) : null,
        status:      ev.status,
        channel:     ev.channel?.name || ev.entityMetadata?.location || null,
      });
    });
    return results.length === 0
      ? { message: include_past ? 'No scheduled events found.' : 'No upcoming events right now.' }
      : { count: results.length, events: results };
  } catch (err) {
    return { error: 'Could not fetch events: ' + err.message };
  }
}

function toolListChannels(guild) {
  const byCategory = {};
  guild.channels.cache
    .filter(c => c.isTextBased() || c.type === 2)
    .sort((a, b) => (a.rawPosition || 0) - (b.rawPosition || 0))
    .forEach(c => {
      const cat = c.parent?.name || 'Uncategorized';
      if (!byCategory[cat]) byCategory[cat] = [];
      byCategory[cat].push({ name: c.name, type: c.type === 2 ? 'voice' : 'text' });
    });
  return byCategory;
}

async function toolSearchChannelMessages(guild, input, sourceChannelId = null) {
  const channel_id   = input.channel_id   || null;
  const channel_name = input.channel_name || null;
  const query        = input.query        || null;
  const limit        = Math.min(input.limit || 20, 50);

  // Resolve channel: prefer ID (unambiguous), fall back to name search.
  let ch = null;
  if (channel_id) {
    ch = guild.channels.cache.get(channel_id);
  } else if (channel_name) {
    ch = guild.channels.cache.find(
      c => c.name.toLowerCase() === channel_name.toLowerCase() && c.isTextBased()
    );
  } else if (sourceChannelId) {
    ch = guild.channels.cache.get(sourceChannelId);
  }

  if (!ch) {
    return { error: channel_name
      ? `Channel #${channel_name} not found. Try list_channels to see available channels.`
      : 'No channel specified. Provide channel_id or channel_name.' };
  }

  let batch;
  try { batch = await ch.messages.fetch({ limit: 100 }); }
  catch (err) { return { error: `Could not read #${ch.name}: ${err.message}` }; }

  let msgs = [...batch.values()].filter(m => !m.author.bot || m.embeds.length > 0);

  if (query) {
    // Split into individual terms and match against author name + content.
    const terms = query.toLowerCase().split(/\s+/).filter(Boolean);
    msgs = msgs.filter(m => {
      const haystack = `${m.member?.displayName || m.author.username} ${m.content}`.toLowerCase();
      return terms.some(t => haystack.includes(t));
    });
  }

  msgs = msgs.slice(0, limit);
  if (msgs.length === 0) {
    return { message: `No messages found in #${ch.name}${query ? ` matching "${query}"` : ''}.` };
  }

  return {
    channel:  ch.name,
    category: ch.parent?.name || null,
    count:    msgs.length,
    messages: msgs.map(m => ({
      author:    m.member?.displayName || m.author.username,
      content:   trunc(m.content, 400),
      timestamp: formatEST(m.createdAt),
    })),
  };
}

function toolSearchDailySummaries(input) {
  const { query, start_date = null, end_date = null, channel_name = null } = input;

  if (!fs.existsSync(SUMMARIES_DIR)) return { message: 'No daily summaries on disk yet.' };

  // Split the query into individual terms and require ALL to appear in the file.
  // This avoids the failure mode where "Tyler pull request PR" is treated as an
  // exact phrase — no summary file would contain that exact string.
  const terms = query.toLowerCase().split(/\s+/).filter(Boolean);
  const results = [];
  let done = false;

  const dirs = fs.readdirSync(SUMMARIES_DIR).filter(d => {
    if (!fs.statSync(path.join(SUMMARIES_DIR, d)).isDirectory()) return false;
    return !channel_name || d.toLowerCase() === channel_name.toLowerCase();
  });

  for (const dir of dirs) {
    if (done) break;
    const files = fs.readdirSync(path.join(SUMMARIES_DIR, dir))
      .filter(f => {
        if (!f.endsWith('.md')) return false;
        const date = f.replace('.md', '');
        if (start_date && date < start_date) return false;
        if (end_date   && date > end_date)   return false;
        return true;
      })
      .sort().reverse();

    for (const file of files) {
      if (done) break;
      const raw = fs.readFileSync(path.join(SUMMARIES_DIR, dir, file), 'utf8');
      const lower = raw.toLowerCase();
      // Require every search term to appear somewhere in the file.
      if (!terms.every(t => lower.includes(t))) continue;
      const match = raw.match(/## Summary\n([\s\S]*?)(?=\n## |$)/);
      results.push({
        channel: dir,
        date:    file.replace('.md', ''),
        excerpt: trunc(match ? match[1].trim() : raw.slice(0, 600)),
      });
      if (results.length >= MAX_RESULTS) done = true;
    }
  }

  return results.length === 0
    ? { message: `Nothing in the daily summaries matched "${query}".` }
    : { count: results.length, results };
}

function toolGetAIIScores(input) {
  const { team_name = null, sprint_name = null } = input;
  let all;
  try { all = JSON.parse(fs.readFileSync(AII_STORE, 'utf8')); }
  catch { return { message: 'No AII scores yet. Run !aiibackfill first.' }; }

  let scores = all;
  if (team_name)   scores = scores.filter(s => s.team?.toLowerCase()   === team_name.toLowerCase());
  if (sprint_name) scores = scores.filter(s => s.sprint?.toLowerCase() === sprint_name.toLowerCase());
  if (scores.length === 0) return { message: 'No AII scores matched those filters.' };

  const grades = ['F','F','F','F','F','F','D','C','B','A'];
  return {
    count: scores.length,
    scores: scores.map(s => ({
      team:            s.team,
      sprint:          s.sprint,
      period:          `${s.start} to ${s.end}`,
      aii_total:       s.aii_total,
      grade:           grades[Math.floor(s.aii_total / 10)] || 'F',
      section_a:       s.section_a?.subtotal,
      section_b:       s.section_b?.subtotal,
      section_c:       s.section_c?.subtotal,
      antipatterns:    (s.section_c?.antipatterns ?? []).map(a => `${a.label} (-${a.deduction})`),
      data_confidence: s.data_confidence,
      notes:           trunc(s.scoring_notes, 250),
    })),
  };
}

async function toolGetStandupRecaps(guild, input) {
  const { team_name = null, start_date = null, end_date = null, limit = 5 } = input;

  const ch = guild.channels.cache.find(c => c.name === RECAP_CHANNEL && c.isTextBased());
  if (!ch) return { error: `#${RECAP_CHANNEL} not found.` };

  const since = start_date ? new Date(start_date) : new Date(Date.now() - 30 * 24 * 60 * 60 * 1000);
  const until = end_date   ? new Date(end_date)   : new Date();
  until.setDate(until.getDate() + 1);

  const collected = [];
  let lastId = null;

  while (collected.length < limit * 2) {
    const opts = { limit: 100 };
    if (lastId) opts.before = lastId;
    let batch;
    try { batch = await ch.messages.fetch(opts); } catch { break; }
    if (batch.size === 0) break;

    const msgs = [...batch.values()];
    let hitFloor = false;

    for (const msg of msgs) {
      if (msg.createdTimestamp < since.getTime()) { hitFloor = true; break; }
      if (msg.createdTimestamp > until.getTime()) continue;
      if (team_name) {
        const tm = msg.content.match(/\[(.+?)\]/);
        if (!tm || tm[1].toLowerCase() !== team_name.toLowerCase()) continue;
      }
      if (msg.attachments.size > 0) collected.push(msg);
    }

    if (hitFloor || batch.size < 100) break;
    lastId = msgs[msgs.length - 1].id;
    await new Promise(r => setTimeout(r, 300));
  }

  const subset = collected.slice(0, limit);
  if (subset.length === 0) return { message: 'No standup recaps found for those filters.' };

  const results = [];
  for (const msg of subset) {
    const att = msg.attachments.first();
    if (!att) continue;
    try {
      const raw   = await downloadAttachment(att.url);
      const match = raw.match(/## Summary\n([\s\S]*?)(?=\n## |$)/);
      results.push({
        label:   msg.content.split('\n')[0],
        summary: trunc(match ? match[1].trim() : raw.slice(0, 600)),
      });
    } catch {}
  }

  return { count: results.length, recaps: results };
}

async function toolGetSprintSummary(guild, input) {
  const { sprint_name = null, team_name = null } = input;

  const ch = guild.channels.cache.find(c => c.name === SPRINT_CHANNEL && c.isTextBased());
  if (!ch) return { error: `#${SPRINT_CHANNEL} not found.` };

  const results = [];
  let lastId    = null;

  while (results.length < MAX_RESULTS) {
    const opts = { limit: 100 };
    if (lastId) opts.before = lastId;
    let batch;
    try { batch = await ch.messages.fetch(opts); } catch { break; }
    if (batch.size === 0) break;

    for (const msg of [...batch.values()]) {
      if (!msg.content.startsWith('## 🏁')) continue;
      if (sprint_name && !msg.content.toLowerCase().includes(sprint_name.toLowerCase())) continue;
      if (team_name   && !msg.content.toLowerCase().includes(team_name.toLowerCase()))   continue;

      let body = '';
      const thread = msg.thread || await msg.fetchThread?.().catch(() => null);
      if (thread) {
        try {
          const tMsgs = await thread.messages.fetch({ limit: 50 });
          body = [...tMsgs.values()]
            .filter(m => m.author.bot && m.content && !m.content.startsWith('---') && !m.content.startsWith('📊'))
            .sort((a, b) => a.createdTimestamp - b.createdTimestamp)
            .map(m => m.content)
            .join('\n\n');
        } catch {}
      }

      results.push({
        header:  msg.content.split('\n')[0].replace(/^## 🏁\s*/, ''),
        summary: trunc(body, 1200),
      });
    }

    if (batch.size < 100) break;
    lastId = [...batch.keys()].pop();
  }

  return results.length === 0
    ? { message: 'No sprint summaries matched those filters.' }
    : { count: results.length, summaries: results };
}

// Tool: get_unsummarized_messages
async function toolGetUnsummarizedMessages(guild, input, sourceChannelId = null) {
  const { channel_id = null, channel_name = null } = input;

  // Load cursor state. Keys are channel IDs, values are the createdTimestamp
  // of the last message the daily summarizer processed for that channel.
  // Channels without a cursor (not yet summarized) fall back to 24h ago.
  let state = {};
  try {
    state = JSON.parse(fs.readFileSync(STATE_FILE, 'utf8'));
  } catch {
    // No state file yet — 24h fallback applies to every channel.
  }

  const MAX_PER_CH = 40;
  const MAX_TOTAL  = 120;
  const FALLBACK_MS = 24 * 60 * 60 * 1000;
  const GUILD_TEXT  = 0;

  // Resolve which channels to search.
  // Priority: explicit channel_id > channel_name > source channel > all channels.
  const resolvedId = channel_id || (channel_name ? null : sourceChannelId);

  let channelsToSearch;
  if (resolvedId) {
    // Single channel by ID — unambiguous.
    const ch = guild.channels.cache.get(resolvedId);
    channelsToSearch = ch && ch.type === GUILD_TEXT && !SKIP_CHANNELS.has(ch.name)
      ? new Map([[ch.id, ch]])
      : new Map();
  } else if (channel_name) {
    // Filter all channels by name — may match more than one.
    channelsToSearch = guild.channels.cache.filter(ch =>
      ch.type === GUILD_TEXT &&
      !SKIP_CHANNELS.has(ch.name) &&
      ch.name.toLowerCase() === channel_name.toLowerCase()
    );
  } else {
    // No filter — scan everything.
    channelsToSearch = guild.channels.cache.filter(ch =>
      ch.type === GUILD_TEXT && !SKIP_CHANNELS.has(ch.name)
    );
  }

  if (channelsToSearch.size === 0) {
    return { message: 'No matching channels found.' };
  }

  const channelGroups = [];
  let   totalSoFar    = 0;

  for (const [, ch] of channelsToSearch) {
    if (totalSoFar >= MAX_TOTAL) break;

    const cursorTs  = state[ch.id] ?? null;
    const sinceDate = cursorTs
      ? new Date(cursorTs)
      : new Date(Date.now() - FALLBACK_MS);

    let msgs;
    try {
      msgs = await fetchMessagesInRange(ch, sinceDate);
    } catch {
      continue;
    }

    // Drop bot messages. No keyword filtering here — return everything
    // and let the model do semantic matching on the full result.
    msgs = msgs.filter(m => !m.author.bot && m.content.trim());
    if (msgs.length === 0) continue;

    const capped = msgs.slice(-MAX_PER_CH);
    totalSoFar  += capped.length;

    channelGroups.push({
      channel:  ch.name,
      category: ch.parent?.name || null,
      since:    cursorTs ? formatEST(new Date(cursorTs)) : 'last 24 hours (no summary cursor)',
      messages: capped.map(m => ({
        author:    m.member?.displayName || m.author.username,
        content:   trunc(m.content, 400),
        timestamp: formatEST(m.createdAt),
      })),
    });
  }

  if (channelGroups.length === 0) {
    return { message: 'No recent messages found since the last summary run.' };
  }

  return {
    note:           'Messages since the last daily summary run (or last 24h for un-summarized channels).',
    total_messages: totalSoFar,
    channels:       channelGroups,
  };
}

// ── Tool dispatcher ───────────────────────────────────────────────────────────

async function executeTool(guild, name, input, sourceChannelId = null) {
  console.log(`[Agent] Tool: ${name}`, JSON.stringify(input));
  try {
    switch (name) {
      case 'get_unsummarized_messages': return await toolGetUnsummarizedMessages(guild, input, sourceChannelId);
      case 'get_server_events':         return await toolGetServerEvents(guild, input);
      case 'list_channels':             return toolListChannels(guild);
      case 'search_channel_messages':   return await toolSearchChannelMessages(guild, input, sourceChannelId);
      case 'search_daily_summaries':    return toolSearchDailySummaries(input);
      case 'get_aii_scores':            return toolGetAIIScores(input);
      case 'get_standup_recaps':        return await toolGetStandupRecaps(guild, input);
      case 'get_sprint_summary':        return await toolGetSprintSummary(guild, input);
      default:                          return { error: `Unknown tool: ${name}` };
    }
  } catch (err) {
    console.error(`[Agent] Tool error (${name}):`, err.message);
    return { error: err.message };
  }
}

// ── Thread history ────────────────────────────────────────────────────────────

async function getThreadHistory(thread, botUserId) {
  let batch;
  try { batch = await thread.messages.fetch({ limit: MAX_HISTORY_MSGS + 2 }); }
  catch { return []; }

  return [...batch.values()]
    .sort((a, b) => a.createdTimestamp - b.createdTimestamp)
    .slice(-MAX_HISTORY_MSGS)
    .filter(m => m.content?.trim())
    .map(m => ({
      role:    m.author.id === botUserId ? 'assistant' : 'user',
      content: m.content.replace(/<@!?\d+>/g, '').trim(),
    }))
    .filter(m => m.content.length > 0);
}

// ── Main handler ──────────────────────────────────────────────────────────────

async function handleAgentQuery(message, client) {
  const guild = message.guild;
  if (!guild) return;

  const botMentionRe = new RegExp(`<@!?${client.user.id}>`, 'g');
  const question = message.content
    .replace(botMentionRe, getBotName())
    .replace(/<@!?\d+>/g, '')
    .trim();

  // Resolve or create a thread.
  let thread;
  if (message.channel.isThread()) {
    thread = message.channel;
    managedThreads.add(thread.id);
  } else {
    const threadName = question.replace(/[`|]/g, '').trim().slice(0, 80) +
      (question.length > 80 ? '...' : '') || 'Scrumlord';
    try {
      thread = await message.startThread({ name: threadName, autoArchiveDuration: 1440 });
      managedThreads.add(thread.id);
    } catch {
      thread = message.channel;
    }
  }

  if (!question) {
    await thread.send("Hey! What can I help with? Ask me about meetings, sprints, standups, scores — anything going on in the server.");
    return;
  }

  await thread.sendTyping().catch(() => {});
  const typingInterval = setInterval(() => thread.sendTyping().catch(() => {}), 8000);

  try {
    const history = message.channel.isThread() ? await getThreadHistory(thread, client.user.id) : [];

    const displayName = message.member?.displayName || message.author.username;
    const username    = message.author.username;

    // Best-effort team detection from channel category or role names.
    let userTeam = message.channel.parent?.name || null;
    if (!userTeam && message.member?.roles) {
      const teamCategories = new Set();
      guild.channels.cache.forEach(c => {
        if (c.parent?.name) teamCategories.add(c.parent.name.toLowerCase());
      });
      message.member.roles.cache.forEach(role => {
        if (!userTeam && teamCategories.has(role.name.toLowerCase())) userTeam = role.name;
      });
    }

    // The source channel is the channel the message was posted in, or the
    // parent channel if the message is inside a thread Scrumlord created.
    const sourceChannel     = message.channel.isThread() ? message.channel.parent : message.channel;
    const sourceChannelName = sourceChannel?.name || null;

    const userContext =
      `The person asking is ${displayName} (Discord username: ${username})` +
      (displayName !== username ? ` (display name: ${displayName})` : '') +
      (userTeam ? `. Their team is ${userTeam}.` : '') +
      (sourceChannelName ? ` They are posting from #${sourceChannelName}` : '') +
      (sourceChannel?.id ? ` (channel_id: ${sourceChannel.id}).` : '.') +
      ' Use this to answer questions about "I", "me", or "my" without asking.' +
      (sourceChannel?.id
        ? ` When looking for recent messages in this channel, pass channel_id="${sourceChannel.id}" to avoid matching a same-named channel from a different category.`
        : '');

    const today = new Date().toLocaleDateString('en-US', {
      timeZone: 'America/New_York', weekday: 'long', year: 'numeric', month: 'long', day: 'numeric',
    });

    // Guard: SPRINT_SCHEDULE should always be a populated array from config.js.
    // This check prevents a crash if something goes wrong at import time.
    const scheduleLines = Array.isArray(SPRINT_SCHEDULE) && SPRINT_SCHEDULE.length > 0
      ? SPRINT_SCHEDULE.map(s => `- ${s.name}: ${s.start} to ${s.end}`).join('\n')
      : '(no sprint schedule configured)';

    const sprintContext = `Today is ${today}.\n\nSprint schedule:\n${scheduleLines}`;
    const system        = `${SYSTEM_PROMPT}\n\n${sprintContext}`;

    let messages = [...history, { role: 'user', content: `${userContext}\n${question}` }];
    let response;

    for (let round = 0; round < MAX_TOOL_ROUNDS; round++) {
      response = await anthropic.messages.create({
        model:      'claude-sonnet-4-6',
        max_tokens: 600,
        system,
        tools:      TOOLS,
        messages,
      });

      if (response.stop_reason !== 'tool_use') break;

      messages = [...messages, { role: 'assistant', content: response.content }];

      const toolResults = await Promise.all(
        response.content
          .filter(b => b.type === 'tool_use')
          .map(async b => ({
            type:        'tool_result',
            tool_use_id: b.id,
            content:     JSON.stringify(await executeTool(guild, b.name, b.input, sourceChannel?.id)),
          }))
      );

      messages = [...messages, { role: 'user', content: toolResults }];
    }

    const textBlock = response?.content?.find(b => b.type === 'text');
    const text      = textBlock?.text?.trim();

    if (!text) {
      await thread.send("Hmm, I couldn't put together a good answer for that. Try rephrasing?");
      return;
    }

    // Send in ≤1950-char chunks, splitting at newlines where possible.
    let remaining = text;
    while (remaining.length > 0) {
      let cutAt = Math.min(1950, remaining.length);
      if (remaining.length > 1950) {
        const nl = remaining.lastIndexOf('\n', 1950);
        if (nl > 900) cutAt = nl + 1;
      }
      await thread.send(remaining.slice(0, cutAt));
      remaining = remaining.slice(cutAt);
      if (remaining.length > 0) await new Promise(r => setTimeout(r, 400));
    }

  } catch (err) {
    console.error('[Agent] Error:', err);
    await thread.send('Something went wrong on my end: ' + err.message).catch(() => {});
  } finally {
    clearInterval(typingInterval);
  }
}

module.exports = { handleAgentQuery, isScrumlordThread, managedThreads };
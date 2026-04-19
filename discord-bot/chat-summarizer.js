require('dotenv').config();
const { ChannelType } = require('discord.js');
const cron = require('node-cron');
const fs = require('fs');
const path = require('path');
const https = require('https');
const Anthropic = require('@anthropic-ai/sdk');
const OpenAI = require('openai');

// ─── Config ───────────────────────────────────────────────────────────────────

const CHAT_SUMMARIES_CHANNEL = process.env.CHAT_SUMMARIES_CHANNEL || 'chat-summaries';
const SUMMARIES_DIR          = path.resolve(process.env.SUMMARIES_DIR || './summaries');
const CHAT_SUMMARY_CRON      = process.env.CHAT_SUMMARY_CRON || '0 2 * * *';
const SPRINT_SUMMARY_CRON = process.env.SPRINT_SUMMARY_CRON || '30 2 * * *';
const RECAP_CHANNEL  = process.env.RECAP_CHANNEL  || 'standup-recap';
const SPRINT_CHANNEL = process.env.SPRINT_CHANNEL || RECAP_CHANNEL;
const STATE_FILE             = path.join(__dirname, 'chat-state.json');
const SPRINT_EXCLUDE_TEAMS = new Set(
  (process.env.SPRINT_EXCLUDE_TEAMS || '').split(',').map(s => s.trim()).filter(Boolean)
);

const SKIP_CHANNELS = new Set([
  CHAT_SUMMARIES_CHANNEL,
  process.env.RECAP_CHANNEL || 'standup-recap',
  process.env.BOT_COMMANDS  || 'bot-commands',
  process.env.SPRINT_CHANNEL   || 'scrumlord-generated-sprint-recaps',
]);

const anthropic = new Anthropic({ apiKey: process.env.ANTHROPIC_API_KEY });
const openai    = new OpenAI({ apiKey: process.env.OPENAI_API_KEY });

const SPRINT_SCHEDULE = [
  { name: 'Sprint 1', start: '2026-02-15', end: '2026-02-28' },
  { name: 'Sprint 2', start: '2026-03-01', end: '2026-03-21' },
  { name: 'Sprint 3', start: '2026-03-22', end: '2026-04-04' },
  { name: 'Sprint 4', start: '2026-04-05', end: '2026-04-18' },
  { name: 'Sprint 5', start: '2026-04-19', end: '2026-05-02' },
];

// ─── Moved from index.js ──────────────────────────────────────────────────────

// Fetch messages from a channel within a time range
async function fetchMessagesInRange(channel, sinceDate) {
  const collectedMessages = [];
  let lastMessageId = null;

  try {
    while (true) {
      const options = { limit: 100 };
      if (lastMessageId) options.before = lastMessageId;

      const messages = await channel.messages.fetch(options);
      if (messages.size === 0) break;

      const filteredMessages = messages.filter(
        (msg) => msg.createdAt >= sinceDate
      );
      collectedMessages.push(...filteredMessages.values());

      const oldestMessage = messages.last();
      if (oldestMessage.createdAt < sinceDate) break;

      lastMessageId = oldestMessage.id;
      if (messages.size < 100) break;
    }

    collectedMessages.sort((a, b) => a.createdAt - b.createdAt);
    return collectedMessages;
  } catch (error) {
    throw new Error(`Failed to fetch messages: ${error.message}`);
  }
}

// Format messages to JSON (used by !export)
function formatMessagesToJson(messages) {
  const formattedMessages = messages.map((msg) => ({
    author: {
      id: msg.author.id,
      username: msg.author.username,
    },
    content: msg.content,
    timestamp: msg.createdAt.toISOString(),
  }));
  return JSON.stringify(formattedMessages, null, 2);
}

// ─── State helpers ────────────────────────────────────────────────────────────

function loadState() {
  try {
    return JSON.parse(fs.readFileSync(STATE_FILE, 'utf8'));
  } catch {
    return {};
  }
}

function saveState(state) {
  fs.writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
}

// ─── Continuity context ───────────────────────────────────────────────────────
// Loads all prior summary sections (not raw logs) for a channel to keep the
// context window lean while still giving the AI full history awareness.

function loadPriorSummaries(channelName) {
  const dir = path.join(SUMMARIES_DIR, channelName);
  if (!fs.existsSync(dir)) return '';

  const files = fs.readdirSync(dir)
    .filter(f => f.endsWith('.md'))
    .sort(); // YYYY-MM-DD sorts chronologically

  if (files.length === 0) return '';

  return files.map(f => {
    const date    = f.replace('.md', '');
    const content = fs.readFileSync(path.join(dir, f), 'utf8');
    const match   = content.match(/## Summary\n([\s\S]*?)(?=\n## |$)/);
    const summary = match ? match[1].trim() : content.trim();
    return `[${date}]\n${summary}`;
  }).join('\n\n');
}

// ─── AI summarizer ────────────────────────────────────────────────────────────
// Claude → GPT-4o-mini → null (same fallback chain as recorder.js)

async function summarizeMessages(channelName, dateStr, messages, priorContext) {
  const messageText = (await Promise.all(
    messages
      .filter(m => !m.author.bot && m.content.trim())
      .map(async m => {
        const member = await m.guild.members.fetch(m.author.id).catch(() => null);
        const displayName = member?.nickname || m.author.displayName || m.author.username;
        return `[${displayName}]: ${m.content}`;
      })
  )).join('\n');

  if (!messageText.trim()) return null;

  const systemPrompt =
    `You are a Scrum project assistant summarizing Discord channel activity for a software team.\n` +
    `Extract: decisions made, tasks mentioned, blockers raised, progress updates, and open questions.\n` +
    `If a topic continues from prior summaries, flag it with "(ongoing)".\n` +
    `Ignore casual small talk unless it contains project-relevant information.\n` +
    `Be concise. Use bullet points.`;

  const userPrompt = priorContext
    ? `Prior summaries for #${channelName} (continuity context):\n${priorContext}\n\n` +
      `---\n\nNew messages from ${dateStr}:\n${messageText}\n\n` +
      `Summarize today's activity. Flag continuations with "(ongoing)".`
    : `Messages from #${channelName} on ${dateStr}:\n${messageText}\n\nSummarize the key activity.`;

  // Try Claude
  try {
    const response = await anthropic.messages.create({
      model: 'claude-opus-4-6',
      max_tokens: 1024,
      messages: [{ role: 'user', content: `${systemPrompt}\n\n${userPrompt}` }],
    });
    console.log(`[Claude] Summary generated for #${channelName} on ${dateStr}`);
    return response.content[0].text;
  } catch (err) {
    console.warn(`[Claude] Failed for #${channelName} — falling back to OpenAI:`, err.message);
  }

  // Fallback: GPT-4o-mini
  try {
    const response = await openai.chat.completions.create({
      model: 'gpt-4o-mini',
      max_tokens: 1024,
      messages: [
        { role: 'system', content: systemPrompt },
        { role: 'user',   content: userPrompt   },
      ],
    });
    console.log(`[OpenAI] Summary generated for #${channelName} on ${dateStr}`);
    return response.choices[0].message.content;
  } catch (err) {
    console.warn(`[OpenAI] Failed for #${channelName}:`, err.message);
  }

  return null;
}

// ─── Markdown writer ──────────────────────────────────────────────────────────

async function writeMarkdown(channelName, dateStr, summary, messages) {
  const dir = path.join(SUMMARIES_DIR, channelName);
  fs.mkdirSync(dir, { recursive: true });

  const filePath = path.join(dir, `${dateStr}.md`);
  const rawLog = (await Promise.all(
    messages
      .filter(m => !m.author.bot && m.content.trim())
      .map(async m => {
        const member = await m.guild.members.fetch(m.author.id).catch(() => null);
        const displayName = member?.nickname || m.author.displayName || m.author.username;
        const time = new Date(m.createdTimestamp).toLocaleTimeString('en-US', {
          hour: '2-digit', minute: '2-digit',
          timeZone: 'America/New_York', timeZoneName: 'short',
        });
        return `**[${time}] ${displayName}:** ${m.content}`;
      })
  )).join('\n');

  const content = summary
    ? `# #${channelName} — ${dateStr}\n\n## Summary\n${summary}\n\n## Raw Log\n${rawLog}`
    : `# #${channelName} — ${dateStr}\n\n*No AI summary available.*\n\n## Raw Log\n${rawLog}`;

  fs.writeFileSync(filePath, content, 'utf8');
  return filePath;
}

// ─── Group messages by calendar day (EST) ────────────────────────────────────

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

// ─── Summarize a specific channel over a time range ──────────────────────────
// Used by both the cron job and the !summarize command in index.js.

async function summarizeChannel(channel, sinceDate, outputChannel) {
  const channelName = channel.name;
  const messages    = await fetchMessagesInRange(channel, sinceDate);

  if (messages.length === 0) {
    console.log(`[ChatSummarizer] No messages in #${channelName} since ${sinceDate.toISOString()}`);
    return null;
  }

  console.log(`[ChatSummarizer] ${messages.length} message(s) in #${channelName} to summarize.`);

  const dayGroups    = groupByDay(messages);
  const priorContext = loadPriorSummaries(channelName);

  for (const [dateStr, dayMessages] of [...dayGroups.entries()].sort()) {
    if (dayMessages.length === 0) continue;

    const summary  = await summarizeMessages(channelName, dateStr, dayMessages, priorContext);
    const filePath = await writeMarkdown(channelName, dateStr, summary, dayMessages);

    try {
      await outputChannel.send({
        content: `📄 Chat summary: **#${channelName}** [${channel.parent?.name || 'uncategorized'}] — ${dateStr}`,
        files:   [filePath],
      });
    } catch (err) {
      console.error(`[ChatSummarizer] Failed to post for #${channelName} on ${dateStr}:`, err.message);
    }

    await new Promise(r => setTimeout(r, 1500));
  }

  // Return newest message timestamp for cursor update
  return messages[messages.length - 1].createdTimestamp;
}

// ─── Cron runner (full sweep of all channels) ─────────────────────────────────

async function runSummarizer(client) {
  const guild = client.guilds.cache.first();
  if (!guild) return console.error('[ChatSummarizer] No guild found.');

  const outputChannel = guild.channels.cache.find(
    c => c.name === CHAT_SUMMARIES_CHANNEL && c.isTextBased()
  );
  if (!outputChannel) {
    return console.error(`[ChatSummarizer] Cannot find #${CHAT_SUMMARIES_CHANNEL}.`);
  }

  const state        = loadState();
  const allowedChannels = process.env.SUMMARIZE_CHANNELS
    ? new Set(process.env.SUMMARIZE_CHANNELS.split(',').map(c => c.trim()))
    : null;

  const textChannels = guild.channels.cache.filter(
    c => c.isTextBased() &&
        c.type === ChannelType.GuildText &&
        !SKIP_CHANNELS.has(c.name) &&
        (!allowedChannels || allowedChannels.has(c.id))
  );

  console.log(`[ChatSummarizer] Starting run on ${textChannels.size} channel(s).`);

  for (const [, channel] of textChannels) {
    const sinceTimestamp = state[channel.id] ?? null;
    const isBackfill     = sinceTimestamp === null;
    const sinceDate      = sinceTimestamp ? new Date(sinceTimestamp) : new Date(0);

    if (isBackfill) {
      console.log(`[ChatSummarizer] Backfilling #${channel.name} (full history).`);
      await outputChannel.send(
        `⏳ First-time backfill for **#${channel.name}** — this may take a moment...`
      );
    }

    try {
      const latestTimestamp = await summarizeChannel(channel, sinceDate, outputChannel);
      if (latestTimestamp != null) {
        state[channel.id] = latestTimestamp;
        saveState(state); // save per-channel so a crash doesn't lose all progress
      }
    } catch (err) {
      console.error(`[ChatSummarizer] Error on #${channel.name}:`, err);
      await outputChannel.send(
        `⚠️ Failed to summarize **#${channel.name}**: ${err.message}`
      ).catch(() => {});
    }
  }

  console.log('[ChatSummarizer] Run complete.');
}

// ═══════════════════════════════════════════════════════════════════════════════
// SPRINT SUMMARIZATION
// Commands: !sprintsummary <YYYY-MM-DD> <YYYY-MM-DD> [--team <TeamName>]
//           !listteams
// ═══════════════════════════════════════════════════════════════════════════════

function isValidDate(str) {
  return /^\d{4}-\d{2}-\d{2}$/.test(str) && !isNaN(Date.parse(str));
}

function estDateOf(message) {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'America/New_York' })
    .format(new Date(message.createdTimestamp));
}

function isInRange(dateStr, start, end) {
  return dateStr >= start && dateStr <= end;
}

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

// Paginate a summary channel within a closed [start, end] date window.
// Different from fetchMessagesInRange which uses an open sinceDate.
async function fetchSummaryMessagesInRange(channel, start, end) {
  const results = [];
  let   lastId  = null;
  const stopMs  = Date.parse(start);

  while (true) {
    const options = { limit: 100 };
    if (lastId) options.before = lastId;

    let batch;
    try { batch = await channel.messages.fetch(options); }
    catch (err) {
      console.warn(`[SprintSummarizer] Fetch error on #${channel.name}:`, err.message);
      break;
    }

    if (batch.size === 0) break;
    const msgs = [...batch.values()];

    for (const msg of msgs) {
      if (msg.createdTimestamp < stopMs) return results.reverse();
      if (isInRange(estDateOf(msg), start, end)) results.push(msg);
    }

    if (batch.size < 100) break;
    lastId = msgs[msgs.length - 1].id;
    await new Promise(r => setTimeout(r, 300));
  }

  return results.reverse();
}

// Collect chat summary attachments from #chat-summaries.
// Message format: "📄 Chat summary: **#text** [Scrum Pilot] — 2026-04-15"
async function collectChatSummaries(guild, start, end, teamName) {
  const channel = guild.channels.cache.find(
    c => c.name === CHAT_SUMMARIES_CHANNEL && c.isTextBased()
  );
  if (!channel) {
    console.warn(`[SprintSummarizer] #${CHAT_SUMMARIES_CHANNEL} not found.`);
    return [];
  }

  const since = new Date(start);
  since.setDate(since.getDate() - 1);
  const allMessages = await fetchMessagesInRange(channel, since);
  const results  = [];

  for (const msg of allMessages) {
    const dateMatch = msg.content.match(/—\s*(\d{4}-\d{2}-\d{2})/);
    if (!dateMatch || !isInRange(dateMatch[1], start, end)) continue;

    const teamMatch = msg.content.match(/\[(.+?)\]/);
    if (teamName && teamMatch &&teamMatch[1].toLowerCase() !== teamName.toLowerCase()) continue;

    const attachment = msg.attachments.first();
    if (!attachment) continue;

    try {
      const content = await downloadAttachment(attachment.url);
      results.push({ label: msg.content.split('\n')[0], content });
    } catch (err) {
      console.warn(`[SprintSummarizer] Could not download chat summary:`, err.message);
    }
  }

  return results;
}

// Collect standup recap attachments from #standup-recap.
// Message format: "📋 Meeting recap from <date> [Scrum Pilot]"
async function collectRecapSummaries(guild, start, end, teamName) {
  const channel = guild.channels.cache.find(
    c => c.name === RECAP_CHANNEL && c.isTextBased()
  );
  if (!channel) {
    console.warn(`[SprintSummarizer] #${RECAP_CHANNEL} not found.`);
    return [];
  }

  const messages = await fetchSummaryMessagesInRange(channel, start, end);
  const results  = [];

  for (const msg of messages) {
    const match    = msg.content.match(/\[(.+?)\]/);
    const category = match ? match[1] : 'Scrum Pilot'; // untagged = legacy Scrum Pilot recording
    if (teamName && category.toLowerCase() !== teamName.toLowerCase()) continue;

    const attachment = msg.attachments.first();
    if (!attachment) continue;

    try {
      const content = await downloadAttachment(attachment.url);
      results.push({ label: msg.content.split('\n')[0], content });
    } catch (err) {
      console.warn(`[SprintSummarizer] Could not download recap:`, err.message);
    }
  }

  return results;
}

async function collectRawMessages(guild, start, end, teamName) {
  const results = [];
  const since   = new Date(start);
  const until   = new Date(end);
  until.setDate(until.getDate() + 1);

  const teamChannels = guild.channels.cache.filter(
    c => c.isTextBased() &&
         c.type === ChannelType.GuildText &&
         !SKIP_CHANNELS.has(c.name) &&
         c.parent?.name?.toLowerCase() === teamName?.toLowerCase()
  );

  for (const [, channel] of teamChannels) {
    const messages = await fetchMessagesInRange(channel, since);
    const filtered = messages.filter(m => new Date(m.createdTimestamp) <= until);
    if (filtered.length === 0) continue;

    const text = (await Promise.all(
      filtered
        .filter(m => !m.author.bot && m.content.trim())
        .map(async m => {
          const member      = await m.guild.members.fetch(m.author.id).catch(() => null);
          const displayName = member?.nickname || m.author.displayName || m.author.username;
          return `[${estDateOf(m)}] [${displayName}]: ${m.content}`;
        })
    )).join('\n');

    if (text.trim()) {
      results.push({ label: `Raw messages: #${channel.name} (${teamName})`, content: text });
    }
  }

  return results;
}

// Team names come from Discord category structure — no .env config needed.
function getTeamNames(guild) {
  const names = new Set();
  guild.channels.cache.forEach(c => { if (c.parent?.name) names.add(c.parent.name); });
  return [...names].sort();
}

async function postSprintSummary(guild, summaryText, start, end, teamName, sprintName = null) {
  const channel = guild.channels.cache.find(
    c => c.name === SPRINT_CHANNEL && c.isTextBased()
  );
  if (!channel) {
    console.error(`[SprintSummarizer] Output channel #${SPRINT_CHANNEL} not found.`);
    return;
  }

  const teamLabel   = teamName  ? ` · ${teamName}`   : '';
  const sprintLabel = sprintName ? `${sprintName} · ` : '';
  const header      = `## 🏁 ${sprintLabel}Sprint Summary${teamLabel}\n**${start} → ${end}**`;

  const headerMsg = await channel.send(header);
  const thread    = await headerMsg.startThread({
    name: `${sprintLabel}${teamName || 'All Teams'} · ${start} → ${end}`,
    autoArchiveDuration: 10080,
  });

  const LIMIT = 1950;
  let remaining = summaryText;
  while (remaining.length > 0) {
    let cutAt = LIMIT;
    if (remaining.length > LIMIT) {
      const nl = remaining.lastIndexOf('\n', LIMIT);
      if (nl > LIMIT / 2) cutAt = nl + 1;
    }
    await thread.send(remaining.slice(0, cutAt));
    remaining = remaining.slice(cutAt);
    if (remaining.length > 0) await new Promise(r => setTimeout(r, 500));
  }
}

async function summarizeChunk(content, label, start, end) {
  const prompt =
    `Summarize the following Discord channel messages from ${start} through ${end}.\n` +
    `Extract: decisions made, tasks mentioned, blockers raised, progress updates, and open questions.\n` +
    `Be concise. Use bullet points. Ignore casual small talk.\n\n` +
    `Channel: ${label}\n\n${content}`;

  try {
    const res = await Promise.race([
      anthropic.messages.create({
        model: 'claude-opus-4-6', max_tokens: 1024,
        messages: [{ role: 'user', content: prompt }],
      }),
      new Promise((_, reject) => setTimeout(() => reject(new Error('Claude timeout')), 60000))
    ]);
    return res.content[0].text;
  } catch (err) {
    console.warn(`[SprintSummarizer] Claude chunk failed, trying GPT-4o-mini:`, err.message);
    const res = await openai.chat.completions.create({
      model: 'gpt-4o-mini', max_tokens: 1024,
      messages: [{ role: 'user', content: prompt }],
    });
    return res.choices[0].message.content;
  }
}

async function sprintSummaryExists(guild, start, end, teamName) {
  const channel = guild.channels.cache.find(
    c => c.name === SPRINT_CHANNEL && c.isTextBased()
  );
  if (!channel) return false;

  const teamLabel   = teamName  ? ` · ${teamName}`   : '';
  const sprint      = SPRINT_SCHEDULE.find(s => s.start === start && s.end === end);
  const sprintLabel = sprint ? `${sprint.name} · ` : '';
  const headerText  = `## 🏁 ${sprintLabel}Sprint Summary${teamLabel}`;

  const messages = await fetchMessagesInRange(channel, new Date(start));
  return messages.some(m => m.content.startsWith(headerText));
}

async function runSprintSummary(guild, start, end, teamName = null) {
  if (teamName && SPRINT_EXCLUDE_TEAMS.has(teamName)) {
    console.log(`[SprintSummarizer] Skipping excluded team: ${teamName}`);
    return;
  }

  if (await sprintSummaryExists(guild, start, end, teamName)) {
    console.log(`[SprintSummarizer] Already summarized ${start} → ${end}${teamName ? ` (team: ${teamName})` : ''} — skipping.`);
    return;
  }

  console.log(`[SprintSummarizer] Collecting ${start} → ${end}` + (teamName ? ` (team: ${teamName})` : '') + '...');

  const [chatItems, recapItems] = await Promise.all([
    collectRawMessages(guild, start, end, teamName),
    collectRecapSummaries(guild, start, end, teamName),
  ]);

  console.log(`[SprintSummarizer] ${chatItems.length} chat item(s), ${recapItems.length} recap file(s).`);

  const outChannel = guild.channels.cache.find(c => c.name === SPRINT_CHANNEL && c.isTextBased());
  if (!outChannel) { console.error(`[SprintSummarizer] #${SPRINT_CHANNEL} not found.`); return; }

const allItems = [...chatItems, ...recapItems];
  if (allItems.length === 0) {
    await outChannel.send(`_No summary data found for ${start} → ${end}${teamName ? ` (team: ${teamName})` : ''}._`);
    return;
  }

  const teamLabel = teamName ? ` for team **${teamName}**` : '';

  const buildPrompt = (items) => [
    `You are producing a Sprint Summary${teamLabel} covering ${start} through ${end}.`,
    `The following are raw channel messages and standup meeting recaps from that period.`,
    `Synthesize them into a concise, well-structured sprint roll-up.`,
    ``,
    `Include these sections (skip any with no data):`,
    `1. **Sprint Overview** — What was this sprint focused on?`,
    `2. **Key Accomplishments** — Concrete work completed, grouped by theme.`,
    `3. **Blockers & Risks** — Any blockers raised, escalated, or unresolved.`,
    `4. **Carry-over Items** — Work started but not finished.`,
    `5. **Notable Decisions** — Key decisions made or milestones hit.`,
    `6. **Participation Notes** — Who was active; notable async contributions.`,
    ``,
    `Be concise. Use bullet points. Synthesize — do not repeat the raw summaries.`,
    ``,
    `---`,
    ``,
    ...items.flatMap(({ label, content }) => [`## ${label}`, '', content.trim(), '', '---', '']),
  ].join('\n');

  const callAI = async (prompt) => {
    try {
      const res = await Promise.race([
        anthropic.messages.create({
          model: 'claude-opus-4-6', max_tokens: 2048,
          messages: [{ role: 'user', content: prompt }],
        }),
        new Promise((_, reject) => setTimeout(() => reject(new Error('Claude timeout')), 60000))
      ]);
      return res.content[0].text;
    } catch (err) {
      console.warn('[SprintSummarizer] Claude failed, falling back to GPT-4o-mini:', err.message);
      const res = await openai.chat.completions.create({
        model: 'gpt-4o-mini', max_tokens: 2048,
        messages: [{ role: 'user', content: prompt }],
      });
      return res.choices[0].message.content;
    }
  };

  let summary;
  try {
    summary = await callAI(buildPrompt(allItems));
  } catch (err) {
    if (err.status === 429 || err.code === 'rate_limit_exceeded' || err.message?.includes('tokens') || err.message?.includes('too large')) {
      console.warn('[SprintSummarizer] Token limit hit, switching to chunked pre-summarization...');
      const chunkedItems = [];
      for (const item of allItems) {
        try {
          const chunked = await summarizeChunk(item.content, item.label, start, end);
          chunkedItems.push({ label: item.label, content: chunked });
        } catch (chunkErr) {
          console.warn(`[SprintSummarizer] Chunk failed for ${item.label}, using truncated raw:`, chunkErr.message);
          chunkedItems.push({ label: item.label, content: item.content.slice(-8000) });
        }
      }
      summary = await callAI(buildPrompt(chunkedItems));
    } else {
      throw err;
    }
  }

  const sprint = SPRINT_SCHEDULE.find(s => s.start === start && s.end === end);
  await postSprintSummary(guild, summary, start, end, teamName, sprint?.name || null);
}

async function runAllSprintSummaries(guild) {
  const today   = new Date().toISOString().slice(0, 10);
  const teams   = getTeamNames(guild).filter(t => !SPRINT_EXCLUDE_TEAMS.has(t));
  const sprints = SPRINT_SCHEDULE.filter(s => s.end <= today);

  console.log(`[SprintSummarizer] Backfill: ${sprints.length} sprint(s), ${teams.length} team(s).`);

  for (const sprint of sprints) {
    for (const team of teams) {
      console.log(`[SprintSummarizer] ${sprint.name} — ${team}`);
      await runSprintSummary(guild, sprint.start, sprint.end, team);
      await new Promise(r => setTimeout(r, 2000));
    }
  }

  console.log('[SprintSummarizer] Backfill complete.');
}
async function handleSprintCommand(message) {
  const content = message.content.trim();

  if (content.startsWith('!listteams')) {
    const teams = getTeamNames(message.guild);
    if (teams.length === 0) {
      await message.reply('No Discord categories found.');
      return true;
    }
    await message.reply(`**Teams (from Discord categories):**\n${teams.map(t => `• ${t}`).join('\n')}`);
    return true;
  }

  if (content.startsWith('!sprintsummary')) {
    const args    = content.split(/\s+/).slice(1);
    let teamName  = null;
    const teamIdx = args.indexOf('--team');
    if (teamIdx !== -1) {
      teamName = args.slice(teamIdx + 1).join(' ') || null;
      args.splice(teamIdx);
    }

    const [start, end] = args;

    if (!start || !end) {
      await message.reply(
        '⚠️ Usage:\n' +
        '`!sprintsummary <YYYY-MM-DD> <YYYY-MM-DD>`\n' +
        '`!sprintsummary <YYYY-MM-DD> <YYYY-MM-DD> --team <TeamName>`\n\n' +
        'Run `!listteams` to see available teams.'
      );
      return true;
    }
    if (!isValidDate(start) || !isValidDate(end)) {
      await message.reply('❌ Dates must be in `YYYY-MM-DD` format.');
      return true;
    }
    if (start > end) {
      await message.reply('❌ Start date must be before end date.');
      return true;
    }

    await message.reply(
      `⏳ Generating sprint summary for **${start} → ${end}**` +
      (teamName ? ` (team: **${teamName}**)` : '') + '...'
    );

    try {
      await runSprintSummary(message.guild, start, end, teamName);
    } catch (err) {
      console.error('[SprintSummarizer] Error:', err);
      const safeMsg = err.message?.includes('org-')
        ? 'API rate limit or token limit exceeded. Check the console for details.'
        : err.message;
      await message.reply(`❌ ${safeMsg}`);
    }
    return true;
  }

  if (content.startsWith('!sprintbackfill')) {
    await message.reply(`⏳ Running sprint summaries for all ${SPRINT_SCHEDULE.length} sprints across all teams...`);
    try {
      await runAllSprintSummaries(message.guild);
      await message.reply('✅ Sprint backfill complete.');
    } catch (err) {
      console.error('[SprintSummarizer] Backfill error:', err);
      const safeMsg = err.message?.includes('org-') 
        ? 'API rate limit or token limit exceeded. Check the console for details.'
        : err.message;
      await message.reply(`❌ ${safeMsg}`);
    }
    return true;
  }

  return false;
}

// ─── Exports ──────────────────────────────────────────────────────────────────

module.exports = {
  runSummarizer,
  CHAT_SUMMARY_CRON,
  summarizeChannel,        // used by !summarize in index.js
  fetchMessagesInRange,    // used by !export in index.js
  formatMessagesToJson,    // used by !export in index.js
  handleSprintCommand,
  runSprintSummary,
  runAllSprintSummaries,
  SPRINT_SUMMARY_CRON,
  SPRINT_SCHEDULE,
  getTeamNames,
};
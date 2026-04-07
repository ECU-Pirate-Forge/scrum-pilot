require('dotenv').config();
const { ChannelType } = require('discord.js');
const cron = require('node-cron');
const fs = require('fs');
const path = require('path');
const Anthropic = require('@anthropic-ai/sdk');
const OpenAI = require('openai');

// ─── Config ───────────────────────────────────────────────────────────────────

const CHAT_SUMMARIES_CHANNEL = process.env.CHAT_SUMMARIES_CHANNEL || 'chat-summaries';
const SUMMARIES_DIR          = path.resolve(process.env.SUMMARIES_DIR || './summaries');
const CHAT_SUMMARY_CRON      = process.env.CHAT_SUMMARY_CRON || '0 2 * * *';
const STATE_FILE             = path.join(__dirname, 'chat-state.json');

const SKIP_CHANNELS = new Set([
  CHAT_SUMMARIES_CHANNEL,
  process.env.RECAP_CHANNEL || 'standup-recap',
  process.env.BOT_COMMANDS  || 'bot-commands',
]);

const anthropic = new Anthropic({ apiKey: process.env.ANTHROPIC_API_KEY });
const openai    = new OpenAI({ apiKey: process.env.OPENAI_API_KEY });

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
        content: `📄 Chat summary: **#${channelName}** — ${dateStr}`,
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

// ─── Exports ──────────────────────────────────────────────────────────────────

module.exports = {
  runSummarizer,
  CHAT_SUMMARY_CRON,
  summarizeChannel,        // used by !summarize in index.js
  fetchMessagesInRange,    // used by !export in index.js
  formatMessagesToJson,    // used by !export in index.js
};
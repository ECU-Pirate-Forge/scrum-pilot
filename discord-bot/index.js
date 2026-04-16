require('dotenv').config();
const { Client, GatewayIntentBits, Events, AttachmentBuilder, PermissionsBitField} = require('discord.js');

const READ_CHANNEL_ENV_KEY = 'SCRUMLORD_READ_CHANNEL_ID';
const SPEAK_CHANNEL_ENV_KEY = 'SCRUMLORD_SPEAK_CHANNEL_ID';
const { handleVoiceStateUpdate } = require('./recorder');

const cron = require('node-cron');
const {
  runSummarizer,
  CHAT_SUMMARY_CRON,
  SPRINT_SUMMARY_CRON,
  summarizeChannel,
  fetchMessagesInRange,
  formatMessagesToJson,
  handleSprintCommand,
  runSprintSummary,
  runAllSprintSummaries,
  SPRINT_SCHEDULE,
  getTeamNames,
} = require('./chat-summarizer');

const client = new Client({
  intents: [
    GatewayIntentBits.Guilds,
    GatewayIntentBits.GuildMembers,
    GatewayIntentBits.GuildPresences,
    GatewayIntentBits.GuildMessages,
    GatewayIntentBits.MessageContent,
    GatewayIntentBits.GuildVoiceStates,
  ],
});

function parseTimeRange(rangeStr) {
  const match = rangeStr.match(/^(\d+)([dhm])$/);
  if (!match) return null;

  const value = parseInt(match[1]);
  const unit = match[2];

  const now = new Date();
  const sinceDate = new Date(now);

  switch (unit) {
    case 'd':
      sinceDate.setDate(now.getDate() - value);
      break;
    case 'h':
      sinceDate.setHours(now.getHours() - value);
      break;
    case 'm':
      sinceDate.setMinutes(now.getMinutes() - value);
      break;
    default:
      return null;
  }

  return sinceDate;
}

function extractChannelId(input) {
  if (!input) return null;
  const mentionMatch = input.trim().match(/^<#(\d+)>$/);
  if (mentionMatch) return mentionMatch[1];
  const normalized = input.trim();
  return /^\d+$/.test(normalized) ? normalized : null;
}

function getChannelRoutingConfig() {
  const readChannelId = process.env[READ_CHANNEL_ENV_KEY] || null;
  const speakChannelId = process.env[SPEAK_CHANNEL_ENV_KEY] || null;

  return {
    readChannelId,
    speakChannelId,
    isConfigured: Boolean(readChannelId && speakChannelId),
  };
}

async function resolveConfiguredChannel(guild, channelId) {
  if (!guild || !channelId) return null;

  try {
    return await guild.channels.fetch(channelId);
  } catch (error) {
    return null;
  }
}

async function sendCommandReply(message, content, routingConfig) {
  if (message.guild && routingConfig && routingConfig.speakChannelId) {
    const speakChannel = await resolveConfiguredChannel(
      message.guild,
      routingConfig.speakChannelId
    );

    if (speakChannel && typeof speakChannel.send === 'function') {
      return speakChannel.send(content);
    }
  }

  return message.reply(content);
}

// Confirmation bot has joined server
client.once(Events.ClientReady, (readyClient) => {
  console.log(`Scrumlord is online. Logged in as ${readyClient.user.tag}`);
  //setClient(readyClient);

  // Schedule daily chat summarization
  cron.schedule(CHAT_SUMMARY_CRON, () => {
    console.log('[ChatSummarizer] Cron fired.');
    runSummarizer(client).catch(err =>
      console.error('[ChatSummarizer] Cron error:', err)
    );
  });
  console.log(`[ChatSummarizer] Scheduled daily at: ${CHAT_SUMMARY_CRON}`);

  cron.schedule(SPRINT_SUMMARY_CRON, async () => {
    try {
      const yesterday = new Date();
      yesterday.setDate(yesterday.getDate() - 1);
      const dateStr = yesterday.toISOString().slice(0, 10);
      const sprint = SPRINT_SCHEDULE.find(s => s.end === dateStr);
      if (!sprint) return;

      console.log(`[SprintSummarizer] Sprint end detected: ${sprint.name} — running summaries.`);
      const guild = client.guilds.cache.first();
      const teams = getTeamNames(guild);
      for (const team of teams) {
        await runSprintSummary(guild, sprint.start, sprint.end, team);
        await new Promise(r => setTimeout(r, 2000));
      }
    } catch (err) {
      console.error('[SprintSummarizer] Cron error:', err);
    }
  });
  console.log(`[SprintSummarizer] Scheduled sprint end check at: ${SPRINT_SUMMARY_CRON}`);
});

client.on(Events.VoiceStateUpdate, (oldState, newState) => {
  handleVoiceStateUpdate(oldState, newState);
});

// Message monitoring hook for both data collection and command lookout
client.on(Events.MessageCreate, async (message) => {
  // Ignore messages from bots (including itself)
  if (message.author.bot) return;

  if (await handleSprintCommand(message)) return;

  const isCommand = message.content.startsWith('!');
  if (!isCommand) return;

  const routingConfig = getChannelRoutingConfig();

  if (message.content.startsWith('!setchannels')) {
    if (!message.guild) {
      await message.reply('❌ `!setchannels` can only be used inside a server channel.');
      return;
    }

    if (
      !message.member ||
      !message.member.permissions ||
      !message.member.permissions.has(PermissionsBitField.Flags.ManageGuild)
    ) {
      await message.reply('❌ You need the **Manage Server** permission to run this command.');
      return;
    }

    const args = message.content.trim().split(/\s+/);
    if (args.length < 3) {
      await message.reply(
        '⚠️ Usage: `!setchannels <read_channel> <speak_channel>`\n' +
          'Example: `!setchannels #team-chat #scrumlord-updates`'
      );
      return;
    }

    const readChannelId = extractChannelId(args[1]);
    const speakChannelId = extractChannelId(args[2]);

    if (!readChannelId || !speakChannelId) {
      await message.reply(
        '❌ Invalid channel reference. Use channel mentions like `#channel` or raw channel IDs.'
      );
      return;
    }

    const [readChannel, speakChannel] = await Promise.all([
      resolveConfiguredChannel(message.guild, readChannelId),
      resolveConfiguredChannel(message.guild, speakChannelId),
    ]);

    if (!readChannel || !speakChannel) {
      await message.reply('❌ One or both channels could not be found in this server.');
      return;
    }

    if (!readChannel.isTextBased() || !speakChannel.isTextBased()) {
      await message.reply('❌ Both channels must be text-based channels.');
      return;
    }

    await message.reply(
      '✅ Manual configuration mode enabled. Add these values to `discord-bot/.env` and restart the bot:\n' +
        `\`${READ_CHANNEL_ENV_KEY}=${readChannel.id}\`\n` +
        `\`${SPEAK_CHANNEL_ENV_KEY}=${speakChannel.id}\``
    );
    return;
  }

  // Once configured, command input is accepted only from the read channel.
  if (
    routingConfig.isConfigured &&
    message.guild &&
    message.channel.id !== routingConfig.readChannelId
  ) {
    return;
  }

  // Basic ping command to confirm the bot is alive and listening
  if (message.content === '!ping') {
    await sendCommandReply(message, 'Pong! Scrumlord is watching. 👑', routingConfig);
    return;
  }

  // Export command to retrieve and export messages as JSON
  if (message.content.startsWith('!export')) {
    const args = message.content.split(' ');

    // Validate arguments
    if (args.length < 2) {
      await sendCommandReply(
        message,
        '⚠️ Usage: `!export <time_range>`\n' +
          'Examples:\n' +
          '  `!export 7d` - Export last 7 days\n' +
          '  `!export 24h` - Export last 24 hours\n' +
          '  `!export 30m` - Export last 30 minutes',
        routingConfig
      );
      return;
    }

    const timeRange = args[1];
    const sinceDate = parseTimeRange(timeRange);

    if (!sinceDate) {
      await sendCommandReply(
        message,
        '❌ Invalid time range format. Use: `<number><d|h|m>`\n' +
          'Examples: `7d` (7 days), `24h` (24 hours), `30m` (30 minutes)'
      );
      return;
    }

    try {
      const speakChannel =
        (await resolveConfiguredChannel(message.guild, routingConfig.speakChannelId)) ||
        message.channel;

      const configuredReadChannel = await resolveConfiguredChannel(
        message.guild,
        routingConfig.readChannelId
      );
      const sourceChannel = configuredReadChannel || message.channel;

      if (routingConfig.isConfigured && !configuredReadChannel) {
        await sendCommandReply(
          message,
          `❌ Configured read channel (${READ_CHANNEL_ENV_KEY}) is unavailable.`,
          routingConfig
        );
        return;
      }

      // Send initial feedback
      const fetchingMsg = await speakChannel.send(
        `⏳ Fetching messages from <#${sourceChannel.id}> for the last ${timeRange}...`
      );

      // Fetch messages
      const messages = await fetchMessagesInRange(sourceChannel, sinceDate);

      if (messages.length === 0) {
        fetchingMsg.edit(
          `No messages found in the last ${timeRange}.`
        );
        return;
      }

      // Format to JSON
      const jsonData = formatMessagesToJson(messages);
      const jsonBuffer = Buffer.from(jsonData, 'utf-8');

      // Check file size (probably a non issue since only text, but good to be safe)
      const fileSizeMB = jsonBuffer.length / (1024 * 1024);
      if (fileSizeMB > 8) {
        fetchingMsg.edit(
          `⚠️ Export too large (${fileSizeMB.toFixed(2)}MB). `
        );
        return;
      }

      // Create file attachment
      const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
      const filename = `messages-export-${sourceChannel.id}-${timestamp}.json`;
      const attachment = new AttachmentBuilder(jsonBuffer, { name: filename });

      // Send file
      await fetchingMsg.edit(
        `✅ Exported **${messages.length}** messages from the last ${timeRange}.`
      );
      await speakChannel.send({ files: [attachment] });

      console.log(
        `[Export] User ${message.author.tag} exported ${messages.length} messages from channel ${sourceChannel.name}`
      );
    } catch (error) {
      console.error('[Export Error]', error);

      let errorMsg = 'Failed to export messages.';

      if (error.message.includes('Missing Permissions')) {
        errorMsg +=
          ' The bot lacks permission to read message history in this channel.';
      } else if (error.message.includes('Missing Access')) {
        errorMsg += ' The bot cannot access this channel.';
      } else {
        errorMsg += ` Error: ${error.message}`;
      }

      await sendCommandReply(message, errorMsg, routingConfig);
    }
  }

 if (message.content.startsWith('!summarize')) {
    const args = message.content.split(' ');
 
    if (args.length < 2) {
      await sendCommandReply(
        message,
        '⚠️ Usage: `!summarize <time_range>`\n' +
        'Examples:\n' +
        '  `!summarize 7d` - Summarize last 7 days\n' +
        '  `!summarize 24h` - Summarize last 24 hours\n' +
        '  `!summarize 30m` - Summarize last 30 minutes',
        routingConfig
      );
      return;
    }
 
    const timeRange = args[1];
    const sinceDate = parseTimeRange(timeRange);
 
    if (!sinceDate) {
      await sendCommandReply(
        message,
        '❌ Invalid time range format. Use: `<number><d|h|m>`\n' +
        'Examples: `7d` (7 days), `24h` (24 hours), `30m` (30 minutes)',
        routingConfig
      );
      return;
    }
 
    try {
      const statusMsg = await message.channel.send(`⏳ Running full summarization sweep...`);
      await runSummarizer(client);
      await statusMsg.edit(`✅ Summarization sweep complete.`);
      console.log(`[Summarize] ${message.author.tag} triggered full sweep.`);
    } catch (error) {
      console.error('[Summarize Error]', error);
      await sendCommandReply(message, `❌ Failed to summarize: ${error.message}`, routingConfig);
    }
    return;
  }
});

// Log in using the token from .env
client.login(process.env.DISCORD_TOKEN);

if (process.env.NODE_ENV === 'test') {
  module.exports = { parseTimeRange, formatMessagesToJson, fetchMessagesInRange, extractChannelId, getChannelRoutingConfig };
}
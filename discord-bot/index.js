require('dotenv').config();
const { Client, GatewayIntentBits, Events, AttachmentBuilder } = require('discord.js');

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

// Fetch messages from a channel within a time range
async function fetchMessagesInRange(channel, sinceDate) {
  const collectedMessages = [];
  let lastMessageId = null;

  try {
    while (true) {
      // Discord max 100 msgs per fetch
      const options = { limit: 100 };
      if (lastMessageId) {
        options.before = lastMessageId;
      }

      const messages = await channel.messages.fetch(options);
      if (messages.size === 0) break;

      // Filter messages by timestamp
      const filteredMessages = messages.filter(
        (msg) => msg.createdAt >= sinceDate
      );

      collectedMessages.push(...filteredMessages.values());

      // If we found messages older than our range stop fetching
      const oldestMessage = messages.last();
      if (oldestMessage.createdAt < sinceDate) break;

      lastMessageId = oldestMessage.id;

      // Stop if fewer than 100 messages (no more history)
      if (messages.size < 100) break;
    }

    // Sort messages by timestamp (oldest first)
    collectedMessages.sort((a, b) => a.createdAt - b.createdAt);
    return collectedMessages;
  } catch (error) {
    throw new Error(`Failed to fetch messages: ${error.message}`);
  }
}

// Format to JSON
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

// Confirmation bot has joined server
client.once(Events.ClientReady, (readyClient) => {
  console.log(`Scrumlord is online. Logged in as ${readyClient.user.tag}`);
});

// Message monitoring hook for both data collection and command lookout
client.on(Events.MessageCreate, async (message) => {
  // Ignore messages from bots (including itself)
  if (message.author.bot) return;

  // Basic ping command to confirm the bot is alive and listening
  if (message.content === '!ping') {
    message.reply('Pong! Scrumlord is watching. 👑');
    return;
  }

  // Export command to retrieve and export messages as JSON
  if (message.content.startsWith('!export')) {
    const args = message.content.split(' ');

    // Validate arguments
    if (args.length < 2) {
      message.reply(
        '⚠️ Usage: `!export <time_range>`\n' +
          'Examples:\n' +
          '  `!export 7d` - Export last 7 days\n' +
          '  `!export 24h` - Export last 24 hours\n' +
          '  `!export 30m` - Export last 30 minutes'
      );
      return;
    }

    const timeRange = args[1];
    const sinceDate = parseTimeRange(timeRange);

    if (!sinceDate) {
      message.reply(
        '❌ Invalid time range format. Use: `<number><d|h|m>`\n' +
          'Examples: `7d` (7 days), `24h` (24 hours), `30m` (30 minutes)'
      );
      return;
    }

    try {
      // Send initial feedback
      const fetchingMsg = await message.reply(
        `⏳ Fetching messages from the last ${timeRange}...`
      );

      // Fetch messages
      const messages = await fetchMessagesInRange(message.channel, sinceDate);

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
      const filename = `messages-export-${message.channel.id}-${timestamp}.json`;
      const attachment = new AttachmentBuilder(jsonBuffer, { name: filename });

      // Send file
      await fetchingMsg.edit(
        `✅ Exported **${messages.length}** messages from the last ${timeRange}.`
      );
      await message.channel.send({ files: [attachment] });

      console.log(
        `[Export] User ${message.author.tag} exported ${messages.length} messages from channel ${message.channel.name}`
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

      message.reply(errorMsg);
    }
  }
});

// Log in using the token from .env
client.login(process.env.DISCORD_TOKEN);
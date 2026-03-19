require('dotenv').config();
const { Client, GatewayIntentBits, Events } = require('discord.js');
const { handleVoiceStateUpdate } = require('./recorder');
//const { handleVoiceStateUpdate, watchForCraigLink, setClient } = require('./craig')

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

// Confirmation bot has joined server
client.once(Events.ClientReady, (readyClient) => {
  console.log(`Scrumlord is online. Logged in as ${readyClient.user.tag}`);
  //setClient(readyClient);
});

client.on(Events.VoiceStateUpdate, (oldState, newState) => {
  handleVoiceStateUpdate(oldState, newState);
});

// Message monitoring hook for both data collection and command lookout
client.on(Events.MessageCreate, (message) => {
  // // Watch for Craig's download link
  // watchForCraigLink(message, async (craigMessage) => {
    // Transcription hook goes here later
  //   console.log('[Transcription] TODO — got Craig link:', craigMessage.content);
  // });

  // Ignore messages from bots (including itself)
  if (message.author.bot) return;

  // Basic ping command to confirm the bot is alive and listening
  if (message.content === '!ping') {
    message.reply('Pong! Scrumlord is watching. 👑');
  }
});

// // Craig voice state trigger
// client.on(Events.VoiceStateUpdate, (oldState, newState) => {
//   handleVoiceStateUpdate(oldState, newState);
// });

// Log in using the token from .env
client.login(process.env.DISCORD_TOKEN);
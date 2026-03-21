// const { MEETING_VOICE_CHANNEL, BOT_COMMANDS_CHANNEL, CRAIG_BOT_ID, QUORUM } = require('./config');

// let recording = false;
// let _client = null;

// function setClient(client) {
//   _client = client;
// }

// async function getControlChannel(guild) {
//   return guild.channels.cache.find(
//     c => c.name === BOT_COMMANDS_CHANNEL && c.isTextBased()
//   );
// }

// function getHumanCount(voiceChannel) {
//   return voiceChannel.members.filter(m => !m.user.bot).size;
// }

// async function handleVoiceStateUpdate(oldState, newState) {
//   const guild = newState.guild;
//   const meetingChannel = guild.channels.cache.find(
//     c => c.name === MEETING_VOICE_CHANNEL
//   );

//   if (!meetingChannel) return;

//   const humanCount = getHumanCount(meetingChannel);
//   const controlChannel = await getControlChannel(guild);
//   if (!controlChannel) return;

//   if (!recording && humanCount >= QUORUM) {
//     recording = true;
//     await controlChannel.send('/join');
//     await controlChannel.send('🔴 Meeting detected — Craig is now recording.');
//     console.log('[Craig] Recording started.');
//   }

//   if (recording && humanCount === 0) {
//     recording = false;
//     await controlChannel.send('/stop');
//     await controlChannel.send('⏹️ Meeting ended — Craig is stopping.');
//     console.log('[Craig] Recording stopped.');
//   }
// }

// async function watchForCraigLink(message, handleTranscription) {
//   // Debug: log everything Craig sends
//   if (message.author.id === CRAIG_BOT_ID) {
//     console.log('[Craig DEBUG] Message received:');
//     console.log('  content:', message.content);
//     console.log('  embeds:', JSON.stringify(message.embeds, null, 2));
//     console.log('  channel:', message.channel.name ?? 'DM');
//     console.log('  guild:', message.guild?.name ?? 'none (DM)');
//   }

//   if (message.author.id !== CRAIG_BOT_ID) return;

//   const hasLinkInContent = message.content.includes('http');
//   const hasLinkInEmbeds = message.embeds.some(
//     e => e.url || e.description?.includes('http')
//   );

//   if (!hasLinkInContent && !hasLinkInEmbeds) return;

//   console.log('[Craig] Download link received:', message.content, message.embeds);

//   // Post the link to #bot-commands
//   if (_client) {
//     const guild = _client.guilds.cache.first();
//     const controlChannel = await getControlChannel(guild);
//     if (controlChannel) {
//       const link = message.content || message.embeds.find(e => e.url)?.url;
//       await controlChannel.send(`🎙️ Recording available: ${link}`);
//     }
//   }

//   if (typeof handleTranscription === 'function') {
//     await handleTranscription(message);
//   }
// }

// module.exports = { setClient, handleVoiceStateUpdate, watchForCraigLink };
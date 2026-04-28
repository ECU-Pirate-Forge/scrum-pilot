// tests/index.test.js
// Tests for index.js: client initialization, event registration, and message routing.
// Command behavior is covered in commands.test.js.
// Utility function behavior is covered in utils.test.js.

jest.mock('discord.js', () => {
  const mockClient = {
    once: jest.fn(),
    on:   jest.fn(),
    login: jest.fn().mockResolvedValue('token'),
    user:  { id: 'bot-id-123', tag: 'Scrumlord#0001' },
    guilds: { cache: { first: jest.fn().mockReturnValue(null) } },
  };
  return {
    Client: jest.fn(() => mockClient),
    GatewayIntentBits: {
      Guilds:               1,
      GuildMembers:         2,
      GuildPresences:       3,
      GuildMessages:        4,
      MessageContent:       5,
      GuildVoiceStates:     6,
      GuildScheduledEvents: 7,
    },
    Events: {
      ClientReady:       'ready',
      VoiceStateUpdate:  'voiceStateUpdate',
      MessageCreate:     'messageCreate',
    },
    AttachmentBuilder: jest.fn(),
    PermissionsBitField: { Flags: { ManageGuild: 'ManageGuild' } },
    ChannelType: { GuildText: 0, GuildVoice: 2 },
  };
});

jest.mock('dotenv', () => ({ config: jest.fn() }));
jest.mock('node-cron', () => ({ schedule: jest.fn() }));

jest.mock('../recorder',        () => ({ handleVoiceStateUpdate: jest.fn() }));
jest.mock('../commands',        () => ({ dispatchCommand: jest.fn().mockResolvedValue(false) }));
jest.mock('../chat-summarizer', () => ({
  runSummarizer:     jest.fn(),
  runSprintSummary:  jest.fn(),
  getTeamNames:      jest.fn().mockReturnValue([]),
}));
jest.mock('../aii/aii',         () => ({ handleAIICommand: jest.fn().mockResolvedValue(false) }));
jest.mock('../agent/conversate', () => ({
  handleAgentQuery:   jest.fn().mockResolvedValue(undefined),
  isScrumlordThread:  jest.fn().mockResolvedValue(false),
}));

// ── Helpers ───────────────────────────────────────────────────────────────────

function getHandler(mockClient, eventName, method = 'on') {
  const call = mockClient[method].mock.calls.find(([event]) => event === eventName);
  return call ? call[1] : null;
}

function makeMessage(overrides = {}) {
  return {
    author:   { bot: false, tag: 'User#0001' },
    content:  '',
    channel:  {
      id:       'channel-1',
      isThread: jest.fn().mockReturnValue(false),
      send:     jest.fn().mockResolvedValue({ edit: jest.fn() }),
    },
    mentions: { has: jest.fn().mockReturnValue(false) },
    guild:    null,
    reply:    jest.fn(),
    ...overrides,
  };
}

// ── Setup ─────────────────────────────────────────────────────────────────────

let mockClient;
let Client, GatewayIntentBits, Events, dotenv, cron;
let dispatchCommand, handleAgentQuery, isScrumlordThread, handleSprintCommand, handleAIICommand;

beforeEach(() => {
  jest.resetModules();
  jest.clearAllMocks();
  process.env.DISCORD_TOKEN = 'test-token';
  process.env.NODE_ENV      = 'test';
  delete process.env.SCRUMLORD_READ_CHANNEL_ID;
  delete process.env.SCRUMLORD_SPEAK_CHANNEL_ID;

  ({ Client, GatewayIntentBits, Events } = require('discord.js'));
  dotenv = require('dotenv');
  cron   = require('node-cron');

  // Re-import after resetModules so we get fresh mock references.
  require('../index');
  mockClient = Client.mock.results[0].value;

  ({ dispatchCommand }  = require('../commands'));
  ({ handleAgentQuery, isScrumlordThread } = require('../agent/conversate'));
  ({ handleAIICommand } = require('../aii/aii'));
  ({ handleSprintCommand } = require('../chat-summarizer'));
});

// ── Initialization ────────────────────────────────────────────────────────────

describe('Initialization', () => {
  test('calls dotenv.config', () => {
    expect(dotenv.config).toHaveBeenCalled();
  });

  test('creates a Client with all required intents', () => {
    expect(Client).toHaveBeenCalledWith({
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
  });

  test('logs in with the token from .env', () => {
    expect(mockClient.login).toHaveBeenCalledWith('test-token');
  });
});

// ── Event registration ────────────────────────────────────────────────────────

describe('Event registration', () => {
  test('registers a once listener for ClientReady', () => {
    expect(mockClient.once).toHaveBeenCalledWith(Events.ClientReady, expect.any(Function));
  });

  test('registers an on listener for VoiceStateUpdate', () => {
    expect(mockClient.on).toHaveBeenCalledWith(Events.VoiceStateUpdate, expect.any(Function));
  });

  test('registers an on listener for MessageCreate', () => {
    expect(mockClient.on).toHaveBeenCalledWith(Events.MessageCreate, expect.any(Function));
  });
});

// ── ClientReady ───────────────────────────────────────────────────────────────

describe('ClientReady', () => {
  test('logs the ready message', () => {
    const spy = jest.spyOn(console, 'log').mockImplementation(() => {});
    const handler = getHandler(mockClient, Events.ClientReady, 'once');
    handler({ user: { tag: 'Scrumlord#0001' } });
    expect(spy).toHaveBeenCalledWith('Scrumlord is online. Logged in as Scrumlord#0001');
    spy.mockRestore();
  });

  test('schedules two cron jobs', () => {
    const handler = getHandler(mockClient, Events.ClientReady, 'once');
    handler({ user: { tag: 'Scrumlord#0001' } });
    expect(cron.schedule).toHaveBeenCalledTimes(2);
  });
});

// ── Message routing ───────────────────────────────────────────────────────────

describe('Message routing', () => {
  let messageHandler;

  beforeEach(() => {
    messageHandler = getHandler(mockClient, Events.MessageCreate, 'on');
  });

  test('ignores messages from bots', async () => {
    const msg = makeMessage({ author: { bot: true } });
    await messageHandler(msg);
    expect(dispatchCommand).not.toHaveBeenCalled();
    expect(handleAgentQuery).not.toHaveBeenCalled();
  });

  test('routes Scrumlord-managed thread messages to the agent', async () => {
    isScrumlordThread.mockResolvedValueOnce(true);
    const msg = makeMessage({
      channel: {
        id:       'thread-1',
        isThread: jest.fn().mockReturnValue(true),
        send:     jest.fn(),
      },
    });
    await messageHandler(msg);
    expect(handleAgentQuery).toHaveBeenCalledWith(msg, expect.anything());
    expect(dispatchCommand).not.toHaveBeenCalled();
  });

  test('routes @mention messages to the agent', async () => {
    const msg = makeMessage({
      mentions: { has: jest.fn().mockReturnValue(true) },
    });
    await messageHandler(msg);
    expect(handleAgentQuery).toHaveBeenCalledWith(msg, expect.anything());
    expect(dispatchCommand).not.toHaveBeenCalled();
  });

  test('does not call dispatchCommand for non-command plain messages', async () => {
    const msg = makeMessage({ content: 'just chatting' });
    await messageHandler(msg);
    expect(dispatchCommand).not.toHaveBeenCalled();
  });

  test('calls dispatchCommand for ! messages', async () => {
    const msg = makeMessage({ content: '!ping' });
    await messageHandler(msg);
    expect(dispatchCommand).toHaveBeenCalledWith(msg, expect.anything());
  });

  test('calls handleSprintCommand before dispatchCommand', async () => {
    handleSprintCommand.mockResolvedValueOnce(true);
    const msg = makeMessage({ content: '!sprintsummary 2026-01-01 2026-01-14' });
    await messageHandler(msg);
    expect(handleSprintCommand).toHaveBeenCalledWith(msg);
    expect(dispatchCommand).not.toHaveBeenCalled();
  });

  test('calls handleAIICommand before dispatchCommand', async () => {
    handleAIICommand.mockResolvedValueOnce(true);
    const msg = makeMessage({ content: '!aiiscore Sprint 3' });
    await messageHandler(msg);
    expect(handleAIICommand).toHaveBeenCalled();
    expect(dispatchCommand).not.toHaveBeenCalled();
  });
});

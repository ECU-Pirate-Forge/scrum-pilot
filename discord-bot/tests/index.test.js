// tests/index.test.js

jest.mock('discord.js', () => {
  const mockClient = {
    once: jest.fn(),
    on: jest.fn(),
    login: jest.fn().mockResolvedValue('token'),
  };
  return {
    Client: jest.fn(() => mockClient),
    GatewayIntentBits: {
      Guilds: 1,
      GuildMembers: 2,
      GuildPresences: 3,
      GuildMessages: 4,
      MessageContent: 5,
      GuildVoiceStates: 6,
    },
    Events: {
      ClientReady: 'ready',
      MessageCreate: 'messageCreate',
    },
    AttachmentBuilder: jest.fn().mockImplementation((buffer, options) => ({
      buffer,
      name: options.name,
    })),
  };
});

jest.mock('dotenv', () => ({ config: jest.fn() }));

function getHandler(mockClient, eventName, method = 'on') {
  const call = mockClient[method].mock.calls.find(([event]) => event === eventName);
  return call ? call[1] : null;
}

let mockClient;
let bot;
let Client, GatewayIntentBits, Events, dotenv;

beforeEach(() => {
  jest.resetModules();
  jest.clearAllMocks();
  process.env.DISCORD_TOKEN = 'test-token';
  process.env.NODE_ENV = 'test';

  // Re-require after resetModules so we get the fresh mock references
  ({ Client, GatewayIntentBits, Events } = require('discord.js'));
  dotenv = require('dotenv');
  bot = require('../index');
  mockClient = Client.mock.results[0].value;
});

  // -------------------------------------------------------
  // Initialization
  // -------------------------------------------------------

  describe('Initialization', () => {
    test('loads dotenv config', () => {
      expect(dotenv.config).toHaveBeenCalled();
    });

    test('creates a Client with the correct intents', () => {
      expect(Client).toHaveBeenCalledWith({
        intents: [
          GatewayIntentBits.Guilds,
          GatewayIntentBits.GuildMembers,
          GatewayIntentBits.GuildPresences,
          GatewayIntentBits.GuildMessages,
          GatewayIntentBits.MessageContent,
          GatewayIntentBits.GuildVoiceStates,
        ],
      });
    });

    test('logs in with the token from .env', () => {
      expect(mockClient.login).toHaveBeenCalledWith('test-token');
    });
  });

  // -------------------------------------------------------
  // ClientReady event
  // -------------------------------------------------------

  describe('ClientReady event', () => {
    test('registers a once listener for the ready event', () => {
      expect(mockClient.once).toHaveBeenCalledWith(Events.ClientReady, expect.any(Function));
    });

    test('logs the correct ready message', () => {
      const consoleSpy = jest.spyOn(console, 'log').mockImplementation(() => {});
      const readyHandler = getHandler(mockClient, Events.ClientReady, 'once');
      readyHandler({ user: { tag: 'Scrumlord#1234' } });
      expect(consoleSpy).toHaveBeenCalledWith(
        'Scrumlord is online. Logged in as Scrumlord#1234'
      );
      consoleSpy.mockRestore();
    });
  });

  // -------------------------------------------------------
  // parseTimeRange
  // -------------------------------------------------------

  describe('parseTimeRange', () => {
    test('returns a date N days in the past for Xd format', () => {
      const result = bot.parseTimeRange('7d');
      const expected = new Date();
      expected.setDate(expected.getDate() - 7);
      expect(result.getTime()).toBeCloseTo(expected.getTime(), -3);
    });

    test('returns a date N hours in the past for Xh format', () => {
      const result = bot.parseTimeRange('24h');
      const expected = new Date();
      expected.setHours(expected.getHours() - 24);
      expect(result.getTime()).toBeCloseTo(expected.getTime(), -3);
    });

    test('returns a date N minutes in the past for Xm format', () => {
      const result = bot.parseTimeRange('30m');
      const expected = new Date();
      expected.setMinutes(expected.getMinutes() - 30);
      expect(result.getTime()).toBeCloseTo(expected.getTime(), -3);
    });

    test('returns null for an invalid format', () => {
      expect(bot.parseTimeRange('7x')).toBeNull();
    });

    test('returns null for a plain number with no unit', () => {
      expect(bot.parseTimeRange('7')).toBeNull();
    });

    test('returns null for an empty string', () => {
      expect(bot.parseTimeRange('')).toBeNull();
    });

    test('returns null for a unit with no number', () => {
      expect(bot.parseTimeRange('d')).toBeNull();
    });
  });

  // -------------------------------------------------------
  // formatMessagesToJson
  // -------------------------------------------------------

  describe('formatMessagesToJson', () => {
    const mockMessages = [
      {
        author: { id: '111', username: 'Alice' },
        content: 'Hello there',
        createdAt: new Date('2026-01-01T10:00:00Z'),
      },
      {
        author: { id: '222', username: 'Bob' },
        content: 'General Kenobi',
        createdAt: new Date('2026-01-01T10:01:00Z'),
      },
    ];

    test('returns a valid JSON string', () => {
      const result = bot.formatMessagesToJson(mockMessages);
      expect(() => JSON.parse(result)).not.toThrow();
    });

    test('includes the correct fields for each message', () => {
      const result = JSON.parse(bot.formatMessagesToJson(mockMessages));
      expect(result[0]).toEqual({
        author: { id: '111', username: 'Alice' },
        content: 'Hello there',
        timestamp: '2026-01-01T10:00:00.000Z',
      });
    });

    test('returns the correct number of messages', () => {
      const result = JSON.parse(bot.formatMessagesToJson(mockMessages));
      expect(result).toHaveLength(2);
    });

    test('returns an empty array for no messages', () => {
      const result = JSON.parse(bot.formatMessagesToJson([]));
      expect(result).toEqual([]);
    });
  });

  // -------------------------------------------------------
  // fetchMessagesInRange
  // -------------------------------------------------------

  describe('fetchMessagesInRange', () => {
    const sinceDate = new Date('2026-01-01T00:00:00Z');

    const makeMessage = (id, createdAt, content = 'test') => ({
      id,
      content,
      createdAt: new Date(createdAt),
      author: { id: '111', username: 'Alice' },
    });

    test('returns messages within the time range', async () => {
      const msg1 = makeMessage('1', '2026-01-01T12:00:00Z');
      const msg2 = makeMessage('2', '2026-01-01T13:00:00Z');
      const mockMessages = {
        size: 2,
        filter: (fn) => ({ values: () => [msg1, msg2].filter(fn).values() }),
        last: () => msg1,
        values: () => [msg1, msg2].values(),
      };

      const mockChannel = {
        messages: {
          fetch: jest.fn()
            .mockResolvedValueOnce(mockMessages)
            .mockResolvedValueOnce({ size: 0 }),
        },
      };

      const result = await bot.fetchMessagesInRange(mockChannel, sinceDate);
      expect(result.length).toBe(2);
    });

    test('returns messages sorted oldest first', async () => {
      const older = makeMessage('1', '2026-01-01T10:00:00Z');
      const newer = makeMessage('2', '2026-01-01T12:00:00Z');
      const mockMessages = {
        size: 2,
        filter: (fn) => ({ values: () => [newer, older].filter(fn).values() }),
        last: () => older,
        values: () => [newer, older].values(),
      };

      const mockChannel = {
        messages: {
          fetch: jest.fn()
            .mockResolvedValueOnce(mockMessages)
            .mockResolvedValueOnce({ size: 0 }),
        },
      };

      const result = await bot.fetchMessagesInRange(mockChannel, sinceDate);
      expect(result[0].createdAt.getTime()).toBeLessThan(result[1].createdAt.getTime());
    });

    test('returns empty array when no messages exist', async () => {
      const mockChannel = {
        messages: { fetch: jest.fn().mockResolvedValue({ size: 0 }) },
      };
      const result = await bot.fetchMessagesInRange(mockChannel, sinceDate);
      expect(result).toEqual([]);
    });

    test('throws an error if fetch fails', async () => {
      const mockChannel = {
        messages: { fetch: jest.fn().mockRejectedValue(new Error('Missing Permissions')) },
      };
      await expect(bot.fetchMessagesInRange(mockChannel, sinceDate)).rejects.toThrow(
        'Failed to fetch messages: Missing Permissions'
      );
    });
  });

  // -------------------------------------------------------
  // !ping command
  // -------------------------------------------------------

  describe('!ping command', () => {
    let messageHandler;

    beforeEach(() => {
      messageHandler = getHandler(mockClient, Events.MessageCreate, 'on');
    });

    test('registers a listener for the messageCreate event', () => {
      expect(mockClient.on).toHaveBeenCalledWith(Events.MessageCreate, expect.any(Function));
    });

    test('ignores messages from bots', async () => {
      const msg = { author: { bot: true }, content: '!ping', reply: jest.fn() };
      await messageHandler(msg);
      expect(msg.reply).not.toHaveBeenCalled();
    });

    test('replies to !ping with the correct message', async () => {
      const msg = { author: { bot: false }, content: '!ping', reply: jest.fn() };
      await messageHandler(msg);
      expect(msg.reply).toHaveBeenCalledWith('Pong! Scrumlord is watching. 👑');
    });

    test('does not reply to unrecognized commands', async () => {
      const msg = { author: { bot: false }, content: '!unknown', reply: jest.fn() };
      await messageHandler(msg);
      expect(msg.reply).not.toHaveBeenCalled();
    });

    test('does not reply to regular messages', async () => {
      const msg = { author: { bot: false }, content: 'hey everyone', reply: jest.fn() };
      await messageHandler(msg);
      expect(msg.reply).not.toHaveBeenCalled();
    });

    test('does not reply to empty messages', async () => {
      const msg = { author: { bot: false }, content: '', reply: jest.fn() };
      await messageHandler(msg);
      expect(msg.reply).not.toHaveBeenCalled();
    });
  });

  // -------------------------------------------------------
  // !export command
  // -------------------------------------------------------

  describe('!export command', () => {
    let messageHandler;

    beforeEach(() => {
      messageHandler = getHandler(mockClient, Events.MessageCreate, 'on');
    });

    const makeExportMessage = (content) => ({
      author: { bot: false, tag: 'Alice#0001' },
      content,
      channel: {
        id: 'channel-123',
        name: 'general',
        send: jest.fn(),
        messages: { fetch: jest.fn() },
      },
      reply: jest.fn(),
    });

    test('shows usage message when no time range is provided', async () => {
      const msg = makeExportMessage('!export');
      await messageHandler(msg);
      expect(msg.reply).toHaveBeenCalledWith(expect.stringContaining('Usage'));
    });

    test('shows error for invalid time range format', async () => {
      const msg = makeExportMessage('!export badformat');
      await messageHandler(msg);
      expect(msg.reply).toHaveBeenCalledWith(expect.stringContaining('Invalid time range'));
    });

    test('shows "no messages found" when channel has no messages in range', async () => {
      const fetchingMsg = { edit: jest.fn() };
      const msg = makeExportMessage('!export 7d');
      msg.reply = jest.fn().mockResolvedValue(fetchingMsg);
      msg.channel.messages.fetch = jest.fn().mockResolvedValue({ size: 0 });

      await messageHandler(msg);
      expect(fetchingMsg.edit).toHaveBeenCalledWith(expect.stringContaining('No messages found'));
    });

    test('sends a JSON file attachment when messages are found', async () => {
      const fetchingMsg = { edit: jest.fn() };
      const msg = makeExportMessage('!export 7d');
      msg.reply = jest.fn().mockResolvedValue(fetchingMsg);

      const mockMsg = {
        id: '1',
        content: 'hello',
        createdAt: new Date(),
        author: { id: '111', username: 'Alice' },
      };
      const mockFetchResult = {
        size: 1,
        filter: (fn) => ({ values: () => [mockMsg].filter(fn).values() }),
        last: () => mockMsg,
        values: () => [mockMsg].values(),
      };
      msg.channel.messages.fetch = jest.fn()
        .mockResolvedValueOnce(mockFetchResult)
        .mockResolvedValueOnce({ size: 0 });

      await messageHandler(msg);

      expect(fetchingMsg.edit).toHaveBeenCalledWith(expect.stringContaining('Exported'));
      expect(msg.channel.send).toHaveBeenCalledWith(
        expect.objectContaining({ files: expect.any(Array) })
      );
    });

    test('reports missing permissions error', async () => {
      const fetchingMsg = { edit: jest.fn() };
      const msg = makeExportMessage('!export 7d');
      msg.reply = jest.fn()
        .mockResolvedValueOnce(fetchingMsg)
        .mockResolvedValueOnce(undefined);
      msg.channel.messages.fetch = jest.fn().mockRejectedValue(new Error('Missing Permissions'));

      await messageHandler(msg);
      expect(msg.reply).toHaveBeenCalledWith(expect.stringContaining('lacks permission'));
    });

    test('reports missing access error', async () => {
      const fetchingMsg = { edit: jest.fn() };
      const msg = makeExportMessage('!export 7d');
      msg.reply = jest.fn()
        .mockResolvedValueOnce(fetchingMsg)
        .mockResolvedValueOnce(undefined);
      msg.channel.messages.fetch = jest.fn().mockRejectedValue(new Error('Missing Access'));

      await messageHandler(msg);
      expect(msg.reply).toHaveBeenCalledWith(expect.stringContaining('cannot access'));
    });
  });
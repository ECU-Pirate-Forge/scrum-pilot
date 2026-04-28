// tests/commands.test.js
// Tests for commands.js: all !command handlers tested directly,
// independent of the index.js message router.

jest.mock('discord.js', () => ({
  ChannelType: { GuildText: 0, GuildVoice: 2 },
  PermissionsBitField: { Flags: { ManageGuild: 'ManageGuild' } },
  AttachmentBuilder: jest.fn().mockImplementation((buffer, options) => ({
    buffer,
    name: options?.name,
  })),
}));

jest.mock('../chat-summarizer', () => ({
  runSummarizer: jest.fn().mockResolvedValue(undefined),
}));

const { dispatchCommand, getRoutingConfig } = require('../commands');
const { runSummarizer } = require('../chat-summarizer');

// ── Helpers ───────────────────────────────────────────────────────────────────

function makeChannel(overrides = {}) {
  return {
    id:       'channel-1',
    name:     'general',
    send:     jest.fn().mockResolvedValue({ edit: jest.fn() }),
    messages: { fetch: jest.fn() },
    ...overrides,
  };
}

function makeMessage(content, overrides = {}) {
  return {
    author:  { bot: false, tag: 'User#0001' },
    content,
    channel: makeChannel(),
    guild:   null,
    member:  null,
    reply:   jest.fn(),
    ...overrides,
  };
}

const mockClient = { guilds: {} };

afterEach(() => {
  delete process.env.SCRUMLORD_READ_CHANNEL_ID;
  delete process.env.SCRUMLORD_SPEAK_CHANNEL_ID;
  jest.clearAllMocks();
});

// ── getRoutingConfig ──────────────────────────────────────────────────────────

describe('getRoutingConfig', () => {
  test('returns isConfigured: false when env vars are absent', () => {
    const config = getRoutingConfig();
    expect(config.readChannelId).toBeNull();
    expect(config.speakChannelId).toBeNull();
    expect(config.isConfigured).toBe(false);
  });

  test('returns isConfigured: true when both vars are set', () => {
    process.env.SCRUMLORD_READ_CHANNEL_ID  = '111';
    process.env.SCRUMLORD_SPEAK_CHANNEL_ID = '222';
    const config = getRoutingConfig();
    expect(config.readChannelId).toBe('111');
    expect(config.speakChannelId).toBe('222');
    expect(config.isConfigured).toBe(true);
  });
});

// ── dispatchCommand — routing gate ────────────────────────────────────────────

describe('routing gate', () => {
  test('returns false for commands outside configured read channel', async () => {
    process.env.SCRUMLORD_READ_CHANNEL_ID  = '111';
    process.env.SCRUMLORD_SPEAK_CHANNEL_ID = '222';

    const msg = makeMessage('!ping', {
      channel: makeChannel({ id: '999' }),
      guild:   { channels: { fetch: jest.fn() } },
    });

    expect(await dispatchCommand(msg, mockClient)).toBe(false);
    expect(msg.reply).not.toHaveBeenCalled();
  });

  test('processes commands from the configured read channel', async () => {
    process.env.SCRUMLORD_READ_CHANNEL_ID  = '111';
    process.env.SCRUMLORD_SPEAK_CHANNEL_ID = '222';

    const speakCh = { send: jest.fn() };
    const msg     = makeMessage('!ping', {
      channel: makeChannel({ id: '111' }),
      guild:   {
        channels: {
          fetch: jest.fn(id => id === '222' ? Promise.resolve(speakCh) : Promise.resolve(null)),
        },
      },
    });

    await dispatchCommand(msg, mockClient);
    expect(speakCh.send).toHaveBeenCalledWith('Pong! Scrumlord is watching. 👑');
  });

  test('returns false for unknown commands', async () => {
    const msg = makeMessage('!unknown');
    expect(await dispatchCommand(msg, mockClient)).toBe(false);
  });
});

// ── !ping ─────────────────────────────────────────────────────────────────────

describe('!ping', () => {
  test('replies with pong message', async () => {
    const msg = makeMessage('!ping');
    await dispatchCommand(msg, mockClient);
    expect(msg.reply).toHaveBeenCalledWith('Pong! Scrumlord is watching. 👑');
  });

  test('returns true', async () => {
    const msg = makeMessage('!ping');
    expect(await dispatchCommand(msg, mockClient)).toBe(true);
  });

  test('sends to speak channel when routing is configured', async () => {
    process.env.SCRUMLORD_READ_CHANNEL_ID  = '111';
    process.env.SCRUMLORD_SPEAK_CHANNEL_ID = '222';

    const speakCh = { send: jest.fn() };
    const msg     = makeMessage('!ping', {
      channel: makeChannel({ id: '111' }),
      guild:   {
        channels: {
          fetch: jest.fn(id => id === '222' ? Promise.resolve(speakCh) : Promise.resolve(null)),
        },
      },
    });

    await dispatchCommand(msg, mockClient);
    expect(speakCh.send).toHaveBeenCalledWith('Pong! Scrumlord is watching. 👑');
    expect(msg.reply).not.toHaveBeenCalled();
  });
});

// ── !export ───────────────────────────────────────────────────────────────────

describe('!export', () => {
  function makeExportMsg(content, fetchImpl) {
    const statusMsg = { edit: jest.fn() };
    const channel   = makeChannel({
      send:     jest.fn().mockResolvedValue(statusMsg),
      messages: { fetch: fetchImpl || jest.fn().mockResolvedValue({ size: 0 }) },
    });
    return {
      msg:       makeMessage(content, { channel }),
      statusMsg,
    };
  }

  function makeBatch(msgs) {
    return {
      size:   msgs.length,
      filter: fn => ({ values: () => msgs.filter(fn)[Symbol.iterator]() }),
      last:   ()  => msgs[msgs.length - 1],
    };
  }

  test('shows usage when no time range is given', async () => {
    const msg = makeMessage('!export');
    await dispatchCommand(msg, mockClient);
    expect(msg.reply).toHaveBeenCalledWith(expect.stringContaining('Usage'));
  });

  test('shows error for invalid time range format', async () => {
    const msg = makeMessage('!export badformat');
    await dispatchCommand(msg, mockClient);
    expect(msg.reply).toHaveBeenCalledWith(expect.stringContaining('Invalid format'));
  });

  test('edits status message when no messages are found', async () => {
    const { msg, statusMsg } = makeExportMsg('!export 7d',
      jest.fn().mockResolvedValue({ size: 0 })
    );
    await dispatchCommand(msg, mockClient);
    expect(statusMsg.edit).toHaveBeenCalledWith(expect.stringContaining('No messages found'));
  });

  test('sends a JSON attachment when messages are found', async () => {
    const mockMsg = {
      id:        '1',
      content:   'hello',
      createdAt: new Date(),
      author:    { id: '111', username: 'Alice' },
    };
    const { msg, statusMsg } = makeExportMsg('!export 7d',
      jest.fn()
        .mockResolvedValueOnce(makeBatch([mockMsg]))
        .mockResolvedValueOnce({ size: 0 })
    );

    await dispatchCommand(msg, mockClient);
    expect(statusMsg.edit).toHaveBeenCalledWith(expect.stringContaining('Exported'));
    expect(msg.channel.send).toHaveBeenCalledWith(
      expect.objectContaining({ files: expect.any(Array) })
    );
  });

  test('reports missing permissions error', async () => {
    const { msg } = makeExportMsg('!export 7d',
      jest.fn().mockRejectedValue(new Error('Missing Permissions'))
    );
    await dispatchCommand(msg, mockClient);
    expect(msg.reply).toHaveBeenCalledWith(expect.stringContaining('lacks permission'));
  });

  test('reports missing access error', async () => {
    const { msg } = makeExportMsg('!export 7d',
      jest.fn().mockRejectedValue(new Error('Missing Access'))
    );
    await dispatchCommand(msg, mockClient);
    expect(msg.reply).toHaveBeenCalledWith(expect.stringContaining('cannot access'));
  });

  test('returns true', async () => {
    const msg = makeMessage('!export');
    expect(await dispatchCommand(msg, mockClient)).toBe(true);
  });
});

// ── !summarize ────────────────────────────────────────────────────────────────

describe('!summarize', () => {
  test('shows usage when no time range is given', async () => {
    const msg = makeMessage('!summarize');
    await dispatchCommand(msg, mockClient);
    expect(msg.reply).toHaveBeenCalledWith(expect.stringContaining('Usage'));
  });

  test('shows error for invalid format', async () => {
    const msg = makeMessage('!summarize badformat');
    await dispatchCommand(msg, mockClient);
    expect(msg.reply).toHaveBeenCalledWith(expect.stringContaining('Invalid format'));
  });

  test('calls runSummarizer on valid time range', async () => {
    const statusMsg = { edit: jest.fn() };
    const msg       = makeMessage('!summarize 24h', {
      channel:  makeChannel({ send: jest.fn().mockResolvedValue(statusMsg) }),
      author:   { bot: false, tag: 'User#0001' },
    });
    await dispatchCommand(msg, mockClient);
    expect(runSummarizer).toHaveBeenCalledWith(mockClient);
    expect(statusMsg.edit).toHaveBeenCalledWith(expect.stringContaining('complete'));
  });

  test('reports error when runSummarizer throws', async () => {
    runSummarizer.mockRejectedValueOnce(new Error('API failure'));
    const statusMsg = { edit: jest.fn() };
    const msg       = makeMessage('!summarize 7d', {
      channel: makeChannel({ send: jest.fn().mockResolvedValue(statusMsg) }),
    });
    await dispatchCommand(msg, mockClient);
    expect(msg.reply).toHaveBeenCalledWith(expect.stringContaining('API failure'));
  });

  test('returns true', async () => {
    const msg = makeMessage('!summarize');
    expect(await dispatchCommand(msg, mockClient)).toBe(true);
  });
});

// ── !setchannels ──────────────────────────────────────────────────────────────

describe('!setchannels', () => {
  function makeSetChannelsMsg(content, permGranted = true) {
    return makeMessage(content, {
      guild: {
        channels: {
          fetch: jest.fn(id => {
            if (id === '111' || id === '222') {
              return Promise.resolve({ id, isTextBased: () => true });
            }
            return Promise.resolve(null);
          }),
        },
      },
      member: {
        permissions: { has: jest.fn().mockReturnValue(permGranted) },
      },
    });
  }

  test('always processes regardless of read channel restriction', async () => {
    process.env.SCRUMLORD_READ_CHANNEL_ID  = '999';
    process.env.SCRUMLORD_SPEAK_CHANNEL_ID = '888';

    const msg = makeSetChannelsMsg('!setchannels <#111> <#222>');
    msg.channel.id = 'some-other-channel';

    await dispatchCommand(msg, mockClient);
    // Should have replied (not silently ignored).
    expect(msg.reply).toHaveBeenCalled();
  });

  test('rejects without Manage Server permission', async () => {
    const msg = makeSetChannelsMsg('!setchannels <#111> <#222>', false);
    await dispatchCommand(msg, mockClient);
    expect(msg.reply).toHaveBeenCalledWith(expect.stringContaining('Manage Server'));
  });

  test('shows usage when fewer than two channels are given', async () => {
    const msg = makeSetChannelsMsg('!setchannels <#111>');
    await dispatchCommand(msg, mockClient);
    expect(msg.reply).toHaveBeenCalledWith(expect.stringContaining('Usage'));
  });

  test('replies with env var lines for valid channel pair', async () => {
    const msg = makeSetChannelsMsg('!setchannels <#111> <#222>');
    await dispatchCommand(msg, mockClient);
    expect(msg.reply).toHaveBeenCalledWith(
      expect.stringContaining('SCRUMLORD_READ_CHANNEL_ID=111')
    );
    expect(msg.reply).toHaveBeenCalledWith(
      expect.stringContaining('SCRUMLORD_SPEAK_CHANNEL_ID=222')
    );
  });

  test('rejects invalid channel references', async () => {
    const msg = makeSetChannelsMsg('!setchannels not-a-channel also-bad');
    await dispatchCommand(msg, mockClient);
    expect(msg.reply).toHaveBeenCalledWith(expect.stringContaining('Invalid'));
  });

  test('returns true', async () => {
    const msg = makeSetChannelsMsg('!setchannels <#111> <#222>');
    expect(await dispatchCommand(msg, mockClient)).toBe(true);
  });
});

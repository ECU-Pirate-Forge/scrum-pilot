// tests/utils.test.js
// Tests for utils.js: pure helper functions and shared Discord utilities.

jest.mock('discord.js', () => ({
  ChannelType: { GuildText: 0, GuildVoice: 2 },
}));

const {
  parseTimeRange,
  extractChannelId,
  formatMessagesToJson,
  fetchMessagesInRange,
  isValidDate,
  isInRange,
  estDateOf,
  groupByDay,
  formatDate,
  getDisplayName,
} = require('../utils');

// ── parseTimeRange ────────────────────────────────────────────────────────────

describe('parseTimeRange', () => {
  test('returns a date N days in the past for Xd format', () => {
    const result   = parseTimeRange('7d');
    const expected = new Date();
    expected.setDate(expected.getDate() - 7);
    expect(result.getTime()).toBeCloseTo(expected.getTime(), -3);
  });

  test('returns a date N hours in the past for Xh format', () => {
    const result   = parseTimeRange('24h');
    const expected = new Date();
    expected.setHours(expected.getHours() - 24);
    expect(result.getTime()).toBeCloseTo(expected.getTime(), -3);
  });

  test('returns a date N minutes in the past for Xm format', () => {
    const result   = parseTimeRange('30m');
    const expected = new Date();
    expected.setMinutes(expected.getMinutes() - 30);
    expect(result.getTime()).toBeCloseTo(expected.getTime(), -3);
  });

  test('returns null for unknown unit', () => {
    expect(parseTimeRange('7x')).toBeNull();
  });

  test('returns null for number without unit', () => {
    expect(parseTimeRange('7')).toBeNull();
  });

  test('returns null for empty string', () => {
    expect(parseTimeRange('')).toBeNull();
  });

  test('returns null for unit without number', () => {
    expect(parseTimeRange('d')).toBeNull();
  });

  test('returns null for undefined', () => {
    expect(parseTimeRange(undefined)).toBeNull();
  });
});

// ── extractChannelId ──────────────────────────────────────────────────────────

describe('extractChannelId', () => {
  test('parses a channel mention', () => {
    expect(extractChannelId('<#123456789>')).toBe('123456789');
  });

  test('passes through a raw numeric ID', () => {
    expect(extractChannelId('123456789')).toBe('123456789');
  });

  test('returns null for a channel name', () => {
    expect(extractChannelId('general')).toBeNull();
  });

  test('returns null for null', () => {
    expect(extractChannelId(null)).toBeNull();
  });

  test('returns null for empty string', () => {
    expect(extractChannelId('')).toBeNull();
  });

  test('trims surrounding whitespace', () => {
    expect(extractChannelId('  <#111>  ')).toBe('111');
  });
});

// ── formatMessagesToJson ──────────────────────────────────────────────────────

describe('formatMessagesToJson', () => {
  const mockMessages = [
    {
      author:    { id: '111', username: 'Alice' },
      content:   'Hello there',
      createdAt: new Date('2026-01-01T10:00:00Z'),
    },
    {
      author:    { id: '222', username: 'Bob' },
      content:   'General Kenobi',
      createdAt: new Date('2026-01-01T10:01:00Z'),
    },
  ];

  test('returns a valid JSON string', () => {
    expect(() => JSON.parse(formatMessagesToJson(mockMessages))).not.toThrow();
  });

  test('includes the correct fields for each message', () => {
    const result = JSON.parse(formatMessagesToJson(mockMessages));
    expect(result[0]).toEqual({
      author:    { id: '111', username: 'Alice' },
      content:   'Hello there',
      timestamp: '2026-01-01T10:00:00.000Z',
    });
  });

  test('returns the correct number of messages', () => {
    expect(JSON.parse(formatMessagesToJson(mockMessages))).toHaveLength(2);
  });

  test('returns an empty array for no messages', () => {
    expect(JSON.parse(formatMessagesToJson([]))).toEqual([]);
  });
});

// ── fetchMessagesInRange ──────────────────────────────────────────────────────

describe('fetchMessagesInRange', () => {
  const sinceDate = new Date('2026-01-01T00:00:00Z');

  const makeMsg = (id, createdAt) => ({
    id,
    content:   'test',
    createdAt: new Date(createdAt),
    author:    { id: '111', username: 'Alice' },
  });

  function makeBatch(msgs, size = msgs.length) {
    return {
      size,
      filter: fn => ({ values: () => msgs.filter(fn)[Symbol.iterator]() }),
      last:   ()  => msgs[msgs.length - 1],
      values: ()  => msgs[Symbol.iterator](),
    };
  }

  test('returns messages within the time range', async () => {
    const msg1 = makeMsg('1', '2026-01-01T12:00:00Z');
    const msg2 = makeMsg('2', '2026-01-01T13:00:00Z');

    const channel = {
      name:     'general',
      messages: {
        fetch: jest.fn()
          .mockResolvedValueOnce(makeBatch([msg1, msg2]))
          .mockResolvedValueOnce({ size: 0 }),
      },
    };

    const result = await fetchMessagesInRange(channel, sinceDate);
    expect(result).toHaveLength(2);
  });

  test('returns messages sorted oldest first', async () => {
    const older = makeMsg('1', '2026-01-01T10:00:00Z');
    const newer = makeMsg('2', '2026-01-01T12:00:00Z');

    const channel = {
      name:     'general',
      messages: {
        fetch: jest.fn()
          .mockResolvedValueOnce(makeBatch([newer, older]))
          .mockResolvedValueOnce({ size: 0 }),
      },
    };

    const result = await fetchMessagesInRange(channel, sinceDate);
    expect(result[0].id).toBe('1'); // older first
    expect(result[1].id).toBe('2');
  });

  test('stops paginating when a message is before sinceDate', async () => {
    const recent = makeMsg('2', '2026-01-02T00:00:00Z');
    const old    = makeMsg('1', '2025-12-31T00:00:00Z'); // before sinceDate

    const channel = {
      name:     'general',
      messages: { fetch: jest.fn().mockResolvedValue(makeBatch([recent, old])) },
    };

    const result = await fetchMessagesInRange(channel, sinceDate);
    expect(result).toHaveLength(1);
    expect(result[0].id).toBe('2');
  });

  test('returns empty array when no messages exist', async () => {
    const channel = {
      name:     'general',
      messages: { fetch: jest.fn().mockResolvedValue({ size: 0 }) },
    };
    expect(await fetchMessagesInRange(channel, sinceDate)).toEqual([]);
  });

  test('throws a descriptive error if fetch fails', async () => {
    const channel = {
      name:     'general',
      messages: { fetch: jest.fn().mockRejectedValue(new Error('Missing Permissions')) },
    };
    await expect(fetchMessagesInRange(channel, sinceDate))
      .rejects.toThrow('Failed to fetch messages from #general: Missing Permissions');
  });
});

// ── isValidDate ───────────────────────────────────────────────────────────────

describe('isValidDate', () => {
  test('returns true for a valid YYYY-MM-DD date', () => {
    expect(isValidDate('2026-04-15')).toBe(true);
  });

  test('returns false for an invalid date value', () => {
    expect(isValidDate('2026-13-01')).toBe(false);
  });

  test('returns false for wrong format', () => {
    expect(isValidDate('04/15/2026')).toBe(false);
  });

  test('returns false for an empty string', () => {
    expect(isValidDate('')).toBe(false);
  });
});

// ── isInRange ─────────────────────────────────────────────────────────────────

describe('isInRange', () => {
  test('returns true when date is within range', () => {
    expect(isInRange('2026-03-15', '2026-03-01', '2026-03-31')).toBe(true);
  });

  test('returns true on the start boundary', () => {
    expect(isInRange('2026-03-01', '2026-03-01', '2026-03-31')).toBe(true);
  });

  test('returns true on the end boundary', () => {
    expect(isInRange('2026-03-31', '2026-03-01', '2026-03-31')).toBe(true);
  });

  test('returns false before the start', () => {
    expect(isInRange('2026-02-28', '2026-03-01', '2026-03-31')).toBe(false);
  });

  test('returns false after the end', () => {
    expect(isInRange('2026-04-01', '2026-03-01', '2026-03-31')).toBe(false);
  });
});

// ── estDateOf ─────────────────────────────────────────────────────────────────

describe('estDateOf', () => {
  test('returns a YYYY-MM-DD string in EST timezone', () => {
    // 2026-04-15 at midnight UTC = 2026-04-14 in EST (UTC-4 in April)
    const msg = { createdTimestamp: new Date('2026-04-15T04:00:00Z').getTime() };
    expect(estDateOf(msg)).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });

  test('returns the correct date for a known timestamp', () => {
    // Noon UTC on April 15 = April 15 in EST
    const msg = { createdTimestamp: new Date('2026-04-15T16:00:00Z').getTime() };
    expect(estDateOf(msg)).toBe('2026-04-15');
  });
});

// ── groupByDay ────────────────────────────────────────────────────────────────

describe('groupByDay', () => {
  test('groups messages by their EST calendar date', () => {
    const msgs = [
      { createdTimestamp: new Date('2026-04-15T16:00:00Z').getTime() }, // Apr 15 EST
      { createdTimestamp: new Date('2026-04-15T20:00:00Z').getTime() }, // Apr 15 EST
      { createdTimestamp: new Date('2026-04-16T16:00:00Z').getTime() }, // Apr 16 EST
    ];
    const groups = groupByDay(msgs);
    expect(groups.size).toBe(2);
    expect(groups.get('2026-04-15')).toHaveLength(2);
    expect(groups.get('2026-04-16')).toHaveLength(1);
  });

  test('returns an empty map for no messages', () => {
    expect(groupByDay([])).toEqual(new Map());
  });
});

// ── formatDate ────────────────────────────────────────────────────────────────

describe('formatDate', () => {
  test('returns a human-readable EST date string', () => {
    const ts     = new Date('2026-04-15T16:00:00Z').getTime();
    const result = formatDate(ts);
    expect(result).toContain('2026');
    expect(result).toContain('April');
    expect(result).toMatch(/ET$/); // ends with timezone abbreviation
  });

  test('accepts a numeric string timestamp', () => {
    const ts = String(new Date('2026-04-15T16:00:00Z').getTime());
    expect(() => formatDate(ts)).not.toThrow();
  });
});

// ── getDisplayName ────────────────────────────────────────────────────────────

describe('getDisplayName', () => {
  function makeGuild(member) {
    return { members: { cache: { get: jest.fn().mockReturnValue(member) } } };
  }

  test('returns displayName when member has one', () => {
    const guild = makeGuild({ displayName: 'Cool Alias', user: { username: 'realname' } });
    expect(getDisplayName(guild, 'u1')).toBe('Cool Alias');
  });

  test('falls back to username when displayName is absent', () => {
    const guild = makeGuild({ displayName: null, user: { username: 'realname' } });
    expect(getDisplayName(guild, 'u1')).toBe('realname');
  });

  test('falls back to User-{id} when member is not cached', () => {
    const guild = makeGuild(undefined);
    expect(getDisplayName(guild, 'u99')).toBe('User-u99');
  });
});

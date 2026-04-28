# Scrumlord — Discord Bot

Scrumlord is the Discord bot component of Scrum Pilot. It monitors voice channels for standups, records and transcribes them, summarizes chat activity, generates sprint roll-ups, and scores team agile health via the Agile Integrity Index (AII). It also supports conversational @mentions through an AI agent interface.

---

## Prerequisites

- [Node.js](https://nodejs.org/) v18 or higher
- [ffmpeg](https://ffmpeg.org/) (bundled via `ffmpeg-static` — no manual install needed)
- A Discord application with a bot token ([Discord Developer Portal](https://discord.com/developers/applications))
- An [Anthropic API key](https://console.anthropic.com/) (Claude — primary summarizer)
- An [OpenAI API key](https://platform.openai.com/) (Whisper transcription + GPT-4o-mini fallback)

## Installation
```bash
cd discord-bot
npm install
```

## Configuration

Copy `.env.example` to `.env` and fill in your values:

```bash
cp .env.example .env
```

> **Never commit `.env`.** It is already in `.gitignore`.

### Environment Variables

#### Discord credentials
| Variable | Required | Description |
|---|---|---|
| `DISCORD_TOKEN` | ✅ | Bot token from the Discord Developer Portal → Bot tab |
| `CLIENT_ID` | ✅ | Application ID from the Developer Portal → General Information tab |

#### AI providers
| Variable | Required | Description |
|---|---|---|
| `ANTHROPIC_API_KEY` | ✅ | Used for all AI summarization (Claude) |
| `OPENAI_API_KEY` | ✅ | Used for Whisper transcription and as a Claude fallback |

All AI calls use Claude first and fall back to GPT-4o-mini automatically on failure.

#### Channel routing
| Variable | Required | Description |
|---|---|---|
| `SCRUMLORD_READ_CHANNEL_ID` | ❌ | Channel ID where Scrumlord reads `!commands`. Set with `!setchannels`. |
| `SCRUMLORD_SPEAK_CHANNEL_ID` | ❌ | Channel ID where Scrumlord posts command output. Set with `!setchannels`. |

If these are not set, commands are accepted from any channel and replies go to the same channel.

#### Voice recording
| Variable | Required | Description |
|---|---|---|
| `VOICE_CHANNELS` | ✅ | Comma-separated channel IDs to monitor. Scrumlord joins when ≥2 humans are present. |

#### Chat summarization
| Variable | Default | Description |
|---|---|---|
| `CHAT_SUMMARIES_CHANNEL` | `chat-summaries` | Channel where daily chat summaries are posted. |
| `SUMMARIES_DIR` | `./summaries` | Local path where summary `.md` files are stored. |
| `SUMMARIZE_CHANNELS` | *(all text channels)* | Comma-separated channel IDs to include. Leave empty to summarize every non-system channel. |
| `CHAT_SUMMARY_CRON` | `0 2 * * *` | Cron schedule for the daily chat summary sweep. |

#### Sprint summaries
| Variable | Default | Description |
|---|---|---|
| `SPRINT_SUMMARY_CRON` | `30 2 * * *` | Cron schedule for the sprint-end detection check (runs after the chat cron). |
| `SPRINT_CHANNEL` | `scrumlord-generated-sprint-recaps` | Channel where sprint roll-ups and AII scores are posted. |
| `SPRINT_EXCLUDE_TEAMS` | *(none)* | Comma-separated Discord category names to skip when auto-detecting teams. |

### Getting a Bot Token
1. Go to the [Discord Developer Portal](https://discord.com/developers/applications)
2. Select your application (or create a new one)
3. Navigate to **Bot** → **Reset Token** and copy the token into `DISCORD_TOKEN`
4. Under **Privileged Gateway Intents**, enable: Server Members, Presence, Message Content

### Channel ID Setup

Run `!setchannels #read-channel #speak-channel` inside Discord. The bot will print the two `.env` lines to add. Restart the bot after editing `.env`.

---

## Running

### Start the bot
```bash
node index.js
```

### Reprocess leftover recordings
If `.ogg` files are present in the bot directory from a previous interrupted session, run:
```bash
node reprocess.js
```
This picks up both the legacy single-track format (`recording-<timestamp>.ogg`) and the current per-user multi-track format, and runs them through the full transcription and summarization pipeline.

### Run tests
```bash
npm test
```

> **Note for test authors:** The utility functions previously exported from `index.js` under `NODE_ENV=test` have moved. Import them directly from their home modules instead:
> ```js
> const { parseTimeRange, formatMessagesToJson, fetchMessagesInRange, extractChannelId } = require('./utils');
> const { getRoutingConfig } = require('./commands'); // was getChannelRoutingConfig
> ```

---

## Commands

All commands are prefixed with `!`. Once `SCRUMLORD_READ_CHANNEL_ID` is configured, commands are only accepted from that channel (except `!setchannels`, which always works).

### Core

| Command | Description |
|---|---|
| `!ping` | Confirms the bot is online and listening. |
| `!setchannels <read> <speak>` | Prints the `.env` lines needed to configure channel routing. Requires Manage Server permission. Accepts mentions (`#channel`) or raw IDs. |

### Export & Summarization

| Command | Description |
|---|---|
| `!export <time_range>` | Exports messages from the read channel as a JSON attachment. Time range examples: `7d`, `24h`, `30m`. |
| `!summarize <time_range>` | Triggers an immediate full summarization sweep across all configured channels. The time range argument is validated but the sweep always picks up from the last saved cursor per channel. |

### Sprint Management

| Command | Description |
|---|---|
| `!listteams` | Lists all Discord category names detected as team identifiers. |
| `!sprintsummary <YYYY-MM-DD> <YYYY-MM-DD> [--team <Name>]` | Generates a sprint roll-up for the given date range, optionally scoped to one team. |
| `!sprintbackfill` | Generates sprint summaries for all past sprints defined in `config.js` across all teams. Skips already-summarized sprints. |

### Agile Integrity Index (AII)

| Command | Description |
|---|---|
| `!aiiscore <Sprint N> [--team <Name>]` | Runs AII scoring for a named sprint and optional team. Also accepts date pairs: `!aiiscore 2026-03-22 2026-04-04`. |
| `!aiibackfill` | Scores all past sprints that haven't been scored yet. |
| `!aiirescore <Sprint N \| all> [--team <Name>]` | Re-scores a sprint (or all sprints), replacing existing AII posts. Sprint summaries are untouched. |
| `!aiihistory [--team <Name>]` | Prints a table of all recorded AII scores. |

### Agent

@mention Scrumlord in any channel to open a conversational thread. Subsequent messages in that thread are handled automatically — no `@` required after the first.

---

## Features

### Voice Recording & Transcription

Scrumlord automatically joins a monitored voice channel when at least 2 humans are present and records per-user audio tracks (`.ogg`). When the last human leaves, it:

1. Waits for all per-user ffmpeg processes to finish writing
2. Skips entirely silent tracks
3. Compresses each track to MP3 via ffmpeg (silence removal → 32kbps → 16kbps fallback if needed)
4. Transcribes each track via OpenAI Whisper
5. Combines speaker-labeled transcripts and summarizes via Claude
6. Posts a markdown recap to `#standup-recap`

If Scrumlord is already recording one channel and a meeting starts in another, it warns in `#bot-commands` and queues the second channel.

### Daily Chat Summarization

A daily cron sweep (default 2:00 AM) processes new messages in all configured text channels since the last run. Each channel's history is grouped by calendar day (EST) and summarized by Claude, with prior summaries included for continuity context. Summaries are saved as markdown files under `SUMMARIES_DIR` and posted to `CHAT_SUMMARIES_CHANNEL`.

### Sprint Summaries

At sprint end (detected by the cron against `SPRINT_SCHEDULE` in `config.js`), Scrumlord synthesizes all chat summaries and standup recaps from the sprint period into a structured roll-up posted as a threaded message in `SPRINT_CHANNEL`. Sprints can also be triggered manually with `!sprintsummary`.

### Agile Integrity Index (AII)

After each sprint summary is generated, AII scoring runs automatically in the same thread. It evaluates Agile Manifesto alignment, Scrum practice adherence, and anti-pattern detection on a 100-point scale, producing a structured JSON score stored in `data/aii-scores.json` alongside a Discord embed with trend charts.

---

## Project Structure

```
discord-bot/
├── index.js              # Entry point — client setup, event wiring, cron scheduling
├── commands.js           # All ! command handlers and the dispatchCommand() router
├── recorder.js           # Voice recording, per-user audio, Whisper transcription
├── chat-summarizer.js    # Daily chat summaries and sprint roll-ups
├── reprocess.js          # Standalone script to reprocess leftover .ogg files
├── config.js             # Central constants — channels, schedule, sprint definitions
├── ai.js                 # Shared AI clients (Anthropic + OpenAI) and callAI() helper
├── utils.js              # Pure utility functions — date formatting, message fetching, etc.
├── aii/
│   ├── aii.js            # AII scoring engine and !aii* command handlers
│   ├── aii-composite.js  # Optional: PNG chart generation
│   └── aii-dashboard.js  # Optional: HTML dashboard generation
├── agent/
│   └── conversate.js     # AI agent for @mention conversations and thread continuity
├── data/
│   └── aii-scores.json   # Persistent AII score store
├── summaries/            # Per-channel dated markdown summary files
│   └── <channel-name>/
│       └── YYYY-MM-DD.md
├── tests/
│   ├── index.test.js     # Client init, event registration, message routing
│   ├── commands.test.js  # All ! command handlers
│   └── utils.test.js     # Utility functions
├── chat-state.json       # Per-channel message cursor for incremental summarization
├── .env                  # Local secrets — never commit
├── .env.example          # Template with all supported variables documented
├── .gitignore
├── package.json
└── README.md
```

## Contributing

Please read `CONTRIBUTING.md` and `CODE_OF_CONDUCT.md` at the repository root before submitting changes.

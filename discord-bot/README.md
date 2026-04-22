# ScrumLord — Discord Bot

ScrumLord is the Discord bot companion to [ScrumPilot](../README.md). It passively monitors your team's Discord server to collect communication data and uses AI to produce daily chat summaries and end-of-sprint recap reports — giving teams a searchable, structured record of their collaboration without any manual effort.

---

## Features

- **Voice Recording** — automatically joins configured voice channels when quorum is reached and records the session using `@discordjs/voice` + ffmpeg. Recordings are saved locally as `.ogg` files.
- **AI Chat Summaries** — at a configurable cron time (default 2:00 AM), summarizes the day's messages in each active channel using Claude (primary) or GPT-4o-mini (fallback). Summaries are posted to a `#chat-summaries` Discord channel and saved as Markdown files.
- **Sprint Summaries** — at sprint end, automatically collects all daily chat summaries for the sprint window and uses AI to produce a consolidated sprint recap, posted to a dedicated Discord channel.
- **Message Export** — exports raw channel messages as JSON for a specified time range.
- **Persistent Context** — prior summary sections are fed back as context so the AI can flag ongoing topics across days.

---

## Prerequisites

- [Node.js](https://nodejs.org/) v16 or higher
- A Discord account with a server you can administrate
- An [Anthropic API key](https://console.anthropic.com/) (Claude, primary AI)
- An [OpenAI API key](https://platform.openai.com/) (GPT-4o-mini, fallback AI)
- ffmpeg (bundled via `ffmpeg-static` — no separate install needed)

---

## Installation

```bash
cd discord-bot
npm install
```

---

## Configuration

Create a `.env` file in the `discord-bot/` directory:

```env
# Discord
DISCORD_TOKEN=your_bot_token_here
SCRUMLORD_READ_CHANNEL_ID=channel_id_bot_listens_for_commands
SCRUMLORD_SPEAK_CHANNEL_ID=channel_id_bot_replies_to

# AI providers
ANTHROPIC_API_KEY=your_anthropic_key
OPENAI_API_KEY=your_openai_key

# Voice recording (comma-separated channel IDs to auto-record)
VOICE_CHANNELS=channel_id_1,channel_id_2

# Chat summarizer (all optional — defaults shown)
CHAT_SUMMARIES_CHANNEL=chat-summaries
SUMMARIES_DIR=./summaries
CHAT_SUMMARY_CRON=0 2 * * *
SPRINT_SUMMARY_CRON=30 2 * * *
BOT_COMMANDS=bot-commands
SPRINT_CHANNEL=scrumlord-generated-sprint-recaps
```

> **Never commit your `.env` file.** It is already covered by `.gitignore`.

### Getting a Bot Token

1. Go to the [Discord Developer Portal](https://discord.com/developers/applications)
2. Select your application or create a new one
3. Navigate to the **Bot** tab
4. Enable **Message Content Intent**, **Server Members Intent**, and **Presence Intent** under Privileged Gateway Intents
5. Click **Reset Token** and copy it into your `.env` file

### Channel Routing

ScrumLord routes command input and output through two dedicated channels:

- `SCRUMLORD_READ_CHANNEL_ID` — the channel ScrumLord accepts commands from
- `SCRUMLORD_SPEAK_CHANNEL_ID` — the channel ScrumLord posts replies to

Use `!setchannels <read_channel> <speak_channel>` from any channel (requires **Manage Server** permission) to configure routing at runtime, or set the IDs directly in `.env`.

---

## Running the Bot

```bash
node index.js
```

---

## Commands

All commands must be sent in the configured read channel (or any channel before routing is configured).

| Command | Description |
|---|---|
| `!ping` | Confirms the bot is alive and listening |
| `!setchannels <read> <speak>` | Sets the command input/output channels. Requires **Manage Server** permission. Accepts channel mentions (`#channel`) or raw IDs. |
| `!export <time_range>` | Exports messages from the read channel as a JSON attachment. Time range format: `7d`, `24h`, `30m` |
| `!summarize <time_range>` | Triggers an immediate AI summary of the read channel for the given time range. Time range format: `7d`, `24h`, `30m` |
| `!sprintsummary <start> <end> [--team <TeamName>]` | Generates a sprint summary from saved daily chat summaries between two dates (format: `YYYY-MM-DD`). Optionally scoped to a team. |

---

## Automated Schedules

ScrumLord runs two cron jobs automatically:

| Job | Default Schedule | Description |
|---|---|---|
| **Daily Chat Summary** | `0 2 * * *` (2:00 AM) | Summarizes all active channels and posts results to `#chat-summaries` |
| **Sprint End Summary** | `30 2 * * *` (2:30 AM) | Checks if today is a sprint end date and, if so, runs full sprint summaries for all teams |

Override schedules via `CHAT_SUMMARY_CRON` and `SPRINT_SUMMARY_CRON` environment variables (standard cron syntax).

---

## Project Structure

```
discord-bot/
├── index.js             # Entry point — Discord client, command routing, voice state handling
├── chat-summarizer.js   # AI chat & sprint summarization logic, cron scheduling
├── recorder.js          # Voice channel recording (discordjs/voice + ffmpeg)
├── config.js            # Shared configuration constants
├── tests/
│   └── index.test.js    # Jest test suite
├── .env                 # Local secrets (do not commit)
└── package.json
```

---

## Discord Server Setup

Recommended channel structure for ScrumLord:

| Channel | Purpose |
|---|---|
| `#bot-commands` | Where team members send `!` commands |
| `#scrumlord-updates` | Where ScrumLord posts command replies |
| `#chat-summaries` | Where daily AI summaries are posted |
| `#scrumlord-generated-sprint-recaps` | Where sprint recap reports are posted |
| `#standup-recap` | Fallback sprint summary channel |

---

## Running Tests

```bash
npm test
```

---

## Contributing

Please read the root-level [CONTRIBUTING.md](../CONTRIBUTING.md) and [CODE_OF_CONDUCT.md](../CODE_OF_CONDUCT.md) before submitting changes.

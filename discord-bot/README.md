# Scrumlord — Discord Bot

Scrumlord is the Discord bot component of Scrum Pilot. It serves as the communication and data collection layer, monitoring Discord activity and interfacing with GitHub Projects and the AI backend.

## Prerequisites
- [Node.js](https://nodejs.org/) v16 or higher
- A Discord account with a server you can administrate

## Installation
```bash
cd discord-bot
npm install
```

## Configuration
Create a `.env` file in the `discord-bot` directory:
```
DISCORD_TOKEN=your_bot_token_here
```
> **Never commit your `.env` file.** It is already covered by `.gitignore`.

### Getting a Bot Token
1. Go to the [Discord Developer Portal](https://discord.com/developers/applications)
2. Select your application or create a new one
3. Navigate to the **Bot** tab
4. Click **Reset Token** and copy it into your `.env` file

## Running the Bot
```bash
node index.js
```

## Project Structure
```
discord-bot/
├── index.js        # Entry point
├── .env            # Your local secrets (do not commit)
├── .gitignore
└── package.json
```

## Contributing
Please read the root-level CONTRIBUTING.md and CODE_OF_CONDUCT.md before submitting changes.
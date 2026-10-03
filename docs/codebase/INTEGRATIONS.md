# External Integrations

## Database

EF Core selects PostgreSQL when `DATABASE_URL` exists and otherwise configured SQLite. Startup applies migrations and runs idempotent Pirate Forge bootstrap. Both providers must preserve migration ordering and tenancy constraints.

## SendGrid

SendGrid 9.29.3 sends organization invitations. Required API variables are:

```text
SendGrid__ApiKey
SendGrid__FromEmail
SendGrid__FromName
SendGrid__InvitationBaseUrl
```

No values belong in source control. `InvitationBaseUrl` points to Web `/accept-invitation`. HTTP is allowed only for loopback Development; production/non-loopback requires HTTPS.

Options are validated lazily through `IOptions<SendGridOptions>`, not `ValidateOnStart`. Correct invalid configuration and restart the API to reload environment/configuration and cached options. Invitation state is persisted before delivery; raw tokens are never stored/logged. Non-2xx delivery returns `502`; resend replaces the pending token.

## AI, SignalR, Discord, deployment

Groq is selected when configured; otherwise AI uses Ollama. `/hubs/planning-poker` authorizes project access but keeps process-local state. Discord bot data is not tenant-scoped and native `@discordjs/opus` may not install on Node 25 without Python/build tools. Docker Compose passes SendGrid variables from the host; Render configures values on the API service; Actions does not embed secrets in images.

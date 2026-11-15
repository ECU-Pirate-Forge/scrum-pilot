# External Integrations

## SQL Server 2025

AppHost models the local persistent SQL Server container or external SQL Server
2025 connection and the control database. EF Core owns application data access.
Organization databases are provisioned from server-owned registry keys after
central authorization; clients cannot choose a catalog.

Runtime and administrative connections are separate. Production requires
encrypted connections/certificate validation and protected data/backup storage.
See [operations](../multi-tenancy-operations.md).

## Local inference

Ollama runs on the host for local GPU access. AppHost supplies endpoint/model
configuration; the API registers the OllamaSharp client behind `IChatClient`.
Business story-generation code is provider-neutral, but no additional provider
is registered. Failures are explicit and never trigger a remote fallback.

## SendGrid

Organization invitation delivery uses these private API settings:

```text
SendGrid__ApiKey
SendGrid__FromEmail
SendGrid__FromName
SendGrid__InvitationBaseUrl
```

AppHost supplies them first; direct API startup uses the same keys. The invitation
URL is public; delivery keys are never sent to browsers or baked into images.
Non-loopback links require HTTPS. Tokens are hashed, expire, are revocable and
single-use, and require a confirmed matching email on acceptance.

Configuration/delivery failures are reported explicitly. Do not blindly retry
non-idempotent email sends. Automated doubles do not prove real mail delivery.

## Browser and real-time transport

Both supported clients use organization-qualified REST and the
`/hubs/organizations/{organizationId}/planning-poker` hub. Aspire gateways provide
browser-safe endpoint configuration, service discovery, WebSockets, and telemetry
proxying. Poker state is process-local; use one API process.

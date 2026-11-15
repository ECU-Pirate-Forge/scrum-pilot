# ScrumPilot.ServiceDefaults

Server-side Aspire defaults for discovery, HttpClient instrumentation/resilience,
health checks, and OpenTelemetry. Configure the API through these defaults rather
than registering disconnected health/logging implementations.

Readiness checks the central schema/connectivity. Do not resolve a route-dependent
organization context from global health checks or make one failed tenant/optional
inference dependency disable ordinary work.

Mutation/email/inference retries require explicit idempotency analysis.
Telemetry must exclude credentials, tokens, prompts and private work content.

Standalone browser clients use browser-compatible defaults and gateway proxying;
they must not reference server-only startup services blindly.

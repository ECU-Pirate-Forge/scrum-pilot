# Troubleshooting

## Start with the correct resource graph

Run AppHost from this worktree with isolation and inspect discovered resources,
readiness, structured logs and traces. Do not start a second unmanaged SQL instance
on a guessed port or stop another worktree's resources.

The database engine must be SQL Server 2025 (major version 17), not a provider
substitute. Local SQL data/backups need persistent mounts; an external instance
requires explicit connection and SQL-server-side backup path mapping.

## Control plane is not ready

Check the controlled initialization/migration result, private control connection,
SQL credentials, TLS certificate validation and schema version. Ordinary API
startup must not silently create a database or seed a known account.

Use the [operations runbook](multi-tenancy-operations.md). Do not fix this by
calling `EnsureCreated`, changing a catalog name, or disabling authorization.

## An organization is unavailable

An authorized caller can receive `503` for unavailable/mismatched storage.
Inspect owner-visible operation status and trusted database registry/identity.
Missing or inaccessible work is normally `404`; do not infer a tenant from a
local project/PBI ID. A failed tenant must not make all API readiness fail.

Retry only supported failed/provisioning operations. A restore fence requires
finishing the controlled recovery; migration alone must not clear it.

## A member cannot see a project

Verify current central membership, local project grant, and matching immutable
membership-instance ID. Removing/rejoining intentionally invalidates old grants.
Global Admin is not a work-access bypass. Owners implicitly access local projects.

## A browser still shows the previous organization

Use an explicit `/organizations/{organizationId}/...` URL. Switching must cancel
old requests and rebuild poker scope without saving account defaults. Invalid
default pairs return the user to the central chooser, never a guessed project.

For direct startup, verify public `ApiBaseUrl`. Under Aspire, inspect the gateway's
browser runtime configuration, HTTP/WebSocket routing and telemetry proxy rather
than assuming WASM reads server-process environment variables.

## Local inference fails

Check host Ollama availability and the configured installed model. No model is
automatically downloaded and no remote provider is selected as a fallback.
Cancellation/timeout/schema errors should be visible without leaking prompts or
provider response bodies. Ordinary work remains usable without inference.

## Backup/restore fails

SQL Server opens backup files from its own filesystem. Check both SQL and
operator mount mappings and permissions, backup manifest/hash/schema identity,
administrative credentials, and organization maintenance state. Restored Member
grants must be cleared before activation.

Do not manually clear lifecycle or restore-fence fields to make the UI green.
Keep the organization unavailable until verified recovery completes.

## SQL tests fail before running

Confirm Docker/SQL resources are available and the test runner has its isolated
fixture connection. Tests must fail explicitly, not skip or switch providers.
Never point fixture cleanup at a development or production installation.
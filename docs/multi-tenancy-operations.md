# Multi-Tenancy Operations

## Deployment boundary

This is a fresh SQL Server 2025 installation, not an import of previous data.
Keep existing installations and their backups untouched. Run one API process:
planning-poker state and work-drain coordination remain process-local.

The central control database and every organization database require SQL Server
2025 (major version 17) and compatibility level 170. Application CRUD uses EF
Core; SQL administration is restricted to the controlled operations module.

AppHost is authoritative for the SQL resource, parameters, connections,
initialization ordering, API, browser gateways, existing host Ollama and telemetry.
Use persistent local SQL storage or an explicitly configured external instance.
The local Developer-edition container is for nonproduction use. Production
requires an appropriately licensed SQL Server 2025 edition/instance and an
explicitly reviewed AppHost/publisher configuration; accepting the image EULA
does not grant a production license.
Organization databases are dynamic resources identified by the trusted registry,
not client input or manually enumerated AppHost databases.

## Configuration ownership

| Key | Consumer | Boundary |
|---|---|---|
| `ConnectionStrings:control` | API/control initialization | Private central catalog |
| `ConnectionStrings:tenant-server` | API trusted tenant resolver | Private server template; catalog replaced from registry |
| `ConnectionStrings:tenant-admin` | Controlled SQL operations | Private elevated administrative connection |
| `ConnectionStrings:ollama` | API inference integration | Host endpoint and installed model |
| JWT signing/issuer/audience | API | Private signing material; never a browser setting |
| Invitation email/link settings | API | Private delivery credential; validated public link base |
| Public API endpoint/proxy settings | Browser gateways/clients | Safe public runtime configuration only |

AppHost supplies these first. Direct API startup uses the same keys through
environment variables (`ConnectionStrings__control`, for example), user-secrets
or a protected deployment secret store. A process started outside AppHost does
not inherit another process's resource injection automatically.

Never commit private values or copy them into WebAssembly `wwwroot` settings,
Docker build arguments, screenshots, telemetry, publish bundles or CI artifacts.
Do not print connection strings while troubleshooting. Use separate runtime and
administrative identities in production, trusted TLS certificates and encrypted
storage/backups. Local container convenience is not a production permission model.

## Startup and initialization

Restore/build with the SDK selected by [global.json](../global.json), then start
the exact worktree AppHost in isolated mode:

```powershell
dotnet restore .\ScrumPilot.slnx
dotnet build .\ScrumPilot.slnx --configuration Release
aspire start --non-interactive --isolated --apphost .\ScrumPilot.AppHost\ScrumPilot.AppHost.csproj
aspire describe
```

The controlled initialization/migration resource must complete before API
readiness. First installation also requires deliberate Admin bootstrap; there
are no known seeded accounts. Normal API startup/work cannot create or migrate
missing databases or reseed accounts.

For the first AppHost start, supply private parameters through a protected
environment/secret store:

| Environment key | Meaning |
|---|---|
| `Infrastructure__Initialize` | `true` only for deliberate fresh initialization |
| `Parameters__bootstrap_email` | Initial administrator email |
| `Parameters__bootstrap_password` | Initial administrator password |
| `Parameters__jwt_key` | Private signing key |
| `SqlServer__BackupApplicationPath` | Operator/API-visible protected backup directory |
| `SqlServer__BackupServerPath` | Corresponding SQL-server-visible directory |
| `SqlServer__BackupMountSource` | Optional Docker-engine-visible mount source |

After initialization, set `Infrastructure__Initialize=false` and remove bootstrap
credentials. Subsequent controlled startup uses `--migrate` before API readiness;
it must fail if the control database is missing rather than reinitializing it.

Use the discovered gateway endpoints and wait for the corresponding resource.
Do not assume fixed ports. A missing optional inference service or one failed
organization must not make all ordinary API readiness fail.

For direct startup, supply private configuration to the API explicitly and the
public API endpoint to each client. Run each project independently only after
the same controlled initialization/upgrade has completed.

## SQL permissions and catalog identity

- Runtime control identity: only the central application data permissions needed
  for Identity, memberships and durable operations; no server-level provisioning.
- Runtime work identity: application data access to authorized organization
  catalogs, without general catalog creation/drop/restore privileges.
- Administrative identity: the narrowly scoped provisioning, migration,
  backup/restore and purge permissions required by the operator implementation.
- SQL Server's service identity: access to the configured SQL-side backup mount.
- Operator process identity: access only to its corresponding protected artifact
  root and manifests.

Verify actual permissions using separate identities before production. A
successful local administrator test does not prove runtime least privilege.
Revoke access explicitly when decommissioning an installation.

Tenant catalogs use immutable trusted keys, not organization names. Normal opens
verify registered organization/key metadata and fail closed for absent, wrong
or incompatible storage. Never repair an unavailable organization by changing a
connection to another catalog or manually clearing registry/fence fields.

## Organization lifecycle and authorization

Global Admin creates organizations and performs platform purge, but needs
membership for work. Owners manage membership and implicitly access local projects;
Members need grants tied to the current membership instance. The final Owner
cannot leave, be removed, or be demoted.

Creation durably records provisioning state before external database work.
Authorized management exposes state, operation ID and sanitized failure text.
Retry is allowed only for supported provisioning/failed states and must verify
catalog identity rather than claiming success for an unrelated database.

Soft deletion blocks work and evicts poker participants. Owners can restore
within the retention policy. Permanent deletion is Admin-only and permitted at
the exact 30-day UTC boundary. Catalogs and managed backup/rollback artifacts
must all be verified removed before purge can report success.

Use [API contracts](api.md) for directory, status/retry, membership and lifecycle
routes. An inaccessible organization remains non-disclosing; do not expose its
SQL error details to an unauthorized caller.

## Maintenance and migration

1. Authenticate the operator and resolve the trusted organization.
2. Persist Maintenance before evicting poker or draining work.
3. Drain existing bounded work; do not hold an organization-wide exclusive gate
   during potentially slow inference.
4. Verify database identity and backup/preconditions.
5. Apply the specific context's EF migrations through the controlled operation.
6. Validate schema/identity, complete durable state, then reactivate.

Review both migration histories separately; see
[EF setup](../ScrumPilot.API/EF_SETUP.md). Never run an unqualified migration
against a catalog guessed from a route or organization name.

### Direct operator commands

Supply the private named connections, backup mappings and required credentials
through environment/user-secrets, not command-line values:

```powershell
dotnet run --project .\ScrumPilot.API\ScrumPilot.API.csproj -- --initialize
dotnet run --project .\ScrumPilot.API\ScrumPilot.API.csproj -- --migrate
dotnet run --project .\ScrumPilot.API\ScrumPilot.API.csproj -- --bootstrap-admin
dotnet run --project .\ScrumPilot.API\ScrumPilot.API.csproj -- --create-user
dotnet run --project .\ScrumPilot.API\ScrumPilot.API.csproj -- --backup-control
dotnet run --project .\ScrumPilot.API\ScrumPilot.API.csproj -- --backup-organization 42
dotnet run --project .\ScrumPilot.API\ScrumPilot.API.csproj -- --restore-organization 42 BACKUP_ID
dotnet run --project .\ScrumPilot.API\ScrumPilot.API.csproj -- --retry-organization 42
dotnet run --project .\ScrumPilot.API\ScrumPilot.API.csproj -- --upgrade-organization 42
```

`42` and `BACKUP_ID` are examples: use the approved registry organization ID and
returned backup ID. `--initialize` requires an absent control catalog and
`BootstrapAdmin__Email`/`BootstrapAdmin__Password`; `--bootstrap-admin` uses the
same explicit credentials for an existing initialized control plane.
`--create-user` uses `OperatorUser__Email`/`OperatorUser__Password` and represents
operator-confirmed email verification, not unrestricted public registration.
AppHost parameter names and direct API option names intentionally differ at
this boundary; AppHost maps them to the API's names.

On failure, retain an actionable unavailable state and operation ID. A retry
must resume idempotently; it must not mark the organization Active merely because
a SQL command returned without proving the intended postconditions.

## Backup storage and recovery

SQL Server opens backup files from **its own filesystem**. The API/operator
process's path and SQL Server's backup path can differ, especially on Windows
with Linux containers or with an external server. Configure an explicit protected
mapping, persistent mounts and access for both identities. Do not assume the API
can hand a host path directly to a remote SQL Server.

On Windows/Docker Desktop, use native Linux backup storage: the test helper can
map a restricted WSL ext4 directory with `-WslDistribution`, or accept an explicit
compatible UNC `-BackupDirectory`. A Windows bind mount can fail SQL native backup
file operations even when normal file creation succeeds. The helper does not
silently change Docker Desktop distribution-integration settings.

Protect native SQL backups and their catalog/manifests as sensitive tenant data.
Use backup identity, organization/key, schema, integrity/hash and storage
validation before recovery. Never select files by user-controlled paths or a
broad filename prefix.

### Recover one organization

1. Verify the approved backup belongs to the target organization.
2. Persist the restore fence and Maintenance before destructive SQL work.
3. Drain/evict and perform the controlled SQL recovery.
4. Verify restored organization identity/schema and required migration state.
5. Clear restored Member project grants before activation. Current central
   membership-instance checks remain mandatory as a second boundary.
6. Complete the durable restore fence only after cleanup and validation.
7. Reactivate and verify work, status, poker and access using synthetic probes.

If interrupted after restoring data but before clearing grants, the central fence
must remain durable across process restarts and migration retries. Normal opens
and unrelated migration commands cannot bypass it. Retry the controlled recovery
to completion; do not clear flags manually.

An activation timeout or cancellation can occur after its control-plane commit.
Recovery independently re-establishes Maintenance and the restore fence before
rolling back a catalog that may contain old grants. If that fence cannot be
persisted, rollback is not attempted; investigate the unresolved operation
before retrying rather than manually reopening access.

Test recovery by backing up A, mutating A and B, restoring A and proving B is
unchanged. Remove/rejoin a Member and verify an older backup cannot revive grants.
Exercise failure and process-interruption paths, not just successful restore.

### Recover the control plane

Coordinate the control database, organization registry, catalog identities,
backup catalog and memberships as one recovery set. Recovering only the control
database to an earlier point can resurrect identities/memberships or references
to deleted/replaced databases. Stop ordinary work, reconcile every affected
organization and restore fence, validate explicit defaults/grants, and resume
only after verified authorization. This is not a routine per-organization restore.

## Ollama

The default host endpoint/model is
`Endpoint=http://localhost:11434;Model=gemma4:latest`. Install that model explicitly
or choose an already installed local model in AppHost. No container or model
download is started implicitly. The API uses `IChatClient` behind the domain
story-generation interface, with cancellation, limits, timeout and structured
output validation. There is no remote fallback.

Verify container-to-host networking separately when publishing. Do not use
`localhost` inside a container to mean the host without an explicit mapping.
Inference failure must remain visible and must not prevent unrelated Scrum work.

## Delivery and release checks

CI validates both web suites, SQL Server behavior, Aspire, both EF models,
format/build, three publishes and three Dockerfiles. The manual release workflow
publishes all three images to GHCR with immutable commit tags; it does not deploy
an application or initialize production data.

Deploy only through a reviewed Aspire resource/publisher configuration with
persistent SQL data/backups, private parameters, both browser gateways, TLS and
single-API constraints. Static images alone are not the complete resource graph.

Before release, run [testing and browser acceptance](codebase/TESTING.md), verify
direct-start fallbacks and configuration precedence, inspect public artifacts
for private values, and test restart durability and exact retention/purge behavior.

When stopping validation resources, target the exact owned AppHost/catalogs and
artifacts. Never stop another worktree, enumerate/drop databases by broad prefixes,
or delete persistent volumes as routine cleanup.

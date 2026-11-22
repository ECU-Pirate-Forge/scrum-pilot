# Multi-Tenancy Operations

## Deployment boundary

This architecture does not import former SQLite/PostgreSQL installations or
accounts. Existing registered SQL Server 2025 organizations receive additive
planning upgrades through the controlled maintenance/backup flow below; do not
reinitialize their catalogs. Keep prior installations and backups untouched.
Run one API process:
planning-poker state, live graph subscriptions and work-drain coordination remain
process-local. SQL project locks do not make SignalR delivery multi-replica-safe.

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
| `Features:GitHubIntegration` | API and browser clients | Public flag; defaults to `false`, including repository-dependent task planning |
| `DevTunnels:*` | AppHost only | Local run mode only; never published or exposed to browsers |

AppHost supplies these first. Direct API startup uses the same keys through
environment variables (`ConnectionStrings__control`, for example), user-secrets
or a protected deployment secret store. A process started outside AppHost does
not inherit another process's resource injection automatically.

### Dev tunnels

To share a local run, enable [Dev Tunnels](https://learn.microsoft.com/azure/developer/dev-tunnels/overview)
in the AppHost (`ScrumPilot.AppHost/appsettings.json`, user secrets, or
`DevTunnels__Enabled=true`). Tunnels are disabled by default and are not used by
publish/deploy.

| Key | Default | Effect |
|---|---|---|
| `DevTunnels:Enabled` | `false` | Adds one `devtunnel` resource |
| `DevTunnels:WebApi` / `Web` / `FluentWeb` | `true` | Tunnels the HTTPS endpoint of `webapi`, `web-gateway` or `fluentweb-gateway` |
| `DevTunnels:AllowAnonymous` | `false` | Allows public access; otherwise only the signed-in tunnel owner can open the URLs |
| `DevTunnels:TunnelId` | generated | Reuses a stable tunnel ID across runs |

Aspire installs the `devtunnel` CLI and prompts for login when needed. The
dashboard shows each port's public URL. The FluentWeb tunnel's dashboard link
opens `/fluentweb/` directly. Browsers reach the API through each
gateway's `/{client}/_api/webapi` proxy on the origin opened in the browser,
even if the gateway advertises a localhost address. You only need to tunnel
the API itself for direct external callers. New invitation emails use
`/fluentweb/accept-invitation` on the FluentWeb HTTPS tunnel when enabled,
or the local FluentWeb HTTPS gateway otherwise. API CORS keeps its local
gateway origins. If recipients aren't signed in, FluentWeb opens the sign-in
form and returns them to the invitation afterward. Recipients must have access
to the tunnel and use a confirmed ScrumPilot email matching the invitation.
Prefer authenticated tunnels, and enable anonymous access only for data that
is safe to expose.

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

The batch `--migrate` command reports unavailable organizations individually and
continues verifying the other organizations. It reports verified and unavailable
counts separately; control-plane failures still fail startup. An organization
whose upgrade fails remains fenced in Maintenance, including any pending restore
fence. Repair its backup/storage configuration and retry its explicit
`--upgrade-organization` command, which still exits unsuccessfully on failure.
Do not manually clear maintenance or recovery flags to enable sign-in.

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

New tenant catalogs use `scrumpilot_org_<slug>`, where the slug lowercases ASCII
letters and digits, replaces runs of other characters with `_`, and trims
leading/trailing separators. Empty slugs, names exceeding SQL Server's
128-character catalog limit, and reserved or existing catalog names are rejected.
The registry persists the catalog name: changing an organization's display name
does not rename its database. The separate immutable GUID storage key still
identifies tenant metadata, locks and backup artifacts. Normal opens verify that
identity and fail closed for absent, wrong or incompatible storage.

Existing organizations continue using their GUID-named catalogs until explicitly
converted. Before conversion, confirm the exact SQL instance, control database,
organization IDs, source and destination catalog names, backups, and backup
mount. Run the control migration first; stop normal traffic and keep the API
process stopped while using the operator command for each active organization:

```powershell
dotnet run --project .\ScrumPilot.API\ScrumPilot.API.csproj -- --rename-organization-catalog <organization-id>
```

Provide the same private connection and backup configuration used by the
installation. The command reserves the destination, fences work, verifies
identity, takes a backup, renames the database, and activates the mapping. It
refuses collisions and does not drop another database. On interruption, leave
the durable fence in place, inspect the operation error and retry the same
command; do not clear registry or recovery fields manually. If both catalogs
exist or neither exists, operator investigation is required. Deleted or
otherwise unavailable organizations require their normal lifecycle recovery
before conversion. Existing backups retain their GUID-keyed artifact names.

## Organization lifecycle and authorization

Global Admin creates organizations and performs platform purge, but needs
membership for work. Owners manage membership and implicitly access local projects;
Members need grants tied to the current membership instance. In FluentWeb,
an organization Owner can open **Manage Organization**, find the member by
username under **<project name> access**, and select **Grant access**. Accepting
an invitation joins the organization but does not grant project access.
If email delivery is blocked, an Owner can use **Manage Organization > Members >
Pending invitations > Copy invite link** in FluentWeb. Creating the link replaces
the previous link without sending email. Share it privately with the intended
recipient, who must still sign in with the invited email address. The link uses
the configured `SendGrid:InvitationBaseUrl` and expires after 72 hours.
The final Owner
cannot leave, be removed, or be demoted.

Creation durably records provisioning state before external database work.
Authorized management exposes state, operation ID and sanitized failure text.
Retry is allowed only for supported provisioning/failed states and must verify
catalog identity rather than claiming success for an unrelated database.

Soft deletion blocks work and evicts poker and project-work participants. Owners can restore
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

SQL error 3634 with OS error 31 during `DiskChangeFileSize` is one such failure.
It can surface as `TenantUnavailableException` and process exit `-532462766`
during a controlled upgrade. Inspect the SQL resource's logs and use the explicit
native Linux backup mapping rather than disabling rollback backups.

For normal local Windows startup, prepare the mapping for the **intended
worktree**, using an installed WSL distribution with `setfacl` available:

```powershell
$mapping = & .\ScrumPilot.AppHost\scripts\Initialize-WslBackupStorage.ps1 -WslDistribution Ubuntu
$env:SqlServer__BackupApplicationPath = $mapping.ApplicationPath
$env:SqlServer__BackupMountSource = $mapping.MountSource
$env:SqlServer__BackupServerPath = '/var/opt/mssql/backups'
aspire start --non-interactive --isolated --apphost .\ScrumPilot.AppHost\ScrumPilot.AppHost.csproj
```

The helper returns paths only; it does not start services, change credentials,
modify Docker Desktop integration, copy existing backups, or clear maintenance.
It prepares a restricted native directory scoped to the worktree and maps the
same files to the Windows API host and Linux SQL container. Test fixtures reuse
it with a separate storage name. Repeat preparation after WSL restarts, and
supply both mapping keys on every AppHost start (or persist them in protected
local configuration).

AppHost rejects local Windows drive backup mounts before starting resources,
including Docker Desktop's `/run/desktop/mnt/host/c/...` equivalent. External SQL
and published deployments retain their operator-reviewed mapping configuration.
Changing mappings does not move old backups: retain the original directory and
transfer registered artifacts only through an approved recovery procedure.

For an already running installation, stop and restart **only its exact AppHost**
in a coordinated maintenance window with the repaired mapping. Do not start a
second API against the same catalogs. The startup migration retries upgrades;
confirm the affected organization's status becomes Active. If necessary, run
`--upgrade-organization` for its trusted registry ID with the same private
connections and mapping. Never use `--initialize` or manually clear its fence.

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

Work activation does not reactivate GitHub scope authority. The post-drain hook
suspends matching central repository registrations and invalidates their known
heads before replacing storage. An Owner must reconcile each repository and
revalidate its PRs after recovery; old tenant Passed rows cannot authorize a
suspended or newer configuration. See the recovery procedure below.

If interrupted after restoring data but before clearing grants, the central fence
must remain durable across process restarts and migration retries. Normal opens
and unrelated migration commands cannot bypass it. Retry the controlled recovery
to completion; do not clear flags manually.

An activation timeout or cancellation can occur after its control-plane commit.
Recovery independently re-establishes Maintenance and the restore fence before
rolling back a catalog that may contain old grants. If that fence cannot be
persisted, rollback is not attempted; investigate the unresolved operation
before retrying rather than manually reopening access.

### Recover GitHub planning authority

The control database owns global repository registrations and the append-only
candidate-head journal. Tenant catalogs own detailed checks, bindings, snapshots
and specifications. Back up both ownership boundaries; never copy central
authority into a tenant backup or remove a journal head because it has no local
result row.

1. Freeze merges on affected protected branches before planned maintenance or
   whenever external-check invalidation is uncertain. SQL fencing alone cannot
   revoke an already green GitHub check during a provider outage.
2. Persist the storage fence and drain active tenant work through the supported
   operation. Post-drain observers fence registrations and revoke known heads;
   never publish invalidation before the drain while an old writer can turn a
   check green again.
3. Inspect the repository's Suspended/Retiring/Retired state and recovery error.
   Storage recovery and GitHub recovery are independent outcomes: Active storage
   does not mean checks are reconciled. Keep merges frozen on any unconfirmed
   publication; repair App access and retry reconciliation.
4. After restore/upgrade or organization restoration, an Owner saves the reviewed
   matching repository configuration. That flow invalidates central known heads
   before activation. Recover a partial Pending reservation through that same
   flow, not by manually editing registry IDs/version/state.
5. Refresh repository snapshots and reapprove changed specifications when
   required. Revalidate each PR's current distinct head and full diff, then
   confirm the required App-sourced branch check before lifting the freeze.

Project/organization purge retains central registration tombstones and journal
entries even after the catalog disappears. These records intentionally cannot
be updated/deleted as a way to forget recovery obligations or transfer policy
authority to another project. Retry incomplete retirement/invalidation using the
supported operation. A GitHub invalidation failure prevents purge completion:
storage stays Purging and retryable until external retirement is confirmed.
Retention/reassignment requires a separately reviewed
operator process, not ad hoc SQL.

A control-database point-in-time restore can lose journal heads published after
that backup. Freeze merges installation-wide, inventory/revoke all potentially
affected checks directly in GitHub and reconcile central authority before
resuming. The journal's tenant-restore guarantee does not cover rolling back the
journal itself.

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

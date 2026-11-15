# ScrumPilot.Data

EF Core 10 persistence layer for ScrumPilot, with SQLite for local development and PostgreSQL for production.

## Planning Schema and Retention

Planning extends existing PBIs additively rather than replacing backlog data:

- `ParentStoryId` separates child-task containment from dependency edges; source
  problem, generation run/candidate, and portable version fields retain identity.
- `ProjectGuidelinesRevisions`, `GenerationInputs`, `RepositorySnapshots`,
  `TaskSpecificationRevisions`, and `TaskAllowedFiles` preserve immutable input,
  SHA/tree metadata, content hashes, approvals, and exact file operations.
- `ProblemStatements` and `GenerationRuns` retain saved sources and execution state.
- `PbiDependencies` stores multiple prerequisites. `ProjectGraphStates` and
  dependency review records preserve revisioned readiness/acceptance state.
- Repository bindings, PR scope results, non-task exceptions, and webhook
  delivery identities support audited enforcement.
- `PullRequestScopeAttempts` retains repository/PR/head recovery entries
  independently of completed scope-check results.

Project-scoped composite keys/foreign keys prevent cross-project references.
Each project links one repository, and each stable GitHub repository ID links
exclusively to one project; multiple installations do not relax that constraint.
History-backed repository replacement is restricted.

`AddPersistedPlanningFoundations` adds tables/columns and backfills normalized
edges from valid same-project, non-self `DependsOnPbiId` references. The legacy
field remains for compatibility; it cannot represent all prerequisites.
Missing/invalid legacy references and effective cycles need explicit repair;
do not treat migration success as proof every historical graph is valid.
The API reads legacy references too, so omitted invalid backfill edges must not
silently become satisfied work.

Apply the complete planning migration chain in order:

| Migration | Purpose |
|---|---|
| `20261115014516_AddPersistedPlanningFoundations` | Additive planning schema and legacy dependency backfill |
| `20261115020109_CompletePlanningScope` | Scope/audit constraints and authorized purge guards |
| `20261115023000_AddPlanningArtifactApplicabilityReviews` | Applicability-review history with append-only/purge triggers |
| `20261115023704_JournalScopeValidationHeads` | Durable `PullRequestScopeAttempts` repository/PR/head recovery journal |

Planning policy operations use a shared database-backed project lock inside an
enclosing transaction. Scope validation serializes against specification,
guideline, problem, and work-item input mutations across API instances sharing
the database. This lock does not make GitHub API calls/merges transactional.
Scope validation first commits a journaled candidate head after invalidating all
known attempt/legacy-check heads and publishing failure. A second locked phase
rechecks the same head/current policy before success. If the final result save
rolls back after GitHub publication, retries/input mutations can invalidate the
separately committed head even during a PR metadata outage. Keep the journal;
it is recovery evidence, not proof of a passed check or atomic merge isolation.

Immutable history is enforced in persistence and provider-specific database
guards. Ordinary work-item edits/deletions cannot erase referenced provenance;
repair relationships or archive work rather than manually deleting history.
Authorized project deletion and retention-controlled organization purge (global
Admin, after 30 days deleted) use an enclosing transaction. The cleanup inserts a
project-scoped flag in `PlanningPurgeScopes`, removes that project's planning
history in dependency order, then removes the flag. DELETE triggers allow only
the scoped exception; unrelated project history remains protected. Updates are
still rejected. This internal flag is not an API permission or a general-purpose
history editing mechanism.

All four planning migrations passed actual **PostgreSQL 17-alpine up/down/up**;
append-only and authorized-purge guards were exercised, including applicability
reviews and the recovery journal. SQLite **up/down/up and scoped purge** also
passed, and the final EF model check reported no pending changes. Repeat these
checks against the exact deployment revision and a copy of deployment data.

### Rollout precautions

1. Back up and test restoring the existing database before upgrading.
2. Review generated SQL for **both PostgreSQL and SQLite**, including append-only
   guards, legacy-edge backfill, exclusive repository uniqueness, authorized purge,
   and journal retention/recovery.
3. Inventory invalid/self/cross-project references, cycles, and duplicate repository
   assignments. Resolve them explicitly; do not broaden permissions or drop constraints.
4. Run relational/migration tests and the pending-model check below against the
   exact release revision. Exercise up/down/up and guards for all four migrations
   on both providers, including journal recovery after a failed final result save.
   Test the deployment upgrade against a copy of existing data.
5. Remember the API applies migrations at startup. Coordinate restart/rollout so
   old and new clients cannot silently replace multiple dependencies.

Additive does not mean rollback is lossless: dropping planning tables destroys
provenance and recovery evidence, so prefer a
reviewed forward repair and retain backups.

## Tenancy model

| Entity | Purpose |
|---|---|
| `Organization` | Tenant identity, unique normalized name, creation/deletion timestamps, concurrency token |
| `OrganizationMembership` | User membership with organization-scoped `Owner` or `Member` role |
| `Project` | Belongs to exactly one organization through required `OrganizationId` |
| `ProjectMembership` | Explicit project access for ordinary organization members |
| `OrganizationInvitation` | Email-bound invitation state, hashed token, expiry, delivery audit |
| `ApplicationUser` | Identity user with optional default organization/project preferences |

Organization owners have implicit access to every project in their organization. Members require a `ProjectMembership`.

## Pirate Forge migration and bootstrap

`20261003024314_AddOrganizationTenancy` performs compatibility-safe ordering:

1. Adds nullable project organization and user default-organization columns.
2. Creates `Pirate Forge`.
3. Backfills every existing project and user default organization.
4. Creates memberships: global Admin users become Owners; all others become Members.
5. Validates project backfill, then makes `Project.OrganizationId` required.
6. Adds foreign keys and indexes.
7. Creates explicit project memberships for migrated non-owner members.
8. Creates invitation storage and token indexes.

Startup creates missing development seed users, then runs `PirateForgeBootstrapper` in a serializable transaction (plus a PostgreSQL advisory lock). Only seed users newly created in that invocation receive initial Pirate Forge membership and project access, so manual membership or grant revocations remain durable across restarts. Existing member Admins are promoted to Owner when needed; an Admin without membership is not re-added. The bootstrap retries classified transient conflicts and validates the global-Admin owner invariant.

## Lifecycle behavior

- Organization names remain reserved while soft-deleted.
- Soft delete records `DeletedAt`, removes active access, and clears affected defaults.
- A historical owner can restore within 30 days.
- Only a global Admin can purge, and only after 30 days.
- The final Owner cannot be demoted, removed, or leave.
- Removing a member deletes that user's project memberships and clears invalid defaults.

## Provider selection

- `DATABASE_URL` present: PostgreSQL through Npgsql.
- Otherwise: `ConnectionStrings:DefaultConnection` with SQLite.

`AddOrganizationTenancy` is generated for Npgsql and includes explicit provider branches for PostgreSQL and SQLite. Relational migration tests exercise the SQLite path; production migration SQL is generated with Npgsql.

## Migration commands

```powershell
$env:DATABASE_URL = 'postgresql://unused:unused@localhost:5432/scrumpilot_design'
dotnet ef migrations add <MigrationName> --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
dotnet ef database update --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
dotnet ef migrations has-pending-model-changes --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
dotnet ef migrations script --idempotent --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj --output .\artifacts\planning.sql
```

The design-time factory requires a PostgreSQL URI. The pending-model and script commands parse the shown non-secret URI to select Npgsql without connecting; use a real deployment connection only for database-access commands. CI pins `dotnet-ef` to 10.0.5; check package/tool compatibility for the release revision.

Expected outcomes: no pending model changes and reviewed SQL for the intended
provider; do not run `database update` using the illustrative connection. For
focused relational coverage:

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~Planning|FullyQualifiedName~Dependency|FullyQualifiedName~Migration|FullyQualifiedName~PullRequestScope"
```

Expected: passing test summary. This is a recommended command, not a final
validation result. See [shared contracts](../ScrumPilot.Shared/README.md) and
[architecture](../docs/architecture.md).

# Architecture

## Current system

```text
Blazor WebAssembly -> authenticated HTTP/SignalR -> ASP.NET Core controllers/hub
                   -> application services + authorization -> tenant-aware repositories
                   -> EF Core -> SQLite (development) / PostgreSQL (production)
```

The Web client owns navigation state; the API owns authorization. `OrganizationStateService` and `ProjectStateService` never establish access by themselves.

## Tenancy boundaries

- Global Identity `Admin` is used for organization creation, initial-owner search, and expired soft-delete purge.
- `OrganizationMembership` stores organization `Owner`/`Member` roles.
- `ProjectMembership` stores access/no-access for ordinary members; Owners have implicit access to all organization projects.
- Every project has a required organization. Child-resource authorization resolves through persisted project relationships.
- Cross-tenant inaccessible resources are hidden with `404` where appropriate; owner-only operations distinguish an active non-owner with `403`.
- REST endpoints, metrics, preferences, assignment lists, comments, and SignalR use the persisted access model.

## Workflows

Global Admin creates an organization and identifies its first Owner. Owners rename, manage invitations/members/projects, and soft-delete. The final Owner cannot be demoted, removed, or leave. A historical Owner can restore for 30 days; after that only global Admin can purge.

Invitation state is persisted before SendGrid. Tokens are random, stored as hashes, valid for 72 hours, single-use, revocable, and accepted only by an authenticated confirmed matching email. Acceptance returns the organization ID for client refresh.

`AddOrganizationTenancy` creates Pirate Forge, backfills projects before making organization ownership required, maps global Admin users to Owners, and gives migrated ordinary users explicit access to migrated projects. Startup runs guarded idempotent bootstrap and refuses to continue if Pirate Forge has no global-Admin Owner.

## Layers

| Layer | Responsibility |
|---|---|
| Shared | Serializable entities, enums, requests, responses |
| Data | Relationships, provider-compatible migration, scoped queries, atomic mutations |
| API | Identity, authorization, business policy, status mapping, integrations |
| Web | Navigation, management UI, defaults, error presentation |
| Tests | Unit, relational, isolation, migration, and bUnit verification |

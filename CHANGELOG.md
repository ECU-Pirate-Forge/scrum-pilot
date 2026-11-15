# Changelog

## [Unreleased]

### Added
- Project Markdown guidelines/history and the canonical 40-section template.
- Persisted problem statements, immutable generation inputs and accepted-story
  lineage; reviewed child-task proposals and exact AI implementation/test/docs scope.
- Scoped task approvals, Markdown/JSON handoffs, normalized sprint dependencies,
  live Mermaid readiness/completion/reopening and audited acceptance reviews.
- Organization-qualified planning APIs and live graph connections using current
  membership and immutable client work scopes.
- Central GitHub repository authority and candidate-head recovery journal,
  including post-drain maintenance/restore reconciliation.
- Multi-organization accounts with a central control database and SQL Server 2025
  database per organization, Owner/Member roles, and current-membership project grants.
- Organization-qualified work and poker, tab-local switching, explicit defaults,
  invitations, lifecycle status/retry, controlled backup/recovery and 30-day purge.
- Shared client scope/transport and organization workflows in both supported web clients.
- Aspire-first SQL/configuration/gateway/telemetry orchestration and direct-start fallbacks.

### Changed
- Ported guided planning onto concrete SQL Server control/organization contexts;
  obsolete SQLite/PostgreSQL planning migrations are not runnable history.
- Shared local Ollama integration releases tenant storage leases during inference.
- Both Mermaid helpers preserve safe rendering and DOM-ownership race guards.
- Reconciled tenancy with main's FluentWeb, Aspire and package updates.
- Application persistence uses EF Core SQL Server with two migration histories.
- Local story generation uses Ollama through a replaceable service boundary.
- Updated setup, API, architecture, operations, testing and delivery documentation.

### Removed
- Retired chat automation, transcript-only data and nonlocal inference integrations.
- Implicit database creation/migration and known-account seeding during ordinary startup.

## [1.0.0] - YYYY-MM-DD
### Added
- Initial release
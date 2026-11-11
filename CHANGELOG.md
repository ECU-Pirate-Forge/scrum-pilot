# Changelog

## [Unreleased]

### Added
- Multi-organization accounts with a central control database and SQL Server 2025
  database per organization, Owner/Member roles, and current-membership project grants.
- Organization-qualified work and poker, tab-local switching, explicit defaults,
  invitations, lifecycle status/retry, controlled backup/recovery and 30-day purge.
- Shared client scope/transport and organization workflows in both supported web clients.
- Aspire-first SQL/configuration/gateway/telemetry orchestration and direct-start fallbacks.

### Changed
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
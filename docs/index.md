# ScrumPilot Documentation

## Start here

- [Project setup and supported clients](../README.md)
- [Contribution and verification](../CONTRIBUTING.md)
- [Architecture and isolation](architecture.md)
- [API authorization and routes](api.md)
- [SQL Server operations and recovery](multi-tenancy-operations.md)
- [Troubleshooting](troubleshooting.md)

## Codebase reference

- [Stack](codebase/STACK.md)
- [Structure](codebase/STRUCTURE.md)
- [Architecture](codebase/ARCHITECTURE.md)
- [Integrations and configuration boundaries](codebase/INTEGRATIONS.md)
- [Conventions](codebase/CONVENTIONS.md)
- [Testing and acceptance](codebase/TESTING.md)
- [Operational concerns](codebase/CONCERNS.md)
- [Independent EF migration histories](../ScrumPilot.API/EF_SETUP.md)

Plans under `superpowers/plans` are historical design records, not current
installation instructions. The current release uses SQL Server 2025, Aspire-first
configuration, a central control database and database per organization, and
supports both Web and FluentWeb.
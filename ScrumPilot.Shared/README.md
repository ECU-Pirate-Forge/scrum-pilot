# ScrumPilot.Shared

Serializable domain entities, enums and API contracts shared by the API, both
web clients, and tests. UI transport belongs in Client; authorization and
provider integrations do not belong here.

## Contracts

- Organizations, memberships/roles, invitations, lifecycle/provisioning summaries.
- Projects, PBIs, sprints, epics, comments, histories and dependencies.
- Explicit project membership and membership-instance references.
- Profile/theme and validated default organization/project pair.
- Metrics, dashboard/widget preferences, assignment and historical-author summaries.
- Authentication/password and structured AI story contracts.

Organization/project identifiers are scoped references, not authorization.
Local project/PBI IDs can repeat in different databases. Server code validates
related IDs and current memberships; clients cannot choose storage.

Preserve surviving public enum numeric values and nullable contract semantics
when removing a feature. Historical display names must not become mutable
ownership identifiers. Never add private connection or signing configuration
to a browser-visible DTO.

See [architecture](../docs/architecture.md) and [API contracts](../docs/api.md).

# ScrumPilot.Client

UI-neutral organization route and request-lifetime primitives used by both
supported browser clients. This library has no visual, server configuration,
SQL, or AI provider dependencies.

- `OrganizationRoutes` builds explicit organization-qualified URLs.
- `WorkScope` captures one organization/project view lifetime.
- `WorkHttpClient` captures the scope for requests, links cancellation, and
  rejects late results even when a handler ignores cancellation.

Do not mutate a global HttpClient base address to select tenants. Keep themes,
components, chart adapters and thin Blazor boundary components in each UI.
The API still enforces every permission.

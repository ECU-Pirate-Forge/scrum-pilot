# Coding Conventions

## C# and Razor

- PascalCase files/types/methods; interfaces begin with `I`; async methods end in `Async`.
- Nullable reference types and implicit usings are enabled.
- Prefer mutation DTOs over persistence entities.
- Resolve authorization from `ICurrentUser` and persisted relationships, never from client state or organization claims cached in JWTs.
- Derive project/organization scope through the resource hierarchy.
- Never log or commit raw invitation tokens, JWTs, passwords, API keys, or connection credentials.
- Use EF Core SQL Server for CRUD; isolate database administration from request repositories.
- Treat explicit organization routes as authoritative. Capture cancellable work scope rather than mutating global HTTP state.
- Keep shared transport UI-neutral and preserve both clients' supported workflows.
- Define orchestration configuration in AppHost first, with matching project direct-start fallbacks.
- All block statements must be enclosed in { } even if there is only one statement.

## Errors

Controllers map typed exceptions: inaccessible tenant resources normally use `404`, an active member attempting owner work uses `403`, validation uses `400`, lifecycle/concurrency uses `409`, and delivery failure uses `502`. Avoid broad catches and success-shaped fallbacks.

## Formatting

```powershell
dotnet format .\ScrumPilot.slnx --verify-no-changes
```

If it fails, run `dotnet format .\ScrumPilot.slnx`, inspect relevant changes, and verify again.

## Tests

Use NSubstitute for isolated dependencies, bUnit for UI, and real SQL Server 2025 for tenancy/migration guarantees. Access changes require authorized and inaccessible cases. Verify both web suites and the Aspire graph, not only the backend.

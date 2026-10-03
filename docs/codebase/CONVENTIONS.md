# Coding Conventions

## C# and Razor

- PascalCase files/types/methods; interfaces begin with `I`; async methods end in `Async`.
- Nullable reference types and implicit usings are enabled.
- Prefer mutation DTOs over persistence entities.
- Resolve authorization from `ICurrentUser` and persisted relationships, never from client state or organization claims cached in JWTs.
- Derive project/organization scope through the resource hierarchy.
- Never log or commit raw invitation tokens, JWTs, passwords, API keys, or connection credentials.

## Errors

Controllers map typed exceptions: inaccessible tenant resources normally use `404`, an active member attempting owner work uses `403`, validation uses `400`, lifecycle/concurrency uses `409`, and delivery failure uses `502`. Avoid broad catches and success-shaped fallbacks.

## Formatting

```powershell
dotnet format .\ScrumPilot.slnx --verify-no-changes
```

If it fails, run `dotnet format .\ScrumPilot.slnx`, inspect relevant changes, and verify again.

## Tests

Use NSubstitute for isolated dependencies, bUnit for UI, and real relational repositories for tenancy/migration guarantees. Access changes require authorized and inaccessible cases.

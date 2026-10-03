# Contributing to ScrumPilot

Thank you for contributing to ScrumPilot. Contributions of code, tests, documentation, bug reports, and design feedback are welcome. This guide assumes no previous open-source contribution experience and walks through the complete local workflow.

By participating, you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Before You Start

ScrumPilot is a .NET 10 solution composed of:

- `ScrumPilot.Web`: Blazor WebAssembly client
- `ScrumPilot.API`: ASP.NET Core REST API and SignalR hub
- `ScrumPilot.Data`: Entity Framework Core data access and migrations
- `ScrumPilot.Shared`: shared models and API contracts
- `ScrumPilot.AppHost`: Aspire local orchestration
- `ScrumPilot.UnitTests`: xUnit and bUnit tests

The standard local setup uses SQLite and Ollama. PostgreSQL, Groq, and SendGrid are optional and are not required for ordinary development.

## 1. Install Prerequisites

Install the following tools:

| Tool | Required version | Check command |
|---|---|---|
| Git | Current supported release | `git --version` |
| .NET SDK | `10.0.401` or a compatible .NET 10 SDK | `dotnet --version` |
| Aspire CLI | Compatible with Aspire AppHost `13.6.0` | `aspire --version` |
| Ollama | Current supported release | `ollama --version` |

The required .NET SDK is declared in [`global.json`](global.json). Download it from the [.NET download page](https://dotnet.microsoft.com/download/dotnet/10.0) if it is not installed.

### Install the Aspire CLI

The .NET global tool works on Windows, macOS, and Linux:

```shell
dotnet tool install --global Aspire.Cli
```

If it is already installed, update it:

```shell
dotnet tool update --global Aspire.Cli
```

Close and reopen the terminal if `aspire` is not found after installation.

### Install Ollama

#### Windows PowerShell

```powershell
winget install --exact --id Ollama.Ollama
```

#### macOS

```bash
brew install ollama
```

#### Linux

```bash
curl -fsSL https://ollama.com/install.sh | sh
```

See the [Ollama download page](https://ollama.com/download) when a package manager is unavailable.

## 2. Fork and Clone the Repository

1. Open the [ScrumPilot repository](https://github.com/ECU-Pirate-Forge/scrum-pilot).
2. Select **Fork** and create the fork under your GitHub account.
3. Replace `YOUR-USERNAME` in the commands below with your GitHub username.

### Windows PowerShell

```powershell
git clone https://github.com/YOUR-USERNAME/scrum-pilot.git
Set-Location .\scrum-pilot
git remote add upstream https://github.com/ECU-Pirate-Forge/scrum-pilot.git
git remote -v
```

### macOS or Linux

```bash
git clone https://github.com/YOUR-USERNAME/scrum-pilot.git
cd scrum-pilot
git remote add upstream https://github.com/ECU-Pirate-Forge/scrum-pilot.git
git remote -v
```

`origin` should point to your fork. `upstream` should point to the Pirate Forge repository.

## 3. Restore and Build the Solution

Run these commands from the repository root on any platform:

```shell
dotnet restore ./ScrumPilot.slnx
dotnet build ./ScrumPilot.slnx
```

A successful build confirms that the SDK and project dependencies are available. Package vulnerability warnings may appear; do not silently change dependency versions in an unrelated contribution.

## 4. Configure Ollama for Local Inference

The API uses Ollama when `GroqApiKey` is absent or empty. Its committed local configuration expects:

- Ollama URL: `http://localhost:11434/`
- Model: `gemma4:latest`

### Start Ollama

The Ollama desktop application normally starts its service automatically on Windows and macOS. If it is not running, open a separate terminal and run:

```shell
ollama serve
```

On a Linux installation managed by systemd, use:

```bash
sudo systemctl enable --now ollama
sudo systemctl status ollama
```

Leave `ollama serve` running when it was started manually.

### Download and Verify the Model

Run on any platform:

```shell
ollama pull gemma4:latest
ollama list
ollama run gemma4:latest "Reply with READY only."
```

The first pull downloads several gigabytes and can take time. The final command should produce a response from the model.

If your machine cannot run `gemma4:latest`, pull a smaller model and override the API setting before startup.

#### Windows PowerShell

```powershell
ollama pull llama3.2:latest
$env:OllamaModel = "llama3.2:latest"
Remove-Item Env:GroqApiKey -ErrorAction SilentlyContinue
```

#### macOS or Linux

```bash
ollama pull llama3.2:latest
export OllamaModel="llama3.2:latest"
unset GroqApiKey
```

Environment-variable overrides apply only to processes started from the same terminal. Never commit API keys or other secrets.

## 5. Run ScrumPilot with Aspire

Trust the local ASP.NET Core development certificate before the first HTTPS run.

### Windows PowerShell or macOS

```shell
dotnet dev-certs https --trust
```

Linux trust configuration varies by distribution. You can create the certificate with `dotnet dev-certs https` and then follow the [.NET HTTPS certificate guidance](https://learn.microsoft.com/aspnet/core/security/enforcing-ssl#trust-the-aspnet-core-https-development-certificate) for your browser and distribution.

From the repository root, start the API and Web client through the AppHost:

```shell
aspire run --apphost ./ScrumPilot.AppHost/ScrumPilot.AppHost.csproj
```

Aspire prints a dashboard URL. Open it to view resource status, logs, traces, and the current HTTP/HTTPS endpoints. Wait until both `webapi` and `web` report that they are running, then open the Web endpoint.

The default launch profiles normally expose:

- Web: `https://localhost:7280` or `http://localhost:5199`
- API: `https://localhost:7195` or `http://localhost:5219`
- Swagger: `https://localhost:7195/swagger`

The Aspire dashboard is authoritative if a port differs. Press `Ctrl+C` in the Aspire terminal to stop the application.

### Development Accounts

Local startup applies EF Core migrations, creates the SQLite database when needed, and seeds development users. These credentials are for local development only:

| Username | Password | Role |
|---|---|---|
| `Brian` | `Password1234!` | Admin |
| `James` | `Password1234!` | Developer |

Do not reuse these passwords in another environment.

## 6. Run Without Aspire

Aspire is the recommended workflow because it starts both projects and provides centralized diagnostics. To run each project manually, use two terminals from the repository root.

Terminal 1, API:

```shell
dotnet run --project ./ScrumPilot.API/ScrumPilot.API.csproj --launch-profile https
```

Terminal 2, Web:

```shell
dotnet run --project ./ScrumPilot.Web/ScrumPilot.Web.csproj --launch-profile https
```

The Web client reads its API base URL from `ScrumPilot.Web/wwwroot/appsettings.json`, which defaults to `https://localhost:7195/`.

## 7. Create a Branch

Do not work directly on the repository's default branch. First synchronize your local branch with `upstream`, then create a focused branch.

```shell
git fetch upstream
git switch main
git pull --ff-only upstream main
git switch -c feat/short-description
```

Use a descriptive branch prefix:

| Change | Example branch |
|---|---|
| Feature | `feat: sprint-capacity` |
| Bug fix | `fix: pbi-json-parsing` |
| Documentation | `docs: local-setup` |
| Refactoring | `refactor: project-service` |
| Tests | `test: organization-access` |

Keep each branch limited to one logical change. Open an issue or discussion before starting a large feature or architectural change so maintainers can confirm the direction.

## 8. Make and Test Changes

Follow the existing project boundaries and naming style:

- Put shared API contracts and enums in `ScrumPilot.Shared`.
- Keep authorization and business rules on the API, not only in the Web client.
- Add EF Core migrations for persisted model changes.
- Prefer small, single-purpose methods and components.
- Add or update focused tests for changed behavior.
- Update documentation when commands, configuration, or behavior changes.
- Do not include generated `bin` or `obj` output, local databases, model files, credentials, or editor-specific files.

### Run a Focused Test

During development, filter to the affected class or behavior:

```shell
dotnet test ./ScrumPilot.UnitTests/ScrumPilot.UnitTests.csproj --filter "FullyQualifiedName~StoryServiceTests"
```

Replace `StoryServiceTests` with the relevant class or namespace.

### Run the Required Verification

Stop the running Aspire AppHost before building to avoid locked assembly files. Then run:

```shell
dotnet format ./ScrumPilot.slnx --verify-no-changes
dotnet test ./ScrumPilot.UnitTests/ScrumPilot.UnitTests.csproj --configuration Release
dotnet build ./ScrumPilot.slnx --configuration Release
```

If formatting verification fails, apply the repository format and review the resulting diff:

```shell
dotnet format ./ScrumPilot.slnx
git diff
```

For EF Core model changes, also check that a migration represents the current model.

#### Windows PowerShell

```powershell
$env:DATABASE_URL = "postgresql://unused:unused@localhost:5432/scrumpilot_design"
dotnet ef migrations has-pending-model-changes --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
Remove-Item Env:DATABASE_URL
```

#### macOS or Linux

```bash
DATABASE_URL="postgresql://unused:unused@localhost:5432/scrumpilot_design" \
	dotnet ef migrations has-pending-model-changes \
	--project ./ScrumPilot.Data/ScrumPilot.Data.csproj \
	--startup-project ./ScrumPilot.API/ScrumPilot.API.csproj
```

The placeholder URI selects the PostgreSQL design-time provider; this command does not connect to that database.

## 9. Review Your Work

Before committing, inspect exactly what changed:

```shell
git status --short
git diff
git diff --check
```

Look for accidental generated files, secrets, unrelated formatting, debugging statements, and incomplete changes. Stage only files that belong to the contribution:

```shell
git add path/to/changed-file path/to/test-file
git diff --staged
```

Avoid `git add .` until you are comfortable reviewing everything it stages.

## 10. Use Conventional Commits

Commit messages must follow [Conventional Commits](https://www.conventionalcommits.org/):

```text
type(optional-scope): short imperative description
```

Use a lowercase type, an optional project-oriented scope, and a concise description without a trailing period.

### Common Types

| Type | Use for | Example |
|---|---|---|
| `feat` | User-visible functionality | `feat(web): add sprint capacity editor` |
| `fix` | Bug correction | `fix(api): validate generated PBI schema` |
| `docs` | Documentation only | `docs: expand local Ollama setup` |
| `test` | Tests only | `test(data): cover organization bootstrap` |
| `refactor` | Internal change without new behavior | `refactor(api): extract invitation validation` |
| `perf` | Performance improvement | `perf(data): reduce tracked dashboard queries` |
| `build` | Build system or dependencies | `build: update Aspire AppHost package` |
| `ci` | Continuous integration | `ci: run release build on pull requests` |
| `chore` | Maintenance not covered above | `chore: remove obsolete sample file` |
| `revert` | Revert an earlier commit | `revert: feat(web): add sprint capacity editor` |

Useful scopes include `api`, `web`, `data`, `shared`, `tests`, and `apphost`. Omit the scope when a change spans the repository.

### Commit Examples

One-line fix:

```shell
git commit -m "fix(web): handle empty PBI generation response"
```

Documentation change:

```shell
git commit -m "docs: clarify local Ollama setup"
```

Commit with a body explaining motivation:

```shell
git commit -m "refactor(api): centralize project authorization" -m "Reuse persisted project access checks across REST endpoints and the planning poker hub."
```

For an intentionally breaking change, add `!` and explain the impact in a footer:

```shell
git commit -m "feat(api)!: replace legacy project route" -m "BREAKING CHANGE: clients must use /api/organizations/{organizationId}/projects."
```

Do not use vague messages such as `updates`, `fixed stuff`, or `WIP` in the final branch history.

## 11. Push and Open a Pull Request

Push your branch to your fork:

```shell
git push --set-upstream origin feat/short-description
```

Then open your fork on GitHub and select **Compare & pull request**. Set the base repository to `ECU-Pirate-Forge/scrum-pilot` and the base branch requested by the maintainers.

A useful pull request description includes:

```markdown
## Summary
- Explain what changed.
- Explain why the change is needed.

## Verification
- `dotnet format ./ScrumPilot.slnx --verify-no-changes`
- `dotnet test ./ScrumPilot.UnitTests/ScrumPilot.UnitTests.csproj --configuration Release`
- `dotnet build ./ScrumPilot.slnx --configuration Release`

## Screenshots
Include before/after screenshots for visible UI changes, or write "Not applicable."

Closes #123
```

Use `Closes #123` only when the pull request fully resolves that issue. Otherwise use `Related to #123`.

### Pull Request Checklist

- [ ] The change is focused and does not include unrelated edits.
- [ ] New or changed behavior has focused tests.
- [ ] All required verification commands pass locally.
- [ ] Documentation is updated where behavior or setup changed.
- [ ] UI changes work at desktop and mobile widths.
- [ ] No secrets, local databases, model files, or generated output are committed.
- [ ] Commit messages follow Conventional Commits.
- [ ] The pull request explains the motivation and verification steps.

Respond to review comments with either a code change or a short technical explanation. Push follow-up commits to the same branch; the pull request updates automatically.

## Keeping a Pull Request Up to Date

If the base branch changes while your pull request is open:

```shell
git fetch upstream
git switch feat/short-description
git rebase upstream/main
```

Resolve each conflict, stage the resolved files, and continue:

```shell
git add path/to/resolved-file
git rebase --continue
```

After rebasing a branch already pushed to your fork, update it safely:

```shell
git push --force-with-lease
```

Use `--force-with-lease`, not `--force`, because it refuses to overwrite remote work you have not seen. If you are unfamiliar with rebasing or the branch is shared, ask a maintainer before proceeding.

## Troubleshooting

### `aspire` Is Not Recognized

Reopen the terminal and confirm that the .NET global tools directory is on `PATH`:

```shell
dotnet tool list --global
aspire --version
```

### The API Cannot Connect to Ollama

Check the service and installed models:

```shell
ollama ps
ollama list
```

Verify the endpoint:

#### Windows PowerShell

```powershell
Invoke-RestMethod http://localhost:11434/api/tags
```

#### macOS or Linux

```bash
curl http://localhost:11434/api/tags
```

If the configured model is absent, run `ollama pull gemma4:latest`. Also remove or empty `GroqApiKey`; a non-empty Groq key makes the API choose Groq instead of Ollama.

### HTTPS Certificate Errors

Clear and recreate the development certificate:

```shell
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```

Restart the API and browser afterward. Linux users must also trust the exported certificate using distribution-specific browser or system tools.

### Ports Are Already in Use

Stop the earlier Aspire or `dotnet run` process. For an intentionally separate Aspire instance, use randomized ports:

```shell
aspire run --isolated --apphost ./ScrumPilot.AppHost/ScrumPilot.AppHost.csproj
```

Use the endpoint shown in that instance's Aspire dashboard.

### Build Files Are Locked

Stop Aspire, the API, the Web client, and active debugger sessions before rebuilding. Then run:

```shell
dotnet build ./ScrumPilot.slnx
```

### Reset the Local SQLite Database

Resetting deletes local development data. Stop the API first, then remove `ScrumPilot.Data/scrumpilot.db` if it exists. The API recreates and seeds the database on its next startup.

#### Windows PowerShell

```powershell
Remove-Item .\ScrumPilot.Data\scrumpilot.db -ErrorAction SilentlyContinue
```

#### macOS or Linux

```bash
rm -f ./ScrumPilot.Data/scrumpilot.db
```

## Getting Help

Search existing issues before opening a new one. When reporting a problem, include:

- Your operating system
- Output from `dotnet --version`, `aspire --version`, and `ollama --version`
- The command you ran
- The complete error message or relevant log excerpt
- Minimal steps that reproduce the problem
- What you expected to happen

Remove credentials, access tokens, connection strings, personal data, and other secrets from logs before posting them publicly.
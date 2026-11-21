# ScrumPilot User SQL Administration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans
> for inline implementation and review checkpoints.

**Goal:** Add safe, parameterized SQL Server scripts that create, diagnose,
and repair ScrumPilot control-plane users and memberships.

**Architecture:** Three standalone T-SQL scripts in
`sql/user-administration/`. The scripts share explicit catalog and parameter
conventions, never use dynamic SQL, and keep the repair script read-only in
default dry-run mode.

**Tech Stack:** SQL Server 2025 T-SQL, ASP.NET Core Identity table format,
Git.

---

### Task 1: Create the user insertion script

**Files:**
- Create: `sql/user-administration/01-create-scrumpilot-user.sql`

- [ ] Add a header that requires an explicit control-catalog `USE` target and
  identifies the identity password-hash requirement.
- [ ] Declare and validate `@Email`, `@PasswordHash`, optional organization and
  role parameters, and `@UiPreference`.
- [ ] Use a serializable transaction to reject duplicates and invalid
  organization references before writing.
- [ ] Insert a complete Identity row and optional organization membership.
- [ ] Verify inserted identity and membership postconditions before commit.
- [ ] Commit and record the resulting user ID and optional membership result.

**Validation:** Run syntax/static checks, then execute against a fixture-owned
SQL Server 2025 control catalog with and without organization membership.

### Task 2: Create the diagnostic script

**Files:**
- Create: `sql/user-administration/02-diagnose-scrumpilot-users.sql`

- [ ] Add optional `@Email` and `@OrganizationId` parameters.
- [ ] Add read-only result sets for normalization, password/stamp, lockout,
  membership, default-organization, and invitation findings.
- [ ] Include a summary result set and avoid writing or opening tenant catalogs.
- [ ] Make missing membership itself a normal condition rather than a finding.

**Validation:** Seed known-good and known-bad rows in the fixture-owned catalog
and verify every result set identifies the intended issue.

### Task 3: Create the repair script

**Files:**
- Create: `sql/user-administration/03-repair-scrumpilot-user.sql`

- [ ] Require `@Email` and default `@DryRun` to `1`.
- [ ] Add explicit opt-in variables for normalized fields, security stamp,
  email confirmation, lockout clearing, password hash replacement, and stale
  default organization.
- [ ] Preview every requested change in dry-run mode without writes.
- [ ] Apply only requested fields in a transaction and verify exactly one
  matching target.
- [ ] Never change role, membership, project grant, organization lifecycle, or
  tenant data.

**Validation:** Run dry-run, verify no writes, run each opt-in on an isolated
fixture, and verify only the requested fields changed.

### Task 4: Add operator documentation comments

**Files:**
- Modify: all three scripts

- [ ] Ensure each script states target-catalog and password-hash safety.
- [ ] Include usage notes in comments, not runtime PRINT statements.
- [ ] Keep examples free of plaintext passwords and private connection strings.

### Task 5: Validate and commit

- [ ] Run `git diff --check`.
- [ ] Parse scripts with a SQL Server 2025 fixture if infrastructure is
  available.
- [ ] Report unavailable validation clearly rather than claiming SQL behavior
  has been proven.
- [ ] Commit the scripts under conventional SQL documentation style.

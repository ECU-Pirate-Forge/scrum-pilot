# Project and User Provisioning SQL Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans
> for inline implementation and review checkpoints.

**Goal:** Add one parameterized SQL Server script that creates missing users,
ensures organization memberships, creates a project, and grants project access
to listed Members.

**Architecture:** A single administration script with explicit control and
tenant `USE` targets, an in-script user table, a session temp table that carries
resolved user IDs/membership instances into the tenant phase, and separate
transactions for each database.

**Tech Stack:** SQL Server 2025 T-SQL, ASP.NET Core Identity tables, ScrumPilot
control and organization schemas.

---

### Task 1: Create the project and user provisioning script

**Files:**
- Create: `sql/user-administration/04-create-project-and-grant-users.sql`

- [ ] Add catalog, organization, project, granting owner, dry-run, and user-list
  parameter blocks.
- [ ] Set SQL Server session options and require explicit control/tenant catalog
  targets.
- [ ] Create `@Users` as an operator-populated table containing email,
  optional password hash, organization role, and project-access intent.
- [ ] Validate duplicate emails, project name length, organization state,
  granting owner, and password hashes for missing users.
- [ ] Create missing users and missing organization memberships in the control
  catalog without assigning the global `Admin` role.
- [ ] Populate a session temp table with resolved user IDs and membership
  instance IDs.

**Validation:** Execute syntax/static checks and real SQL Server 2025 fixture
tests for missing and existing users.

### Task 2: Implement the tenant project phase

**Files:**
- Modify: `sql/user-administration/04-create-project-and-grant-users.sql`

- [ ] Switch to the explicit tenant catalog.
- [ ] Verify `TenantMetadata.OrganizationId` matches the organization and that
  the tenant is present.
- [ ] Reject duplicate project names.
- [ ] In apply mode, insert the project and project membership rows for active
  Members only.
- [ ] Match every grant to the resolved organization membership instance.
- [ ] Record the granting owner ID and UTC grant time.

**Validation:** Execute against a fixture tenant and verify project, tenant
membership-instance, and implicit Owner behavior.

### Task 3: Add dry-run and postcondition verification

**Files:**
- Modify: `sql/user-administration/04-create-project-and-grant-users.sql`

- [ ] Default `@DryRun` to `1`.
- [ ] In dry-run mode, preview the project and every user action without writes.
- [ ] In apply mode, verify control postconditions before control commit and
  tenant postconditions before tenant commit.
- [ ] Report a clear partial-completion status if tenant apply fails after
  control commit.

**Validation:** Run dry-run and apply in a fixture; prove dry-run has no writes.

### Task 4: Document operator usage

**Files:**
- Modify: `sql/user-administration/04-create-project-and-grant-users.sql`

- [ ] Add comments for explicit catalog editing, user-list population, password
  hash generation, and re-run behavior.
- [ ] State that the script does not assign global Admin or project grants for
  Owners.
- [ ] State that control and tenant commits are separate and not distributed.

**Validation:** Review comments and parameters for plaintext-password or
private-configuration exposure.

### Task 5: Commit and validate

- [ ] Run `git diff --check`.
- [ ] Execute the script in an isolated SQL Server 2025 control and tenant
  fixture.
- [ ] Verify missing user, existing Member, existing Owner, dry-run, and
  duplicate/project-failure cases.
- [ ] Commit the script with a conventional commit message.

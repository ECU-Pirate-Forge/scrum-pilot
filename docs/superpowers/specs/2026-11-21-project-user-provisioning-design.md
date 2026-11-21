# ScrumPilot Project and User Provisioning SQL Design

## Goal

Add one parameterized SQL Server script that:

1. creates missing ScrumPilot Identity users;
2. ensures they belong to one organization;
3. creates one new organization project; and
4. grants the listed active Members access to that project.

The script is an administrative operation, not application CRUD and not a
migration. It must use the trusted control catalog and the trusted tenant
catalog for the selected organization.

## Location

Create:

- `sql/user-administration/04-create-project-and-grant-users.sql`

The script will be reviewed beside the existing account administration scripts.

## Inputs

The script declares:

- `@ControlCatalog` as an operator-edited `USE` target;
- `@TenantCatalog` as an operator-edited `USE` target;
- `@OrganizationId`;
- `@ProjectName`;
- `@Description`;
- `@GrantingUserEmail`;
- `@DryRun`;
- a `@Users` table with `Email`, `PasswordHash`, `OrganizationRole`, and
  `GrantProjectAccess`.

The script does not accept organization names or tenant catalog names as
authoritative. The operator must replace the two catalog targets and verify
that the tenant `TenantMetadata` row matches `@OrganizationId`.

## User behavior

For every listed email:

- normalize the username and email consistently with the existing account script;
- if no Identity user exists, require an ASP.NET Core Identity
  `PasswordHasher<ApplicationUser>` value and create a confirmed, unlockable
  account;
- if the user already exists, require no password hash and preserve it;
- ensure a current organization membership exists;
- add a membership when missing using `Owner` or `Member`;
- do not demote or promote an existing organization owner/member role;
- do not assign the global `Admin` role.

`PasswordHash` is nullable only for existing users. Missing users fail validation
if no valid Identity password hash is supplied.

## Project behavior

The script validates:

- `@ProjectName` is present and no longer than 200 characters;
- `@OrganizationId` identifies an active, nondeleted organization;
- `@GrantingUserEmail` resolves to exactly one user;
- the granting user is an active `Owner` of the organization;
- `TenantMetadata.OrganizationId` matches `@OrganizationId`;
- no project with the same name already exists in the tenant.

On apply, it creates the project in the tenant catalog and inserts project
membership rows only for active organization Members on the provisioned list.
Each project grant records:

- the project's identity;
- the Member's current `MembershipInstanceId`;
- `GrantedAt` as the current UTC time; and
- `GrantedUserId` as the granting owner.

Organization Owners are not inserted into `ProjectMemberships`, because they
already have implicit project access.

## Idempotency and transactions

The script defaults to `@DryRun = 1`.

Dry-run mode returns one preview result per user and the proposed project
metadata without writing.

Apply mode uses a separate transaction for the control catalog and a separate
transaction for the tenant catalog. It uses a session-scoped temp table to carry
user IDs and membership-instance IDs to the tenant step without cross-database
joins or relationships.

It is intentionally not distributed-transaction atomic across both catalogs.
The script will:

1. validate all control and tenant preconditions before either apply;
2. commit control users/memberships first;
3. then create the project and grant rows in the tenant;
4. fail explicitly with a clear result if the tenant phase cannot complete.

This preserves the project's no-cross-database join boundary and makes partial
control-only completion visible and resumable.

## Safety

- No dynamic SQL.
- No plain-text password fields.
- No global `Admin` assignment.
- No project grants for Organization Owners.
- No organization catalog inference or migration.
- No update or deletion of existing project work.
- No unrelated user or membership repair.
- `SERIALIZABLE` isolation is used for control and tenant apply phases.
- Password hash replacement is out of scope.

## Validation

Use a real SQL Server 2025 fixture-owned control catalog and organization
catalog. Verify:

- a missing Member is created and granted;
- an existing Member is reused and granted;
- an existing Owner is not granted a duplicate row;
- an unknown listed user is not silently created;
- duplicate emails fail;
- invalid organization, tenant identity, granting owner, or project name fails;
- dry-run performs no writes;
- rerunning apply is safe for existing accounts but rejects an existing project
  name or uses a new project name.

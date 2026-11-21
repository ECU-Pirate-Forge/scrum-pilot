# ScrumPilot User SQL Administration Design

## Goal

Add three reviewed SQL Server scripts for SQL Server 2025 control-plane
administration:

1. create a confirmed ScrumPilot Identity user and an optional organization
   membership;
2. diagnose identity, normalization, lockout, membership, default-account,
   and invitation inconsistencies; and
3. repair a targeted user with explicit opt-in operations and a dry-run default.

The scripts operate only on the central control database. They must not infer,
open, migrate, or repair organization databases.

## Location

Create the scripts under `sql/user-administration/`:

- `01-create-scrumpilot-user.sql`
- `02-diagnose-scrumpilot-users.sql`
- `03-repair-scrumpilot-user.sql`

Each script is intended to be opened in SQL Server Management Studio or Azure
Data Studio, edited at a clearly marked parameter block, and executed against
the trusted control catalog. It must not derive a catalog from a user, organization
name, or project ID.

## Creation script

The creation script declares these parameters:

- `@Email`
- `@PasswordHash`
- `@OrganizationId`
- `@OrganizationRole`
- `@UiPreference`

`@PasswordHash` is required and must be an ASP.NET Core Identity
`PasswordHasher<ApplicationUser>` result. Raw SQL cannot safely create this
format. Operators must obtain it through a reviewed application/Identity
operation or equivalent helper, not by using a plain-text password or a
developer-known fixed hash.

The script will:

- use an explicit `USE` target that operators replace before execution;
- normalize the email and username with `UPPER(LTRIM(RTRIM(@Email)))`;
- generate new Identity and membership stamps;
- insert `EmailConfirmed = 1`, `LockoutEnabled = 1`, `UiPreference = Light`
  (or the explicit supported preference), and zero failed-access count;
- optionally add an organization membership with a new
  `MembershipInstanceId`;
- set `DefaultOrganizationId` only when a matching organization membership was
  inserted;
- run in a transaction and fail with no changes if the email or username exists,
  the target organization does not exist, the role is invalid, or required
  fields are missing.

The script does not assign the global `Admin` role. Global `Admin` remains
available only through the existing reviewed operator/bootstrap flow.

## Diagnostic script

The diagnostic script is read-only and accepts optional `@Email` and
`@OrganizationId` filters. With no filters it reports the full control-plane
health inventory.

It returns named result sets for:

- duplicate normalized usernames or emails;
- users missing normalized identity fields;
- users missing password hashes or security stamps;
- lockouts and repeated failed sign-in attempts;
- orphan, role-invalid, or duplicate organization memberships;
- memberships with missing/blank user IDs or organization IDs;
- default organization references to missing organizations or organizations
  where the user has no membership;
- stale default organization values on deleted organizations;
- expired and accepted-without-membership invitations;
- users missing an Identity role assignment only for known roles; and
- summary counts for all findings.

Finding `No membership` is not automatically reported as an issue: a valid
registered user may belong to no organization.

## Repair script

The repair script is targeted by required `@Email`. It defaults to
`@DryRun = 1`.

Explicit opt-in variables are:

- `@RepairNormalizedIdentity`
- `@RepairSecurityStamp`
- `@ConfirmEmail`
- `@ClearLockout`
- `@ReplacePasswordHash`
- `@PasswordHash`
- `@RepairDefaultOrganization`

It will preview or apply only the requested repairs and never change
memberships, roles, organization lifecycle state, or project grants. If a new
password hash is requested, it requires a nonblank ASP.NET Identity hash and
optionally refreshes the security stamp.

`@RepairDefaultOrganization` clears `DefaultOrganizationId` only when it points
to a missing/deleted organization or when the user has no matching organization
membership. It never chooses a replacement organization.

The repair runs transactionally. Dry-run mode returns a preview result set and
performs no writes. Apply mode uses a `SERIALIZABLE`-style safe transaction,
counts affected rows, and verifies the target's postconditions before commit.

## Safety and validation

- All scripts use parameterized variables and no dynamic SQL.
- Scripts fail rather than silently assuming a control catalog.
- The repair script cannot update another user by a normalized-email collision;
  it must resolve exactly one exact target after normalization.
- Final-owner, membership, and project-grant rules are not bypassed.
- SQL Server compatibility and migration state remain governed by EF Core;
  these scripts are controlled recovery/administration tools, not migrations.
- Validation uses real SQL Server 2025 fixture-owned catalogs. Unit-only parsing
  is insufficient for constraints and transaction behavior.

## Out of scope

- Creating organizations or organization databases.
- Editing tenant project memberships.
- Assigning global `Admin`.
- Clearing organization maintenance/restoration fences.
- Replacing EF migrations or operator commands.

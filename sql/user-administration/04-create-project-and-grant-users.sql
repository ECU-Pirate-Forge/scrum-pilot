/*
Creates missing ScrumPilot users, ensures organization membership, creates one
project in the organization tenant, and grants the project to active Members.

BEFORE RUNNING:
1. Replace [scrumpilot_control] with the exact control catalog.
2. Replace [scrumpilot_org_<storage-key>] with the exact trusted tenant catalog.
3. Set @OrganizationId, @ProjectName, @Description, and @GrantingUserEmail.
4. Add email/hash/role/access rows to @Users.
5. Obtain each new user's PasswordHash through ASP.NET Core Identity's
   PasswordHasher<ApplicationUser>. Plain-text passwords are not valid.
6. Leave @DryRun = 1 to preview. Set it to 0 only after review.

This script does not assign the global Admin role. Organization Owners have
implicit project access and do not receive duplicate ProjectMembership rows.
Control and tenant writes are separate transactions, not distributed-atomic.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
USE [scrumpilot_control];
GO

DECLARE @OrganizationId int = 1;
DECLARE @ProjectName nvarchar(200) = N'New Project';
DECLARE @Description nvarchar(max) = NULL;
DECLARE @GrantingUserEmail nvarchar(256) = N'owner@example.com';
DECLARE @DryRun bit = 1;

DECLARE @Users TABLE (
    Email nvarchar(256) NOT NULL,
    PasswordHash nvarchar(max) NULL,
    OrganizationRole nvarchar(max) NOT NULL,
    GrantProjectAccess bit NOT NULL
);

/*
Example rows:
INSERT @Users VALUES
    (N'member@example.com', N'<ASP.NET Identity PasswordHash>', N'Member', 1),
    (N'existing-member@example.com', NULL, N'Member', 1),
    (N'owner@example.com', NULL, N'Owner', 0);
*/

DROP TABLE IF EXISTS #ProvisionParameters;
DROP TABLE IF EXISTS #ProvisionedUsers;
DROP TABLE IF EXISTS #UserInput;
DROP TABLE IF EXISTS #ProvisionPreflightStatus;

CREATE TABLE #ProvisionParameters (
    OrganizationId int NOT NULL,
    ProjectName nvarchar(200) NOT NULL,
    Description nvarchar(max) NULL,
    GrantingUserId nvarchar(450) NULL,
    GrantingUserEmail nvarchar(256) NOT NULL,
    DryRun bit NOT NULL
);

CREATE TABLE #ProvisionPreflightStatus (
    Valid bit NOT NULL
);
INSERT INTO #ProvisionPreflightStatus (Valid) VALUES (0);

CREATE TABLE #ProvisionedUsers (
    StagingId int NOT NULL,
    Email nvarchar(256) NOT NULL,
    NormalizedEmail nvarchar(256) NOT NULL,
    UserId nvarchar(450) NOT NULL,
    MembershipInstanceId uniqueidentifier NOT NULL,
    EffectiveRole nvarchar(max) NOT NULL,
    GrantProjectAccess bit NOT NULL,
    NeedsUser bit NOT NULL,
    NeedsMembership bit NOT NULL
);

CREATE TABLE #UserInput (
    StagingId int NOT NULL,
    Email nvarchar(256) NOT NULL,
    NormalizedEmail nvarchar(256) NOT NULL,
    PasswordHash nvarchar(max) NULL,
    OrganizationRole nvarchar(max) NOT NULL,
    GrantProjectAccess bit NOT NULL
);

SET @ProjectName = LTRIM(RTRIM(COALESCE(@ProjectName, N'')));
SET @GrantingUserEmail = LTRIM(RTRIM(COALESCE(@GrantingUserEmail, N'')));
SET @Description = NULLIF(LTRIM(RTRIM(COALESCE(@Description, N''))), N'');

IF @ProjectName = N''
   THROW 50100, 'ProjectName is required.', 1;

IF LEN(@ProjectName) > 200
   THROW 50101, 'ProjectName cannot exceed 200 characters.', 1;

IF @GrantingUserEmail = N''
   THROW 50102, 'GrantingUserEmail is required.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.Organizations
    WHERE OrganizationId = @OrganizationId
      AND DeletedAt IS NULL
      AND StorageState = N'Active'
)
   THROW 50103, 'OrganizationId must identify an active, nondeleted organization.', 1;

IF NOT EXISTS (SELECT 1 FROM @Users)
   THROW 50104, 'At least one user must be listed.', 1;

INSERT INTO #UserInput (
    StagingId,
    Email,
    NormalizedEmail,
    PasswordHash,
    OrganizationRole,
    GrantProjectAccess
)
SELECT
    ROW_NUMBER() OVER (ORDER BY (SELECT 0)),
    LTRIM(RTRIM(Email)),
    UPPER(LTRIM(RTRIM(Email))),
    PasswordHash,
    OrganizationRole,
    GrantProjectAccess
FROM @Users;

IF EXISTS (
    SELECT 1
    FROM #UserInput
    WHERE Email = N''
       OR OrganizationRole NOT IN (N'Owner', N'Member')
)
   THROW 50105, 'Every listed user requires a valid email and Owner or Member role.', 1;

IF EXISTS (
    SELECT NormalizedEmail
    FROM #UserInput
    GROUP BY NormalizedEmail
    HAVING COUNT(*) > 1
)
   THROW 50106, 'The user list contains duplicate normalized emails.', 1;

IF EXISTS (
    SELECT 1
    FROM #UserInput i
    WHERE NOT EXISTS (
        SELECT 1
        FROM dbo.AspNetUsers u
        WHERE u.NormalizedEmail = i.NormalizedEmail
    )
    AND (
        i.PasswordHash IS NULL
        OR LTRIM(RTRIM(i.PasswordHash)) = N''
        OR LEFT(i.PasswordHash, 6) <> N'AQAAAA'
        OR LEN(i.PasswordHash) < 20
    )
)
   THROW 50107, 'Each missing user requires a valid ASP.NET Identity PasswordHash.', 1;

DECLARE @NormalizedGrantingEmail nvarchar(256) =
    UPPER(LTRIM(RTRIM(@GrantingUserEmail)));

DECLARE @GrantingUserId nvarchar(450);
DECLARE @GrantingMembershipInstanceId uniqueidentifier;

SELECT @GrantingUserId = u.Id
FROM dbo.AspNetUsers u
WHERE u.NormalizedEmail = @NormalizedGrantingEmail;

IF @GrantingUserId IS NULL
   THROW 50108, 'GrantingUserEmail does not resolve to exactly one Identity user.', 1;

IF (
    SELECT COUNT(*)
    FROM dbo.AspNetUsers u
    WHERE u.NormalizedEmail = @NormalizedGrantingEmail
) <> 1
   THROW 50108, 'GrantingUserEmail must resolve to exactly one Identity user.', 1;

SELECT @GrantingMembershipInstanceId = m.MembershipInstanceId
FROM dbo.OrganizationMemberships m
WHERE m.OrganizationId = @OrganizationId
  AND m.UserId = @GrantingUserId
  AND m.Role = N'Owner';

IF @GrantingMembershipInstanceId IS NULL
   THROW 50109, 'The granting user is not an active Owner of the organization.', 1;

DECLARE @StagingId int = 1;
DECLARE @MaxStagingId int = (SELECT MAX(StagingId) FROM #UserInput);

WHILE @StagingId <= @MaxStagingId
BEGIN
    DECLARE @Email nvarchar(256);
    DECLARE @NormalizedEmail nvarchar(256);
    DECLARE @PasswordHash nvarchar(max);
    DECLARE @RequestedRole nvarchar(max);
    DECLARE @GrantAccess bit;

    SELECT
        @Email = Email,
        @NormalizedEmail = NormalizedEmail,
        @PasswordHash = PasswordHash,
        @RequestedRole = OrganizationRole,
        @GrantAccess = GrantProjectAccess
    FROM #UserInput
    WHERE StagingId = @StagingId;

    DECLARE @UserId nvarchar(450);
    DECLARE @MembershipInstanceId uniqueidentifier;
    DECLARE @EffectiveRole nvarchar(max);
    DECLARE @NeedsUser bit = 0;
    DECLARE @NeedsMembership bit = 0;

    SET @UserId = NULL;
    SET @MembershipInstanceId = NULL;
    SET @EffectiveRole = NULL;
    SET @NeedsUser = 0;
    SET @NeedsMembership = 0;

    SELECT @UserId = u.Id
    FROM dbo.AspNetUsers u
    WHERE u.NormalizedEmail = @NormalizedEmail;

    IF @UserId IS NULL
    BEGIN
        SET @NeedsUser = 1;
        SET @UserId = CONVERT(nvarchar(36), NEWID());
        SET @MembershipInstanceId = NEWID();
        SET @EffectiveRole = @RequestedRole;
        SET @NeedsMembership = 1;
    END
    ELSE
    BEGIN
        SELECT
            @MembershipInstanceId = m.MembershipInstanceId,
            @EffectiveRole = m.Role
        FROM dbo.OrganizationMemberships m
        WHERE m.OrganizationId = @OrganizationId
          AND m.UserId = @UserId;

        IF @MembershipInstanceId IS NULL
        BEGIN
            SET @MembershipInstanceId = NEWID();
            SET @EffectiveRole = @RequestedRole;
            SET @NeedsMembership = 1;
        END
    END;

    IF @EffectiveRole = N'Owner'
        SET @GrantAccess = 0;

    INSERT INTO #ProvisionedUsers (
        StagingId,
        Email,
        NormalizedEmail,
        UserId,
        MembershipInstanceId,
        EffectiveRole,
        GrantProjectAccess,
        NeedsUser,
        NeedsMembership
    )
    VALUES (
        @StagingId,
        @Email,
        @NormalizedEmail,
        @UserId,
        @MembershipInstanceId,
        @EffectiveRole,
        @GrantAccess,
        @NeedsUser,
        @NeedsMembership
    );

    SET @StagingId += 1;
END;

INSERT INTO #ProvisionParameters (
    OrganizationId,
    ProjectName,
    Description,
    GrantingUserId,
    GrantingUserEmail,
    DryRun
)
VALUES (
    @OrganizationId,
    @ProjectName,
    @Description,
    @GrantingUserId,
    @GrantingUserEmail,
    @DryRun
);

SELECT
    p.OrganizationId,
    p.ProjectName,
    p.Description,
    p.GrantingUserEmail,
    u.Email,
    u.EffectiveRole,
    u.GrantProjectAccess,
    CASE WHEN u.NeedsUser = 1 THEN 'CreateUser' ELSE 'ReuseUser' END AS UserAction,
    CASE WHEN u.NeedsMembership = 1 THEN 'CreateMembership'
         ELSE 'ReuseMembership' END AS MembershipAction,
    p.DryRun
FROM #ProvisionParameters p
CROSS JOIN #ProvisionedUsers u;

IF @DryRun = 1
BEGIN
    PRINT 'DryRun - control catalog: no writes applied.';
END
GO

/*
Tenant preflight and apply target.
Replace [scrumpilot_org_<storage-key>] with the exact trusted tenant catalog.
*/
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
USE [scrumpilot_org_<storage-key>];
GO

DECLARE @OrganizationId int =
    (SELECT OrganizationId FROM #ProvisionParameters);
DECLARE @ProjectName nvarchar(200) =
    (SELECT ProjectName FROM #ProvisionParameters);
DECLARE @Description nvarchar(max) =
    (SELECT Description FROM #ProvisionParameters);
DECLARE @DryRun bit =
    (SELECT DryRun FROM #ProvisionParameters);

IF NOT EXISTS (
    SELECT 1
    FROM dbo.TenantMetadata
    WHERE Id = 1
      AND OrganizationId = @OrganizationId
)
   THROW 50111, 'TenantMetadata does not match OrganizationId.', 1;

IF EXISTS (
    SELECT 1
    FROM dbo.Project
    WHERE OrganizationId = @OrganizationId
      AND ProjectName = @ProjectName
)
   THROW 50112, 'A project with this name already exists in the tenant.', 1;

UPDATE #ProvisionPreflightStatus SET Valid = 1;

SELECT
    @OrganizationId AS OrganizationId,
    @ProjectName AS ProjectName,
    @Description AS Description,
    u.Email,
    u.EffectiveRole,
    u.GrantProjectAccess,
    @DryRun AS DryRun
FROM #ProvisionedUsers u;

IF @DryRun = 1
BEGIN
    PRINT 'DryRun - tenant catalog: no writes applied.';
    RETURN;
END;
GO

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
USE [scrumpilot_control];
GO

IF NOT EXISTS (
    SELECT 1
    FROM #ProvisionPreflightStatus
    WHERE Valid = 1
)
   THROW 50114, 'Tenant preflight did not pass; control changes were not applied.', 1;

DECLARE @OrganizationId int =
    (SELECT OrganizationId FROM #ProvisionParameters);
DECLARE @MaxStagingId int =
    (SELECT COUNT(*) FROM #ProvisionedUsers);

SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;

DECLARE @ControlUserId nvarchar(450);
DECLARE @ControlEmail nvarchar(256);
DECLARE @ControlPasswordHash nvarchar(max);
DECLARE @ControlRole nvarchar(max);
DECLARE @ControlMembershipInstanceId uniqueidentifier;
DECLARE @ControlNeedsUser bit;
DECLARE @ControlNeedsMembership bit;
DECLARE @ControlStagingId int = 1;

WHILE @ControlStagingId <= @MaxStagingId
BEGIN
    SELECT
        @ControlEmail = i.Email,
        @ControlPasswordHash = i.PasswordHash,
        @ControlRole = i.OrganizationRole
    FROM #UserInput i
    WHERE i.StagingId = @ControlStagingId;

    SELECT
        @ControlUserId = p.UserId,
        @ControlMembershipInstanceId = p.MembershipInstanceId,
        @ControlRole = p.EffectiveRole,
        @ControlNeedsUser = p.NeedsUser,
        @ControlNeedsMembership = p.NeedsMembership
    FROM #ProvisionedUsers p
    WHERE p.StagingId = @ControlStagingId;

    IF @ControlNeedsUser = 1
    BEGIN
        INSERT INTO dbo.AspNetUsers (
            Id,
            UiPreference,
            DefaultProjectId,
            DefaultOrganizationId,
            UserName,
            NormalizedUserName,
            Email,
            NormalizedEmail,
            EmailConfirmed,
            PasswordHash,
            SecurityStamp,
            ConcurrencyStamp,
            PhoneNumber,
            PhoneNumberConfirmed,
            TwoFactorEnabled,
            LockoutEnd,
            LockoutEnabled,
            AccessFailedCount
        )
        VALUES (
            @ControlUserId,
            N'Light',
            NULL,
            @OrganizationId,
            @ControlEmail,
            UPPER(LTRIM(RTRIM(@ControlEmail))),
            @ControlEmail,
            UPPER(LTRIM(RTRIM(@ControlEmail))),
            1,
            @ControlPasswordHash,
            CONVERT(nvarchar(36), NEWID()),
            CONVERT(nvarchar(36), NEWID()),
            NULL,
            0,
            0,
            NULL,
            1,
            0
        );
    END
    ELSE
    BEGIN
        UPDATE dbo.AspNetUsers
        SET DefaultOrganizationId =
            CASE
                WHEN DefaultOrganizationId IS NULL THEN @OrganizationId
                ELSE DefaultOrganizationId
            END
        WHERE Id = @ControlUserId;
    END;

    IF @ControlNeedsMembership = 1
    BEGIN
        INSERT INTO dbo.OrganizationMemberships (
            OrganizationId,
            UserId,
            MembershipInstanceId,
            Role,
            JoinedAt
        )
        VALUES (
            @OrganizationId,
            @ControlUserId,
            @ControlMembershipInstanceId,
            @ControlRole,
            SYSUTCDATETIME()
        );
    END;

    SET @ControlStagingId += 1;
END;

IF EXISTS (
    SELECT 1
    FROM #ProvisionedUsers p
    WHERE NOT EXISTS (
        SELECT 1
        FROM dbo.AspNetUsers u
        WHERE u.Id = p.UserId
          AND u.EmailConfirmed = 1
    )
    OR NOT EXISTS (
        SELECT 1
        FROM dbo.OrganizationMemberships m
        WHERE m.OrganizationId = @OrganizationId
          AND m.UserId = p.UserId
          AND m.MembershipInstanceId = p.MembershipInstanceId
    )
)
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50110, 'Control catalog postcondition verification failed.', 1;
END;

COMMIT TRANSACTION;
GO

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
USE [scrumpilot_org_<storage-key>];
GO

IF NOT EXISTS (
    SELECT 1
    FROM #ProvisionPreflightStatus
    WHERE Valid = 1
)
   THROW 50114, 'Tenant preflight did not pass; tenant changes were not applied.', 1;

DECLARE @OrganizationId int =
    (SELECT OrganizationId FROM #ProvisionParameters);
DECLARE @ProjectName nvarchar(200) =
    (SELECT ProjectName FROM #ProvisionParameters);
DECLARE @Description nvarchar(max) =
    (SELECT Description FROM #ProvisionParameters);

SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;

INSERT INTO dbo.Project (
    OrganizationId,
    ProjectName,
    Description
)
VALUES (
    @OrganizationId,
    @ProjectName,
    @Description
);

DECLARE @ProjectId int = CONVERT(int, SCOPE_IDENTITY());

DECLARE @LoopId int = 1;
DECLARE @UserCount int = (SELECT COUNT(*) FROM #ProvisionedUsers);

WHILE @LoopId <= @UserCount
BEGIN
    DECLARE @GrantUserId nvarchar(450);
    DECLARE @GrantMembershipInstanceId uniqueidentifier;
    DECLARE @GrantRole nvarchar(max);
    DECLARE @GrantAccess bit;

    SET @GrantUserId = NULL;
    SET @GrantMembershipInstanceId = NULL;
    SET @GrantRole = NULL;
    SET @GrantAccess = 0;

    SELECT TOP (1)
        @GrantUserId = UserId,
        @GrantMembershipInstanceId = MembershipInstanceId,
        @GrantRole = EffectiveRole,
        @GrantAccess = GrantProjectAccess
    FROM #ProvisionedUsers
    WHERE StagingId = @LoopId;

    IF @GrantRole = N'Member' AND @GrantAccess = 1
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM dbo.ProjectMemberships
            WHERE ProjectId = @ProjectId
              AND UserId = @GrantUserId
        )
        BEGIN
            UPDATE dbo.ProjectMemberships
            SET
                MembershipInstanceId = @GrantMembershipInstanceId,
                GrantedAt = SYSUTCDATETIME(),
                GrantedByUserId =
                    (SELECT GrantingUserId FROM #ProvisionParameters)
            WHERE ProjectId = @ProjectId
              AND UserId = @GrantUserId;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.ProjectMemberships (
                ProjectId,
                UserId,
                MembershipInstanceId,
                GrantedAt,
                GrantedByUserId
            )
            VALUES (
                @ProjectId,
                @GrantUserId,
                @GrantMembershipInstanceId,
                SYSUTCDATETIME(),
                (SELECT GrantingUserId FROM #ProvisionParameters)
            );
        END;
    END;

    SET @LoopId += 1;
END;

IF EXISTS (
    SELECT 1
    FROM dbo.Project p
    WHERE p.ProjectId = @ProjectId
      AND (
          p.OrganizationId <> @OrganizationId
          OR p.ProjectName <> @ProjectName
      )
)
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50113, 'Tenant project postcondition verification failed.', 1;
END;

COMMIT TRANSACTION;

SELECT
    @ProjectId AS ProjectId,
    @OrganizationId AS OrganizationId,
    @ProjectName AS ProjectName,
    'Created' AS Result;

DROP TABLE IF EXISTS #ProvisionParameters;
DROP TABLE IF EXISTS #ProvisionedUsers;
DROP TABLE IF EXISTS #UserInput;
DROP TABLE IF EXISTS #ProvisionPreflightStatus;
GO

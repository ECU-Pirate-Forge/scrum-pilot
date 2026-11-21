/*
Safely repairs a targeted ScrumPilot control-plane user.

BEFORE RUNNING:
1. Replace scrumpilot_control below with the exact configured
   SqlServer:ControlCatalog value.
2. Set @Email to exactly one target account.
3. Leave @DryRun = 1 to preview changes.
4. Set only the explicit repair flags you intend to apply.

The script never repairs organization memberships, roles, project grants,
organization lifecycle state, or tenant databases.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
USE [scrumpilot_control];
GO

DECLARE @Email nvarchar(256) = N'user@example.com';
DECLARE @DryRun bit = 1;

DECLARE @RepairNormalizedIdentity bit = 0;
DECLARE @RepairSecurityStamp bit = 0;
DECLARE @ConfirmEmail bit = 0;
DECLARE @ClearLockout bit = 0;
DECLARE @ReplacePasswordHash bit = 0;
DECLARE @PasswordHash nvarchar(max) = NULL;
DECLARE @RepairDefaultOrganization bit = 0;

IF @Email IS NULL OR LTRIM(RTRIM(@Email)) = N''
   THROW 50020, 'Email is required.', 1;

IF @ReplacePasswordHash = 1
   AND (@PasswordHash IS NULL OR LTRIM(RTRIM(@PasswordHash)) = N'')
   THROW 50021, 'A replacement ASP.NET Identity PasswordHash is required.', 1;

IF @ReplacePasswordHash = 1
   AND (
       LEFT(@PasswordHash, 6) <> N'AQAAAA'
       OR LEN(@PasswordHash) < 20
   )
   THROW 50026, 'PasswordHash does not use a valid ASP.NET Identity encoded-hash format.', 1;

SET @Email = LTRIM(RTRIM(@Email));
DECLARE @NormalizedEmail nvarchar(256) = UPPER(@Email);
DECLARE @NewSecurityStamp nvarchar(max) = CONVERT(nvarchar(36), NEWID());
DECLARE @NewConcurrencyStamp nvarchar(max) = CONVERT(nvarchar(36), NEWID());

SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;

DECLARE @UserId nvarchar(450);
DECLARE @MatchCount int;

SELECT @MatchCount = COUNT(*)
FROM dbo.AspNetUsers
WHERE NormalizedEmail = @NormalizedEmail;

IF @MatchCount <> 1
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50022, 'The repair target must resolve to exactly one user.', 1;
END;

SELECT @UserId = Id
FROM dbo.AspNetUsers
WHERE NormalizedEmail = @NormalizedEmail;

/* Preview every requested identity change. */
SELECT
    @UserId AS UserId,
    @Email AS TargetEmail,
    @DryRun AS DryRun,
    CASE WHEN @RepairNormalizedIdentity = 1
         THEN UPPER(LTRIM(RTRIM(Email)))
         ELSE NormalizedEmail END AS ProposedNormalizedEmail,
    CASE WHEN @RepairNormalizedIdentity = 1
         THEN UPPER(LTRIM(RTRIM(COALESCE(UserName, Email))))
         ELSE NormalizedUserName END AS ProposedNormalizedUserName,
    CASE WHEN @RepairSecurityStamp = 1
         THEN @NewSecurityStamp
         ELSE SecurityStamp END AS ProposedSecurityStamp,
    CASE WHEN @ConfirmEmail = 1
         THEN CONVERT(bit, 1)
         ELSE CONVERT(bit, EmailConfirmed) END AS ProposedEmailConfirmed,
    CASE WHEN @ClearLockout = 1
         THEN NULL
         ELSE LockoutEnd END AS ProposedLockoutEnd,
    CASE WHEN @ClearLockout = 1
         THEN 0
         ELSE AccessFailedCount END AS ProposedAccessFailedCount,
    CASE WHEN @ReplacePasswordHash = 1
         THEN CONVERT(bit, 1)
         ELSE CONVERT(bit, 0) END AS ReplacePasswordHash,
    CASE WHEN @RepairDefaultOrganization = 1
              AND (
                  NOT EXISTS (
                      SELECT 1
                      FROM dbo.Organizations o
                      WHERE o.OrganizationId = u.DefaultOrganizationId
                        AND o.DeletedAt IS NULL
                  )
                  OR NOT EXISTS (
                      SELECT 1
                      FROM dbo.OrganizationMemberships m
                      WHERE m.OrganizationId = u.DefaultOrganizationId
                        AND m.UserId = u.Id
                  )
              )
         THEN NULL
         ELSE u.DefaultOrganizationId END AS ProposedDefaultOrganizationId
FROM dbo.AspNetUsers u
WHERE u.Id = @UserId;

IF @DryRun = 1
BEGIN
    ROLLBACK TRANSACTION;
    SELECT 'DryRun - no changes applied' AS Result;
END
ELSE
BEGIN
    UPDATE u
    SET
        NormalizedEmail =
            CASE WHEN @RepairNormalizedIdentity = 1
                 THEN UPPER(LTRIM(RTRIM(u.Email)))
                 ELSE u.NormalizedEmail END,
        NormalizedUserName =
            CASE WHEN @RepairNormalizedIdentity = 1
                 THEN UPPER(LTRIM(RTRIM(COALESCE(u.UserName, u.Email))))
                 ELSE u.NormalizedUserName END,
        SecurityStamp =
            CASE WHEN @RepairSecurityStamp = 1
                 THEN @NewSecurityStamp
                 ELSE u.SecurityStamp END,
        ConcurrencyStamp =
            CASE WHEN @RepairSecurityStamp = 1
                 THEN @NewConcurrencyStamp
                 ELSE u.ConcurrencyStamp END,
        EmailConfirmed =
            CASE WHEN @ConfirmEmail = 1
                 THEN 1
                 ELSE u.EmailConfirmed END,
        LockoutEnd =
            CASE WHEN @ClearLockout = 1
                 THEN NULL
                 ELSE u.LockoutEnd END,
        AccessFailedCount =
            CASE WHEN @ClearLockout = 1
                 THEN 0
                 ELSE u.AccessFailedCount END,
        PasswordHash =
            CASE WHEN @ReplacePasswordHash = 1
                 THEN @PasswordHash
                 ELSE u.PasswordHash END,
        DefaultOrganizationId =
            CASE WHEN @RepairDefaultOrganization = 1
                      AND (
                          NOT EXISTS (
                              SELECT 1
                              FROM dbo.Organizations o
                              WHERE o.OrganizationId = u.DefaultOrganizationId
                                AND o.DeletedAt IS NULL
                          )
                          OR NOT EXISTS (
                              SELECT 1
                              FROM dbo.OrganizationMemberships m
                              WHERE m.OrganizationId = u.DefaultOrganizationId
                                AND m.UserId = u.Id
                          )
                      )
                 THEN NULL
                 ELSE u.DefaultOrganizationId END
    FROM dbo.AspNetUsers u
    WHERE u.Id = @UserId;

    IF @RepairNormalizedIdentity = 1
       AND EXISTS (
           SELECT 1
           FROM dbo.AspNetUsers
           WHERE Id = @UserId
             AND (
                 NormalizedEmail <> UPPER(LTRIM(RTRIM(Email)))
                 OR NormalizedUserName <> UPPER(LTRIM(RTRIM(COALESCE(UserName, Email))))
             )
       )
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50023, 'Normalized identity postcondition verification failed.', 1;
    END;

    IF @ReplacePasswordHash = 1
       AND EXISTS (
           SELECT 1
           FROM dbo.AspNetUsers
           WHERE Id = @UserId
             AND PasswordHash <> @PasswordHash
       )
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50024, 'Password hash postcondition verification failed.', 1;
    END;

    IF @RepairDefaultOrganization = 1
       AND EXISTS (
           SELECT 1
           FROM dbo.AspNetUsers u
           WHERE u.Id = @UserId
             AND u.DefaultOrganizationId IS NOT NULL
             AND (
                 NOT EXISTS (
                     SELECT 1
                     FROM dbo.Organizations o
                     WHERE o.OrganizationId = u.DefaultOrganizationId
                       AND o.DeletedAt IS NULL
                 )
                 OR NOT EXISTS (
                     SELECT 1
                     FROM dbo.OrganizationMemberships m
                     WHERE m.OrganizationId = u.DefaultOrganizationId
                       AND m.UserId = u.Id
                 )
             )
       )
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50025, 'Default organization postcondition verification failed.', 1;
    END;

    COMMIT TRANSACTION;

    SELECT
        @UserId AS UserId,
        @Email AS Email,
        'Applied requested repairs' AS Result;
END;
GO

/*
Creates one confirmed ScrumPilot Identity user in the central control database.

BEFORE RUNNING:
1. Replace scrumpilot_control below with the exact configured
   SqlServer:ControlCatalog value.
2. Set @Email and @PasswordHash.
3. Obtain @PasswordHash from ASP.NET Core Identity's PasswordHasher.
   A plain-text password is not a valid PasswordHash.

This script deliberately does not assign the global Admin role.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
USE [scrumpilot_control];
GO

DECLARE @Email nvarchar(256) = N'user@example.com';
DECLARE @PasswordHash nvarchar(max) = N'<ASP.NET Identity PasswordHash>';
DECLARE @OrganizationId int = NULL;
DECLARE @OrganizationRole nvarchar(max) = N'Member';
DECLARE @UiPreference nvarchar(max) = N'Light';

DECLARE @NormalizedEmail nvarchar(256);
DECLARE @NormalizedUserName nvarchar(256);
DECLARE @UserId nvarchar(450) = CONVERT(nvarchar(36), NEWID());
DECLARE @SecurityStamp nvarchar(max) = CONVERT(nvarchar(36), NEWID());
DECLARE @ConcurrencyStamp nvarchar(max) = CONVERT(nvarchar(36), NEWID());
DECLARE @MembershipInstanceId uniqueidentifier = NEWID();
DECLARE @Now datetime2 = SYSUTCDATETIME();

IF @Email IS NULL
   OR LTRIM(RTRIM(@Email)) = N''
   THROW 50010, 'Email is required.', 1;

IF @PasswordHash IS NULL
   OR LTRIM(RTRIM(@PasswordHash)) = N''
   THROW 50011, 'An ASP.NET Identity PasswordHash is required.', 1;

IF LEFT(@PasswordHash, 6) <> N'AQAAAA'
   OR LEN(@PasswordHash) < 20
   THROW 50017, 'PasswordHash does not use a valid ASP.NET Identity encoded-hash format.', 1;

IF @UiPreference NOT IN (N'Light', N'Dark', N'System')
   THROW 50012, 'UiPreference must be Light, Dark, or System.', 1;

IF @OrganizationId IS NOT NULL
   AND @OrganizationRole NOT IN (N'Owner', N'Member')
   THROW 50013, 'OrganizationRole must be Owner or Member.', 1;

SET @Email = LTRIM(RTRIM(@Email));
SET @NormalizedEmail = UPPER(@Email);
SET @NormalizedUserName = UPPER(@Email);

SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;

IF EXISTS (
    SELECT 1
    FROM dbo.AspNetUsers
    WHERE NormalizedEmail = @NormalizedEmail
       OR NormalizedUserName = @NormalizedUserName
)
   THROW 50014, 'A user already exists with this email or username.', 1;

IF @OrganizationId IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM dbo.Organizations
       WHERE OrganizationId = @OrganizationId
         AND DeletedAt IS NULL
   )
   THROW 50015, 'The organization does not exist or has been deleted.', 1;

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
    @UserId,
    @UiPreference,
    NULL,
    @OrganizationId,
    @Email,
    @NormalizedUserName,
    @Email,
    @NormalizedEmail,
    1,
    @PasswordHash,
    @SecurityStamp,
    @ConcurrencyStamp,
    NULL,
    0,
    0,
    NULL,
    1,
    0
);

IF @OrganizationId IS NOT NULL
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
        @UserId,
        @MembershipInstanceId,
        @OrganizationRole,
        @Now
    );
END;

IF EXISTS (
    SELECT 1
    FROM dbo.AspNetUsers
    WHERE Id = @UserId
      AND EmailConfirmed = 1
      AND PasswordHash IS NOT NULL
      AND SecurityStamp IS NOT NULL
)
    AND (
        @OrganizationId IS NULL
        OR EXISTS (
            SELECT 1
            FROM dbo.OrganizationMemberships
            WHERE OrganizationId = @OrganizationId
              AND UserId = @UserId
              AND MembershipInstanceId = @MembershipInstanceId
        )
    )
BEGIN
    COMMIT TRANSACTION;

    SELECT
        @UserId AS UserId,
        @Email AS Email,
        @OrganizationId AS OrganizationId,
        @OrganizationRole AS OrganizationRole,
        @MembershipInstanceId AS MembershipInstanceId,
        'Created' AS Result;
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50016, 'Post-create verification failed; no user or membership was created.', 1;
END;
GO

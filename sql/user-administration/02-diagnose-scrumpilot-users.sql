/*
Read-only diagnostics for ScrumPilot control-plane users and memberships.

BEFORE RUNNING:
1. Replace scrumpilot_control below with the exact configured
   SqlServer:ControlCatalog value.
2. Optionally set @Email or @OrganizationId to restrict findings.

This script does not open or inspect organization databases.
*/

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
USE [scrumpilot_control];
GO

DECLARE @Email nvarchar(256) = NULL;
DECLARE @OrganizationId int = NULL;

DECLARE @NormalizedEmail nvarchar(256);
IF @Email IS NULL
    SET @NormalizedEmail = NULL;
ELSE
    SET @NormalizedEmail = UPPER(LTRIM(RTRIM(@Email)));

PRINT 'ScrumPilot user diagnostics';

/* Duplicate normalized identity values */
SELECT
    'Duplicate normalized identity' AS Finding,
    NormalizedEmail,
    COUNT(*) AS UserCount
FROM dbo.AspNetUsers
WHERE (@NormalizedEmail IS NULL OR NormalizedEmail = @NormalizedEmail)
  AND NormalizedEmail IS NOT NULL
GROUP BY NormalizedEmail
HAVING COUNT(*) > 1
UNION ALL
SELECT
    'Duplicate normalized username' AS Finding,
    NormalizedUserName,
    COUNT(*) AS UserCount
FROM dbo.AspNetUsers
WHERE (@NormalizedEmail IS NULL OR NormalizedUserName = @NormalizedEmail)
  AND NormalizedUserName IS NOT NULL
GROUP BY NormalizedUserName
HAVING COUNT(*) > 1;

/* Missing or inconsistent Identity fields */
SELECT
    Id AS UserId,
    UserName,
    Email,
    CASE
        WHEN NormalizedEmail IS NULL OR NormalizedEmail = N'' THEN 'MissingNormalizedEmail'
        WHEN NormalizedUserName IS NULL OR NormalizedUserName = N'' THEN 'MissingNormalizedUserName'
        WHEN NormalizedEmail <> UPPER(LTRIM(RTRIM(Email))) THEN 'StaleNormalizedEmail'
        WHEN NormalizedUserName <> UPPER(LTRIM(RTRIM(UserName))) THEN 'StaleNormalizedUserName'
        WHEN PasswordHash IS NULL OR PasswordHash = N'' THEN 'MissingPasswordHash'
        WHEN SecurityStamp IS NULL OR SecurityStamp = N'' THEN 'MissingSecurityStamp'
        WHEN ConcurrencyStamp IS NULL OR ConcurrencyStamp = N'' THEN 'MissingConcurrencyStamp'
        WHEN EmailConfirmed <> 1 THEN 'EmailNotConfirmed'
        ELSE 'UnexpectedIdentityIssue'
    END AS Finding
FROM dbo.AspNetUsers
WHERE (@NormalizedEmail IS NULL OR NormalizedEmail = @NormalizedEmail)
  AND (
      NormalizedEmail IS NULL
      OR NormalizedEmail = N''
      OR NormalizedUserName IS NULL
      OR NormalizedUserName = N''
      OR (Email IS NOT NULL AND NormalizedEmail <> UPPER(LTRIM(RTRIM(Email))))
      OR (UserName IS NOT NULL AND NormalizedUserName <> UPPER(LTRIM(RTRIM(UserName))))
      OR PasswordHash IS NULL
      OR PasswordHash = N''
      OR SecurityStamp IS NULL
      OR SecurityStamp = N''
      OR ConcurrencyStamp IS NULL
      OR ConcurrencyStamp = N''
      OR EmailConfirmed <> 1
  );

/* Lockout state */
SELECT
    Id AS UserId,
    UserName,
    Email,
    LockoutEnd,
    AccessFailedCount,
    LockoutEnabled
FROM dbo.AspNetUsers
WHERE (@NormalizedEmail IS NULL OR NormalizedEmail = @NormalizedEmail)
  AND (
      (LockoutEnd IS NOT NULL AND LockoutEnd > SYSDATETIMEOFFSET())
      OR AccessFailedCount > 0
  );

/* Membership problems that are discoverable inside the control database */
SELECT
    m.OrganizationId,
    m.UserId,
    m.MembershipInstanceId,
    m.Role,
    m.JoinedAt,
    CASE
        WHEN u.Id IS NULL THEN 'MissingUser'
        WHEN o.OrganizationId IS NULL THEN 'MissingOrganization'
        WHEN m.Role NOT IN (N'Owner', N'Member') THEN 'InvalidRole'
        WHEN m.MembershipInstanceId = '00000000-0000-0000-0000-000000000000'
          THEN 'MissingMembershipInstance'
        ELSE 'UnexpectedMembershipIssue'
    END AS Finding
FROM dbo.OrganizationMemberships m
LEFT JOIN dbo.AspNetUsers u ON u.Id = m.UserId
LEFT JOIN dbo.Organizations o ON o.OrganizationId = m.OrganizationId
WHERE (@OrganizationId IS NULL OR m.OrganizationId = @OrganizationId)
  AND (
      u.Id IS NULL
      OR o.OrganizationId IS NULL
      OR m.Role NOT IN (N'Owner', N'Member')
      OR m.MembershipInstanceId = '00000000-0000-0000-0000-000000000000'
  );

/* Duplicate membership-instance IDs, normally caught by a unique index */
SELECT
    MembershipInstanceId,
    COUNT(*) AS MembershipCount
FROM dbo.OrganizationMemberships
WHERE (@OrganizationId IS NULL OR OrganizationId = @OrganizationId)
GROUP BY MembershipInstanceId
HAVING COUNT(*) > 1;

/* Default organization references */
SELECT
    u.Id AS UserId,
    u.UserName,
    u.Email,
    u.DefaultOrganizationId,
    CASE
        WHEN o.OrganizationId IS NULL THEN 'MissingDefaultOrganization'
        WHEN o.DeletedAt IS NOT NULL THEN 'DeletedDefaultOrganization'
        WHEN NOT EXISTS (
            SELECT 1
            FROM dbo.OrganizationMemberships m
            WHERE m.OrganizationId = u.DefaultOrganizationId
              AND m.UserId = u.Id
        ) THEN 'DefaultOrganizationWithoutMembership'
        ELSE 'UnexpectedDefaultOrganizationIssue'
    END AS Finding
FROM dbo.AspNetUsers u
LEFT JOIN dbo.Organizations o
  ON o.OrganizationId = u.DefaultOrganizationId
WHERE u.DefaultOrganizationId IS NOT NULL
  AND (@NormalizedEmail IS NULL OR u.NormalizedEmail = @NormalizedEmail)
  AND (
      o.OrganizationId IS NULL
      OR o.DeletedAt IS NOT NULL
      OR NOT EXISTS (
          SELECT 1
          FROM dbo.OrganizationMemberships m
          WHERE m.OrganizationId = u.DefaultOrganizationId
            AND m.UserId = u.Id
      )
  );

/* Invitations that may require account/membership follow-up */
SELECT
    i.OrganizationInvitationId,
    i.OrganizationId,
    i.Email,
    i.Status,
    i.ExpiresAt,
    i.AcceptedAt,
    CASE
        WHEN i.Status = N'Pending'
          AND i.ExpiresAt < SYSUTCDATETIME() THEN 'ExpiredPendingInvitation'
        WHEN i.Status = N'Accepted'
          AND i.AcceptedAt IS NULL THEN 'AcceptedWithoutAcceptedTimestamp'
        WHEN i.Status = N'Accepted'
          AND NOT EXISTS (
              SELECT 1
              FROM dbo.AspNetUsers u
              JOIN dbo.OrganizationMemberships m
                ON m.UserId = u.Id
               AND m.OrganizationId = i.OrganizationId
              WHERE u.NormalizedEmail = i.NormalizedEmail
          ) THEN 'AcceptedInvitationWithoutMembership'
        ELSE 'UnexpectedInvitationIssue'
    END AS Finding
FROM dbo.OrganizationInvitations i
WHERE (@OrganizationId IS NULL OR i.OrganizationId = @OrganizationId)
  AND (
      (i.Status = N'Pending' AND i.ExpiresAt < SYSUTCDATETIME())
      OR (i.Status = N'Accepted' AND i.AcceptedAt IS NULL)
      OR (
          i.Status = N'Accepted'
          AND NOT EXISTS (
              SELECT 1
              FROM dbo.AspNetUsers u
              JOIN dbo.OrganizationMemberships m
                ON m.UserId = u.Id
               AND m.OrganizationId = i.OrganizationId
              WHERE u.NormalizedEmail = i.NormalizedEmail
          )
      )
  );

/* Summary counts */
SELECT
    (SELECT COUNT(*) FROM dbo.AspNetUsers
     WHERE (@NormalizedEmail IS NULL OR NormalizedEmail = @NormalizedEmail)
       AND (PasswordHash IS NULL OR PasswordHash = N'' OR SecurityStamp IS NULL OR SecurityStamp = N'' OR EmailConfirmed <> 1))
      AS IdentityIssues,
    (SELECT COUNT(*) FROM dbo.OrganizationMemberships m
     WHERE (@OrganizationId IS NULL OR m.OrganizationId = @OrganizationId)
       AND (NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u WHERE u.Id = m.UserId)
            OR NOT EXISTS (SELECT 1 FROM dbo.Organizations o WHERE o.OrganizationId = m.OrganizationId)
            OR m.Role NOT IN (N'Owner', N'Member')))
      AS MembershipIssues,
    (SELECT COUNT(*) FROM dbo.AspNetUsers
     WHERE (@NormalizedEmail IS NULL OR NormalizedEmail = @NormalizedEmail)
       AND DefaultOrganizationId IS NOT NULL
       AND (
           NOT EXISTS (SELECT 1 FROM dbo.Organizations o WHERE o.OrganizationId = DefaultOrganizationId)
           OR EXISTS (SELECT 1 FROM dbo.Organizations o WHERE o.OrganizationId = DefaultOrganizationId AND o.DeletedAt IS NOT NULL)
           OR NOT EXISTS (
               SELECT 1 FROM dbo.OrganizationMemberships m
               WHERE m.OrganizationId = DefaultOrganizationId
                 AND m.UserId = dbo.AspNetUsers.Id
           )
       ))
      AS DefaultOrganizationIssues,
    (SELECT COUNT(*) FROM dbo.AspNetUsers
     WHERE (@NormalizedEmail IS NULL OR NormalizedEmail = @NormalizedEmail)
       AND (LockoutEnd IS NOT NULL AND LockoutEnd > SYSDATETIMEOFFSET() OR AccessFailedCount > 0))
      AS LockoutIssues;
GO

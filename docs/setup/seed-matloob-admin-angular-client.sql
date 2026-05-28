-- ============================================================
-- IdentityServer seed — matloob:admin-angular (Angular SPA, port 4200)
--
-- Target DB:  IdentityServerAdmin   (SQL Server)
-- Safe to re-run: every INSERT is guarded by NOT EXISTS.
--
-- What this registers:
--   * Client                "matloob:admin-angular"
--                           Authorization Code + PKCE, no client secret
--   * Grant type            authorization_code
--   * Scopes                openid, profile, email, roles,
--                           matloob:api, matloob:admin
--   * RedirectUri           http://localhost:4200/auth/callback
--   * PostLogoutRedirectUri http://localhost:4200/auth/logout
--   * CORS Origin           http://localhost:4200
--
-- NOTE: ApiResources / ApiScopes for "matloob:api" and "matloob:admin"
--       must already exist (they are seeded by the back-office side).
--       If they don't, the client will still be created but tokens
--       won't carry those audiences.
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

USE [IdentityServerAdmin];
GO

BEGIN TRAN;

DECLARE @ClientKey      nvarchar(200) = N'matloob:admin-angular';
DECLARE @ClientName     nvarchar(200) = N'Matloob Angular Admin';
DECLARE @ClientDesc     nvarchar(1000) = N'Angular admin SPA (port 4200) — PKCE, no client secret';
DECLARE @RedirectUri    nvarchar(400) = N'http://localhost:4200/auth/callback';
DECLARE @PostLogoutUri  nvarchar(400) = N'http://localhost:4200/auth/logout';
DECLARE @CorsOrigin     nvarchar(150) = N'http://localhost:4200';

-- -----------------------------------------------------------
-- 1. Client row
-- -----------------------------------------------------------
DECLARE @ClientId int = (SELECT Id FROM Clients WHERE ClientId = @ClientKey);

IF @ClientId IS NULL
BEGIN
    INSERT INTO Clients (
        Enabled, ClientId, ProtocolType, RequireClientSecret, ClientName, Description,
        RequireConsent, AllowRememberConsent, AlwaysIncludeUserClaimsInIdToken,
        RequirePkce, AllowPlainTextPkce, AllowAccessTokensViaBrowser,
        FrontChannelLogoutSessionRequired, BackChannelLogoutSessionRequired,
        AllowOfflineAccess, IdentityTokenLifetime, AccessTokenLifetime,
        AuthorizationCodeLifetime, AbsoluteRefreshTokenLifetime, SlidingRefreshTokenLifetime,
        RefreshTokenUsage, UpdateAccessTokenClaimsOnRefresh, RefreshTokenExpiration,
        AccessTokenType, EnableLocalLogin, IncludeJwtId, AlwaysSendClientClaims,
        Created, DeviceCodeLifetime, NonEditable, RequireRequestObject,
        RequireDPoP, RequirePushedAuthorization, DPoPClockSkew, DPoPValidationMode
    ) VALUES (
        1, @ClientKey, 'oidc', 0, @ClientName, @ClientDesc,
        0, 1, 1,                  -- no consent, remember, include claims in id_token
        1, 0, 1,                  -- RequirePkce=1, AllowAccessTokensViaBrowser=1 (SPA)
        1, 1,
        0, 300, 3600,             -- id token 5 min, access token 1 hour
        300, 2592000, 1296000,    -- code 5 min, abs refresh 30 d, sliding 15 d
        1, 0, 1,
        0, 1, 1, 0,
        SYSUTCDATETIME(), 300, 0, 0,
        0, 0, '00:00:05', 0
    );
    SET @ClientId = SCOPE_IDENTITY();
    PRINT N'+ Created client ' + @ClientKey + N' (Id=' + CAST(@ClientId AS nvarchar(10)) + N').';
END
ELSE
    PRINT N'= Client ' + @ClientKey + N' already exists (Id=' + CAST(@ClientId AS nvarchar(10)) + N').';

-- -----------------------------------------------------------
-- 2. Grant type
-- -----------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM ClientGrantTypes
               WHERE ClientId = @ClientId AND GrantType = 'authorization_code')
BEGIN
    INSERT INTO ClientGrantTypes (GrantType, ClientId)
    VALUES ('authorization_code', @ClientId);
    PRINT N'+ Added grant type authorization_code';
END

-- -----------------------------------------------------------
-- 3. Allowed scopes
-- -----------------------------------------------------------
;WITH scopes(name) AS (
    SELECT 'openid'        UNION ALL
    SELECT 'profile'       UNION ALL
    SELECT 'email'         UNION ALL
    SELECT 'roles'         UNION ALL
    SELECT 'matloob:api'   UNION ALL
    SELECT 'matloob:admin'
)
INSERT INTO ClientScopes (Scope, ClientId)
SELECT s.name, @ClientId
FROM   scopes s
WHERE  NOT EXISTS (SELECT 1 FROM ClientScopes cs
                   WHERE  cs.ClientId = @ClientId AND cs.Scope = s.name);

-- -----------------------------------------------------------
-- 4. Redirect URI
-- -----------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM ClientRedirectUris
               WHERE ClientId = @ClientId AND RedirectUri = @RedirectUri)
BEGIN
    INSERT INTO ClientRedirectUris (RedirectUri, ClientId)
    VALUES (@RedirectUri, @ClientId);
    PRINT N'+ Added redirect URI ' + @RedirectUri;
END

-- -----------------------------------------------------------
-- 5. Post-logout redirect URI
-- -----------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM ClientPostLogoutRedirectUris
               WHERE ClientId = @ClientId AND PostLogoutRedirectUri = @PostLogoutUri)
BEGIN
    INSERT INTO ClientPostLogoutRedirectUris (PostLogoutRedirectUri, ClientId)
    VALUES (@PostLogoutUri, @ClientId);
    PRINT N'+ Added post-logout URI ' + @PostLogoutUri;
END

-- -----------------------------------------------------------
-- 6. CORS origin
-- -----------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM ClientCorsOrigins
               WHERE ClientId = @ClientId AND Origin = @CorsOrigin)
BEGIN
    INSERT INTO ClientCorsOrigins (Origin, ClientId)
    VALUES (@CorsOrigin, @ClientId);
    PRINT N'+ Added CORS origin ' + @CorsOrigin;
END

COMMIT;
GO

-- ============================================================
-- Verification — should print the full configuration
-- ============================================================
PRINT '';
PRINT '=== matloob:admin-angular post-state ===';

SELECT c.Id, c.ClientId, c.ClientName, c.Enabled, c.RequirePkce, c.RequireClientSecret
FROM   Clients c WHERE c.ClientId = 'matloob:admin-angular';

SELECT c.ClientId, gt.GrantType
FROM   Clients c JOIN ClientGrantTypes gt ON gt.ClientId = c.Id
WHERE  c.ClientId = 'matloob:admin-angular';

SELECT c.ClientId, cs.Scope
FROM   Clients c JOIN ClientScopes cs ON cs.ClientId = c.Id
WHERE  c.ClientId = 'matloob:admin-angular' ORDER BY cs.Scope;

SELECT c.ClientId, ru.RedirectUri
FROM   Clients c JOIN ClientRedirectUris ru ON ru.ClientId = c.Id
WHERE  c.ClientId = 'matloob:admin-angular';

SELECT c.ClientId, plu.PostLogoutRedirectUri
FROM   Clients c JOIN ClientPostLogoutRedirectUris plu ON plu.ClientId = c.Id
WHERE  c.ClientId = 'matloob:admin-angular';

SELECT c.ClientId, co.Origin
FROM   Clients c JOIN ClientCorsOrigins co ON co.ClientId = c.Id
WHERE  c.ClientId = 'matloob:admin-angular';
GO

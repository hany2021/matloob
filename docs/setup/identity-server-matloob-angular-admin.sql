-- ============================================================
-- IdentityServer seed for the new Angular Admin (matloob:admin-angular)
--
-- Target DB: IdentityServerAdmin   (SQL Server, restored from
--                                   EServicesPortal (1)/IdentityServerAdmin.bak)
--
-- Idempotent: re-running is safe; rows already present are skipped.
--
-- Prerequisites (run once before this script):
--   1.  Restore IdentityServerAdmin.bak + EServicesPortal.bak via
--       docker-compose at d:/Sure/EServicesPortal (1)/.
--   2.  Run _tmp_matloob_register.sql from d:/Sure/eservices-backend/
--       to create the matloob:api / matloob:admin ApiResources +
--       ApiScopes + the matloob_admin / matloob_user roles.
--
-- This script is additive: it only registers the Angular SPA client
-- (port 4200, no client secret, PKCE) and adds matloob:api +
-- matloob:admin to its allowed scopes so the admin token carries both
-- audiences. The existing matloob:admin and matloob:front-dev clients
-- are left untouched.
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

USE [IdentityServerAdmin];
GO

BEGIN TRAN;

-- --- 1. ApiResources / ApiScopes -- sanity check ---------------
IF NOT EXISTS (SELECT 1 FROM ApiResources WHERE Name = 'matloob:api')
    THROW 50001, 'matloob:api ApiResource is missing. Run _tmp_matloob_register.sql first.', 1;
IF NOT EXISTS (SELECT 1 FROM ApiResources WHERE Name = 'matloob:admin')
    THROW 50002, 'matloob:admin ApiResource is missing. Run _tmp_matloob_register.sql first.', 1;

-- --- 2. Client: matloob:admin-angular --------------------------
DECLARE @ClientId int = (SELECT Id FROM Clients WHERE ClientId = 'matloob:admin-angular');

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
        1, 'matloob:admin-angular', 'oidc', 0, 'Matloob Angular Admin', 'Angular admin SPA (port 4200)',
        0, 1, 1,
        1, 0, 1,                 -- RequirePkce=1, AllowAccessTokensViaBrowser=1
        1, 1,
        0, 300, 3600,
        300, 2592000, 1296000,
        1, 0, 1,
        0, 1, 1, 0,
        SYSUTCDATETIME(), 300, 0, 0,
        0, 0, '00:00:05', 0
    );
    SET @ClientId = SCOPE_IDENTITY();
    PRINT 'Created client matloob:admin-angular (id=' + CAST(@ClientId AS varchar(10)) + ').';
END
ELSE
    PRINT 'Client matloob:admin-angular already exists (id=' + CAST(@ClientId AS varchar(10)) + ').';

-- --- 3. Grant types --------------------------------------------
IF NOT EXISTS (SELECT 1 FROM ClientGrantTypes WHERE ClientId = @ClientId AND GrantType = 'authorization_code')
    INSERT INTO ClientGrantTypes (GrantType, ClientId) VALUES ('authorization_code', @ClientId);

-- --- 4. Allowed scopes -----------------------------------------
;WITH scopes(name) AS (
    SELECT 'openid'         UNION ALL
    SELECT 'profile'        UNION ALL
    SELECT 'email'          UNION ALL
    SELECT 'roles'          UNION ALL
    SELECT 'matloob:api'    UNION ALL
    SELECT 'matloob:admin'
)
INSERT INTO ClientScopes (Scope, ClientId)
SELECT s.name, @ClientId FROM scopes s
WHERE NOT EXISTS (
    SELECT 1 FROM ClientScopes cs
    WHERE cs.ClientId = @ClientId AND cs.Scope = s.name);

-- --- 5. Redirect / post-logout / CORS --------------------------
IF NOT EXISTS (SELECT 1 FROM ClientRedirectUris WHERE ClientId = @ClientId AND RedirectUri = 'http://localhost:4200/auth/callback')
    INSERT INTO ClientRedirectUris (RedirectUri, ClientId) VALUES ('http://localhost:4200/auth/callback', @ClientId);

IF NOT EXISTS (SELECT 1 FROM ClientPostLogoutRedirectUris WHERE ClientId = @ClientId AND PostLogoutRedirectUri = 'http://localhost:4200/auth/logout')
    INSERT INTO ClientPostLogoutRedirectUris (PostLogoutRedirectUri, ClientId) VALUES ('http://localhost:4200/auth/logout', @ClientId);

IF NOT EXISTS (SELECT 1 FROM ClientCorsOrigins WHERE ClientId = @ClientId AND Origin = 'http://localhost:4200')
    INSERT INTO ClientCorsOrigins (Origin, ClientId) VALUES ('http://localhost:4200', @ClientId);

COMMIT;

PRINT '--- matloob:admin-angular post-state ---';
SELECT c.ClientId, cs.Scope
  FROM Clients c JOIN ClientScopes cs ON cs.ClientId = c.Id
 WHERE c.ClientId = 'matloob:admin-angular' ORDER BY cs.Scope;

SELECT c.ClientId, ru.RedirectUri
  FROM Clients c JOIN ClientRedirectUris ru ON ru.ClientId = c.Id
 WHERE c.ClientId = 'matloob:admin-angular';

SELECT c.ClientId, plu.PostLogoutRedirectUri
  FROM Clients c JOIN ClientPostLogoutRedirectUris plu ON plu.ClientId = c.Id
 WHERE c.ClientId = 'matloob:admin-angular';

SELECT c.ClientId, co.Origin
  FROM Clients c JOIN ClientCorsOrigins co ON co.ClientId = c.Id
 WHERE c.ClientId = 'matloob:admin-angular';
GO

/**
 * Default (development) environment. Override per-build via Angular CLI
 * file replacements (see angular.json `fileReplacements`).
 *
 * Every URL is configured via env var here, never hardcoded inside
 * services. The values below are LOCAL DEV defaults — production
 * builds rewrite this file to environment.prod.ts.
 *
 * Local stack:
 * - Matloob API:       http://localhost:5180 (dotnet run from backend/src/Matloob.Api)
 * - IdentityServer:    https://localhost:44310 (NEC.IdentityServer.STS.Identity,
 *   eservices-backend repo). The client id below + the redirect / scope
 *   set must exist in the restored IdentityServerAdmin database — see
 *   docs/60-identity-setup.md for the one-time seed.
 */
export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5180',
  identityAuthority: 'https://localhost:44310',
  clientId: 'matloob:admin-angular',
  redirectUri: 'http://localhost:4200/auth/callback',
  postLogoutRedirectUri: 'http://localhost:4200/auth/logout',
  // matloob:api → token aud accepted by every authenticated endpoint.
  // matloob:admin → token aud additionally required by /api/v1/admin/*
  // routes (see AuthRegistration.cs Policy.Admin). The Angular admin
  // user holds matloob_admin role, so it asks for both up front.
  scope: 'openid profile matloob:api matloob:admin',
};

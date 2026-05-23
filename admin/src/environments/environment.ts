/**
 * Default (development) environment. Override per-build via Angular CLI
 * file replacements (see angular.json `fileReplacements`).
 *
 * Every URL is configured via env var here, never hardcoded inside
 * services. The values below are LOCAL DEV defaults — production
 * builds rewrite this file to environment.prod.ts.
 */
export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:8080',
  identityAuthority: 'http://localhost:5310',
  clientId: 'matloob:admin',
  redirectUri: 'http://localhost:4200/auth/callback',
  postLogoutRedirectUri: 'http://localhost:4200/auth/logout',
  scope: 'openid profile matloob:api',
};

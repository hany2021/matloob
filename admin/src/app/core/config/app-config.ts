import { environment } from '../../../environments/environment';

/**
 * Strongly-typed view onto the runtime environment. Centralised so
 * services consume a single object instead of importing the raw
 * environment file all over the codebase.
 */
export const APP_CONFIG = {
  apiBaseUrl: environment.apiBaseUrl,
  identity: {
    authority: environment.identityAuthority,
    clientId: environment.clientId,
    redirectUri: environment.redirectUri,
    postLogoutRedirectUri: environment.postLogoutRedirectUri,
    scope: environment.scope,
  },
  production: environment.production,
} as const;

export type AppConfig = typeof APP_CONFIG;

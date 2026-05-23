import { Injectable, inject, signal } from '@angular/core';
import { AuthConfig, OAuthService } from 'angular-oauth2-oidc';
import { APP_CONFIG } from '../config/app-config';

/**
 * Thin wrapper around <c>angular-oauth2-oidc</c> that exposes a
 * signal-based view of the auth state and centralises configuration.
 *
 * Roles + sub claim are read from the access token. Components that
 * need them can either read the signals here or pull the claims directly
 * via <see cref="OAuthService.getIdentityClaims"/>.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly oauth = inject(OAuthService);

  /** True once the OIDC flow has produced a valid access token. */
  readonly isAuthenticated = signal<boolean>(false);

  /** IdM <c>sub</c> claim of the current user. Null when signed out. */
  readonly userSub = signal<string | null>(null);

  /** Display name from the id token (best-effort). */
  readonly userName = signal<string | null>(null);

  /** Email from the id token. */
  readonly userEmail = signal<string | null>(null);

  /**
   * Role claims on the access token. Empty when signed out. The string
   * values match the .NET <c>matloob_admin</c> / <c>matloob_user</c>
   * conventions defined in <c>MatloobPolicies</c>.
   */
  readonly roles = signal<readonly string[]>([]);

  /** Convenience flag for admin-only routing decisions. */
  readonly isAdmin = signal<boolean>(false);

  configure(): void {
    const cfg: AuthConfig = {
      issuer: APP_CONFIG.identity.authority,
      clientId: APP_CONFIG.identity.clientId,
      redirectUri: APP_CONFIG.identity.redirectUri,
      postLogoutRedirectUri: APP_CONFIG.identity.postLogoutRedirectUri,
      responseType: 'code',
      scope: APP_CONFIG.identity.scope,
      requireHttps: APP_CONFIG.production,
      showDebugInformation: !APP_CONFIG.production,
    };
    this.oauth.configure(cfg);

    // Automatic silent refresh is intentionally NOT enabled. The
    // matloob:admin-angular client isn't seeded with offline_access /
    // AllowOfflineAccess, so the STS never issues a refresh token —
    // calling setupAutomaticSilentRefresh() would just produce a
    // "POST /connect/token 400" retry loop in the console with no
    // upside. When the 1-hour access token expires, the
    // ProblemDetailsInterceptor's 401 branch redirects the user
    // through the OIDC login flow again.

    this.oauth.events.subscribe(() => this.refreshLocalState());
    this.refreshLocalState();
  }

  /** Boot-time discovery + silent login attempt. */
  async loadDiscoveryAndTryLogin(): Promise<void> {
    try {
      await this.oauth.loadDiscoveryDocumentAndTryLogin();
    } catch {
      // Discovery failure is non-fatal at app startup; the user can
      // click "Login" to trigger a fresh redirect.
    }
    this.refreshLocalState();
  }

  startLogin(targetRoute?: string): void {
    if (targetRoute) {
      sessionStorage.setItem('matloob.admin.return_to', targetRoute);
    }
    this.oauth.initLoginFlow();
  }

  logout(): void {
    sessionStorage.removeItem('matloob.admin.return_to');
    this.oauth.logOut();
  }

  /** Returns the access token for the HTTP interceptor. */
  getAccessToken(): string | null {
    return this.oauth.getAccessToken() ?? null;
  }

  /** Pull "where should the user land after login" from sessionStorage. */
  consumeReturnTo(): string | null {
    const value = sessionStorage.getItem('matloob.admin.return_to');
    if (value) {
      sessionStorage.removeItem('matloob.admin.return_to');
    }
    return value;
  }

  private refreshLocalState(): void {
    const validToken = this.oauth.hasValidAccessToken();
    this.isAuthenticated.set(validToken);

    if (!validToken) {
      this.userSub.set(null);
      this.userName.set(null);
      this.userEmail.set(null);
      this.roles.set([]);
      this.isAdmin.set(false);
      return;
    }

    const claims = (this.oauth.getIdentityClaims() ?? {}) as Record<
      string,
      unknown
    >;
    this.userSub.set((claims['sub'] as string | undefined) ?? null);
    this.userName.set((claims['name'] as string | undefined) ?? null);
    this.userEmail.set((claims['email'] as string | undefined) ?? null);

    const rawRole = claims['role'];
    const list = Array.isArray(rawRole)
      ? rawRole.filter((r): r is string => typeof r === 'string')
      : typeof rawRole === 'string'
        ? [rawRole]
        : [];
    this.roles.set(list);
    this.isAdmin.set(list.includes('matloob_admin'));
  }
}

import { HttpClient } from '@angular/common/http';
import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { Observable, catchError, map, of } from 'rxjs';
import { APP_CONFIG } from '../config/app-config';
import { AuthService } from './auth.service';

/**
 * Route guard: redirects unauthenticated callers to the login flow
 * (preserving the requested URL in sessionStorage so the callback can
 * route the user back).
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isAuthenticated()) {
    return true;
  }
  auth.startLogin(state.url);
  return router.parseUrl('/auth/login');
};

/**
 * Stricter guard: requires the caller's role list to include
 * <c>matloob_admin</c>. Unauthenticated callers are sent through the OIDC
 * login flow; authenticated non-admins are sent to the standalone
 * <c>/auth/forbidden</c> page (which sits OUTSIDE the admin shell, so the
 * redirect can't loop when this guard protects the shell itself).
 *
 * Beyond the JWT role, it also verifies with the server that the admin is still
 * ACTIVE: the token can still carry <c>matloob_admin</c> after an admin has been
 * deactivated (is_active=false). A 403 from the lightweight
 * <c>GET /api/v1/admin/access</c> probe means deactivated → route to the same
 * forbidden page. (Runs on shell entry, so it also covers the dashboard.)
 */
export const adminGuard: CanActivateFn = (
  _route,
  state,
): boolean | UrlTree | Observable<boolean | UrlTree> => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const http = inject(HttpClient);

  if (!auth.isAuthenticated()) {
    auth.startLogin(state.url);
    return router.parseUrl('/auth/login');
  }
  if (!auth.isAdmin()) {
    return router.parseUrl('/auth/forbidden');
  }

  return http
    .get(`${APP_CONFIG.apiBaseUrl}/api/v1/admin/access`, {
      headers: { 'X-Suppress-Error-Toast': '1' },
    })
    .pipe(
      map(() => true),
      // 403 => deactivated admin → forbidden page with reason=inactive so the
      // page shows an "account inactive" message (vs. the role-missing one).
      // Other errors (network/5xx) fail open so a transient blip doesn't lock
      // admins out.
      catchError((e: { status?: number }) =>
        of(e?.status === 403 ? router.parseUrl('/auth/forbidden?reason=inactive') : true),
      ),
    );
};

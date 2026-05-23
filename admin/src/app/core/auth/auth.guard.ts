import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
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
 * <c>matloob_admin</c>. Non-admin authenticated users are redirected
 * to the dashboard with an audit-friendly query flag.
 */
export const adminGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuthenticated()) {
    auth.startLogin();
    return router.parseUrl('/auth/login');
  }
  if (!auth.isAdmin()) {
    return router.parseUrl('/dashboard?forbidden=admin');
  }
  return true;
};

import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../auth/auth.service';
import { APP_CONFIG } from '../config/app-config';

/**
 * Attaches the IdM bearer token + the standard <c>Accept:
 * application/json</c> header to every request that targets the
 * configured backend. External requests (e.g. IdentityServer discovery)
 * are left untouched.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const targetsBackend = req.url.startsWith(APP_CONFIG.apiBaseUrl);
  if (!targetsBackend) {
    return next(req);
  }

  const token = inject(AuthService).getAccessToken();
  const headers: Record<string, string> = {
    Accept: 'application/json',
  };
  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }

  return next(
    req.clone({
      setHeaders: headers,
    }),
  );
};

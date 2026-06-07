import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { ProblemDetails } from '../models/problem-details';
import { ToastService } from '../../shared/components/toast.service';

/**
 * Translates RFC 7807 ProblemDetails responses into typed errors +
 * routes uniform behavior for 401/403/423. Caller error subscribers
 * still see the error and can render contextual UI; this interceptor
 * only adds cross-cutting side-effects (toasts, redirects).
 */
export const problemDetailsInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const toast = inject(ToastService);

  return next(req).pipe(
    catchError((err: unknown) => {
      if (!(err instanceof HttpErrorResponse)) {
        return throwError(() => err);
      }

      const problem = parseProblemDetails(err);

      // Callers can opt out of the cross-cutting toast/redirect (e.g. the admin
      // route guard, which handles its own 403 by routing to /auth/forbidden).
      if (req.headers.has('X-Suppress-Error-Toast')) {
        return throwError(() => problem);
      }

      switch (err.status) {
        case 0:
          toast.error(
            'Network unavailable',
            'Could not reach the API. Check your connection.',
          );
          break;
        case 401:
          auth.startLogin(router.url);
          break;
        case 403:
          // Authenticated but not allowed — most importantly a deactivated
          // admin (is_active=false on the server) whose token still carries
          // matloob_admin. Surface a clear "not permitted" message.
          toast.error(
            'غير مسموح',
            problem.detail ??
              'ليس لديك صلاحية للقيام بهذا الإجراء. قد يكون حسابك غير مفعّل.',
          );
          break;
        case 423:
          toast.warning(
            'Establishment suspended',
            problem.detail ??
              'This establishment is suspended; mutation actions are blocked.',
          );
          break;
        case 500:
        case 502:
        case 503:
        case 504:
          toast.error(
            'Server error',
            problem.detail ??
              `The server returned ${err.status}. Try again in a moment.`,
          );
          break;
        default:
          // 400/404/409/422 are surfaced as inline UI by the caller;
          // we don't double-toast them.
          break;
      }

      return throwError(() => problem);
    }),
  );
};

function parseProblemDetails(err: HttpErrorResponse): ProblemDetails {
  const body = err.error;
  if (body && typeof body === 'object') {
    const obj = body as Record<string, unknown>;
    return {
      type: typeof obj['type'] === 'string' ? obj['type'] : undefined,
      title: typeof obj['title'] === 'string' ? obj['title'] : undefined,
      status: typeof obj['status'] === 'number' ? obj['status'] : err.status,
      detail: typeof obj['detail'] === 'string' ? obj['detail'] : undefined,
      traceId:
        typeof obj['traceId'] === 'string' ? obj['traceId'] : undefined,
      code: typeof obj['code'] === 'string' ? obj['code'] : undefined,
      raw: obj,
    };
  }
  return { status: err.status, detail: err.message, raw: null };
}

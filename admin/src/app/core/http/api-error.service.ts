import { Injectable, inject } from '@angular/core';

import { ProblemDetails } from '../models/problem-details';
import { ToastService } from '../../shared/components/toast.service';

/**
 * Surfaces the inline error states that ProblemDetailsInterceptor
 * deliberately leaves to the caller:
 *
 * - 400 validation / 422 business validation → warning toast
 * - 403 forbidden → warning toast
 * - 404 not found → warning toast
 * - 409 conflict → warning toast
 *
 * 401 / 423 / 5xx are already handled cross-cuttingly by the
 * interceptor, so this service is a no-op for those statuses; calling
 * it on those errors is safe (and useful — pages don't need to branch
 * on status before deciding whether to render an error).
 *
 * Network / unknown errors fall back to a generic toast with the
 * caller-supplied `fallback` message.
 */
@Injectable({ providedIn: 'root' })
export class ApiErrorService {
  private readonly toast = inject(ToastService);

  /**
   * Show a toast for the given error, picking the right wording from
   * the parsed ProblemDetails when available. `fallback` is the title
   * used when the error doesn't carry actionable ProblemDetails (e.g.
   * the request never reached the server).
   */
  notify(err: unknown, fallback = 'Action failed'): void {
    const problem = this.asProblem(err);
    if (!problem) {
      this.toast.error(fallback);
      return;
    }

    switch (problem.status) {
      case 401:
      case 423:
      case 500:
      case 502:
      case 503:
      case 504:
        // Already toasted/redirected by the interceptor; don't double up.
        return;
      case 400:
      case 422:
        this.toast.warning(this.title(problem, 'Invalid request'), problem.detail);
        return;
      case 403:
        this.toast.warning(
          this.title(problem, 'Forbidden'),
          problem.detail ?? "You don't have permission to perform that action.",
        );
        return;
      case 404:
        this.toast.warning(
          this.title(problem, 'Not found'),
          problem.detail ?? 'The requested resource no longer exists.',
        );
        return;
      case 409:
        this.toast.warning(
          this.title(problem, 'Conflict'),
          problem.detail ?? 'The resource changed before your request was applied.',
        );
        return;
      default:
        this.toast.error(this.title(problem, fallback), problem.detail);
    }
  }

  /** Returns true when the error has a recognisable ProblemDetails shape. */
  private asProblem(err: unknown): ProblemDetails | null {
    if (!err || typeof err !== 'object') return null;
    const candidate = err as Partial<ProblemDetails>;
    if (typeof candidate.status === 'number') {
      return candidate as ProblemDetails;
    }
    return null;
  }

  /** Compose a toast title from the optional ProblemDetails code. */
  private title(problem: ProblemDetails, fallback: string): string {
    if (problem.code) return `${fallback} (${problem.code})`;
    return fallback;
  }
}

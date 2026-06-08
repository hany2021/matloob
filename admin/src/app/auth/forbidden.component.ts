import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { ProfileService } from '../core/services/profile.service';

/**
 * Standalone page shown when a signed-in user can't enter the admin panel.
 * Two causes, distinguished by the <c>reason</c> query param set by
 * {@link adminGuard}:
 *   - <c>reason=inactive</c> → an admin whose account was deactivated
 *     (is_active=false) — the server returned 403 from the access probe.
 *   - (no reason) → the user lacks the <c>matloob_admin</c> role.
 *
 * It lives OUTSIDE the admin shell on purpose: the shell is guarded by
 * adminGuard, so redirecting a rejected user to an in-shell route would loop.
 */
@Component({
  selector: 'app-forbidden',
  standalone: true,
  imports: [],
  template: `
    <div class="forbidden-page">
      <div class="card">
        @if (inactive) {
          <h1>الحساب غير مفعّل</h1>
          <p>
            حسابك كمشرف غير مفعّل حاليًا، فلا يمكنك الوصول للوحة الإدارة.
            تواصل مع المسؤول لإعادة تفعيله.
          </p>
        } @else {
          <h1>لا تملك صلاحية الوصول</h1>
          <p>
            هذه اللوحة مخصّصة للمستخدمين الذين يملكون صلاحية
            <code>matloob_admin</code>.
          </p>
        }

        @if (auth.userName(); as name) {
          <p class="muted">مسجّل الدخول باسم {{ name }}.</p>
        } @else if (auth.userEmail(); as email) {
          <p class="muted">مسجّل الدخول باسم {{ email }}.</p>
        }

        <p>
          <a class="logout-link" (click)="logout()">سجّل الخروج</a> وادخل بحساب
          مشرف مفعّل، أو تواصل مع المسؤول إذا كنت تعتقد أن هذا حدث عن طريق الخطأ.
        </p>
      </div>
    </div>
  `,
  styles: [
    `
      .forbidden-page {
        min-height: 100vh;
        display: grid;
        place-items: center;
        padding: var(--space-4);
      }
      .card {
        max-width: 30rem;
      }
      .logout-link {
        color: var(--color-primary);
        text-decoration: underline;
        cursor: pointer;
      }
    `,
  ],
})
export class ForbiddenComponent {
  protected readonly auth = inject(AuthService);
  private readonly profileService = inject(ProfileService);
  private readonly route = inject(ActivatedRoute);

  protected readonly inactive =
    this.route.snapshot.queryParamMap.get('reason') === 'inactive';

  /** Real sign-out — same as the header: OIDC end-session + clear cached profile. */
  protected logout(): void {
    this.auth.logout();
    this.profileService.clear();
  }
}

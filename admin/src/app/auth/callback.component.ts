import { Component, OnInit, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';

@Component({
  selector: 'app-callback',
  standalone: true,
  template: `
    <div class="callback-page">
      <p class="muted">Completing sign-in…</p>
    </div>
  `,
  styles: [
    `
      .callback-page {
        min-height: 100vh;
        display: grid;
        place-items: center;
      }
    `,
  ],
})
export class CallbackComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  async ngOnInit(): Promise<void> {
    // The OIDC library has already handled the callback exchange via
    // tryLogin() called at app boot. We just route the user to wherever
    // they were headed before the redirect.
    const target = this.auth.consumeReturnTo() ?? '/dashboard';
    await this.router.navigateByUrl(target);
  }
}

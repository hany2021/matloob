import { Component, OnInit, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  template: `
    <div class="login-page">
      <div class="card login-card">
        <h1>مطلوب — لوحة الإدارة</h1>
        <p class="muted">
          سجّل الدخول بحساب IdentityServer للمتابعة.
        </p>
        <button type="button" class="btn" (click)="signIn()">
          تسجيل الدخول
        </button>
      </div>
    </div>
  `,
  styles: [
    `
      .login-page {
        min-height: 100vh;
        display: grid;
        place-items: center;
      }
      .login-card {
        min-width: 320px;
        text-align: center;
      }
      .login-card h1 {
        margin: 0 0 var(--space-3);
      }
      .login-card p {
        margin: 0 0 var(--space-5);
      }
    `,
  ],
})
export class LoginComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  ngOnInit(): void {
    if (this.auth.isAuthenticated()) {
      this.router.navigateByUrl('/dashboard');
    }
  }

  signIn(): void {
    this.auth.startLogin();
  }
}

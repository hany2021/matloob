import { Component } from '@angular/core';

@Component({
  selector: 'app-logout',
  standalone: true,
  template: `
    <div class="logout-page">
      <div class="card">
        <p>تم تسجيل خروجك.</p>
        <p><a routerLink="/auth/login">تسجيل الدخول مجدداً</a></p>
      </div>
    </div>
  `,
  styles: [
    `
      .logout-page {
        min-height: 100vh;
        display: grid;
        place-items: center;
      }
    `,
  ],
})
export class LogoutComponent {}

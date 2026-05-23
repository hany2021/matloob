import { Component } from '@angular/core';

@Component({
  selector: 'app-logout',
  standalone: true,
  template: `
    <div class="logout-page">
      <div class="card">
        <p>You have been signed out.</p>
        <p><a routerLink="/auth/login">Sign in again</a></p>
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

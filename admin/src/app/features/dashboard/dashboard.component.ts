import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, ActivatedRoute } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <div class="page">
      <header class="page-header">
        <h1>Dashboard</h1>
      </header>

      @if (route.snapshot.queryParamMap.get('forbidden') === 'admin') {
        <div class="card" style="border-color: #d33; color: #a31515;">
          <strong>Admin-only area.</strong>
          You are signed in as a regular user; the page you tried to open
          is restricted to <code>matloob_admin</code>. Contact your admin
          if you believe this is a mistake.
        </div>
      }

      <div class="grid">
        <div class="card">
          <h3>Signed in</h3>
          <p class="muted">
            @if (auth.userName(); as name) {
              {{ name }}
            } @else if (auth.userEmail(); as email) {
              {{ email }}
            } @else {
              {{ auth.userSub() }}
            }
          </p>
          <p class="muted">Roles: {{ auth.roles().join(', ') || 'none' }}</p>
        </div>

        <div class="card">
          <h3>Quick links</h3>
          <ul class="links">
            <li><a routerLink="/profile">My profile</a></li>
            <li><a routerLink="/establishments">My establishments</a></li>
            @if (auth.isAdmin()) {
              <li><a routerLink="/admin/review-queue">Review queue</a></li>
            }
            <li><a routerLink="/opportunities">Opportunities</a></li>
            <li><a routerLink="/offers">Offers</a></li>
            <li><a routerLink="/evaluations">Evaluations</a></li>
          </ul>
        </div>
      </div>
    </div>
  `,
  styles: [
    `
      .grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
        gap: var(--space-4);
      }
      .links {
        list-style: none;
        padding: 0;
        margin: 0;
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
      }
    `,
  ],
})
export class DashboardComponent {
  protected readonly auth = inject(AuthService);
  protected readonly route = inject(ActivatedRoute);
}

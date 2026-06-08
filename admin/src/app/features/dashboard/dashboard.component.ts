import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <div class="page">
      <header class="page-header">
        <h1>لوحة التحكم</h1>
      </header>

      <div class="grid">
        <div class="card">
          <h3>الحساب</h3>
          <p class="muted">
            @if (auth.userName(); as name) {
              {{ name }}
            } @else if (auth.userEmail(); as email) {
              {{ email }}
            } @else {
              {{ auth.userSub() }}
            }
          </p>
          <p class="muted">الأدوار: {{ auth.roles().join('، ') || 'لا يوجد' }}</p>
        </div>

        @if (auth.isAdmin()) {
          <div class="card">
            <h3>روابط سريعة</h3>
            <ul class="links">
              <li><a routerLink="/admin/review-queue">طلبات المراجعة</a></li>
              <li><a routerLink="/admin/change-requests">طلبات التعديل</a></li>
              <li><a routerLink="/admin/admins">المستخدمون</a></li>
            </ul>
          </div>
        }
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
}

import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

/**
 * Top-level shell for every authenticated admin page. Sidebar +
 * topbar + user menu + outlet.
 */
@Component({
  selector: 'app-admin-shell',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './admin-shell.component.html',
  styleUrl: './admin-shell.component.scss',
})
export class AdminShellComponent {
  protected readonly auth = inject(AuthService);

  protected readonly nav = [
    { path: '/dashboard', label: 'Dashboard', icon: '⌂' },
    { path: '/profile', label: 'My profile', icon: '◔' },
    { path: '/establishments', label: 'Establishments', icon: '◬' },
    { path: '/admin/review-queue', label: 'Review queue', icon: '⚑', adminOnly: true },
    { path: '/opportunities', label: 'Opportunities', icon: '◇' },
    { path: '/offers', label: 'Offers', icon: '◈' },
    { path: '/evaluations', label: 'Evaluations', icon: '✦' },
  ];

  protected visibleNav() {
    return this.nav.filter((item) => !item.adminOnly || this.auth.isAdmin());
  }

  protected logout() {
    this.auth.logout();
  }
}

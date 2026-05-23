import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { ProfileService } from '../../core/services/profile.service';

/**
 * Top-level shell for every authenticated admin page. Sidebar +
 * topbar + user menu + outlet. Loads init-data (profile +
 * establishment memberships) once on mount so feature pages can read
 * the cached signals instead of refetching.
 */
@Component({
  selector: 'app-admin-shell',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './admin-shell.component.html',
  styleUrl: './admin-shell.component.scss',
})
export class AdminShellComponent implements OnInit {
  protected readonly auth = inject(AuthService);
  protected readonly profileService = inject(ProfileService);

  protected readonly nav = [
    { path: '/dashboard', label: 'Dashboard', icon: '⌂' },
    { path: '/profile', label: 'My profile', icon: '◔' },
    { path: '/establishments', label: 'Establishments', icon: '◬' },
    { path: '/admin/review-queue', label: 'Review queue', icon: '⚑', adminOnly: true },
    { path: '/opportunities', label: 'Opportunities', icon: '◇' },
    { path: '/offers', label: 'Offers', icon: '◈' },
    { path: '/evaluations', label: 'Evaluations', icon: '✦' },
  ];

  ngOnInit(): void {
    if (!this.profileService.loaded()) {
      this.profileService.loadInitData().subscribe({
        // Failures here are surfaced by the ProblemDetails interceptor;
        // the shell stays rendered so the user can still navigate to
        // /auth/logout or retry.
        error: () => undefined,
      });
    }
  }

  protected visibleNav() {
    return this.nav.filter((item) => !item.adminOnly || this.auth.isAdmin());
  }

  protected logout() {
    this.auth.logout();
    this.profileService.clear();
  }

  protected onEstablishmentChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.profileService.setActiveEstablishment(value || null);
  }
}

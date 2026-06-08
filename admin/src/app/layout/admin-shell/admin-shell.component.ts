import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { ProfileService } from '../../core/services/profile.service';

/** A sidebar entry: either a direct link (has `path`) or a group (has `children`). */
interface NavItem {
  path?: string;
  label: string;
  icon: string;
  adminOnly?: boolean;
  children?: NavItem[];
}

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

  // Admin-only panel: matches the legacy Filament admin's administrative
  // scope. The establishment/operator surfaces (profile, establishments,
  // opportunities, offers, evaluations) are intentionally NOT navigable here
  // — their components remain on disk but are unrouted (see app.routes.ts).
  protected readonly nav: NavItem[] = [
    { path: '/dashboard', label: 'لوحة التحكم', icon: '⌂' },
    {
      label: 'المنشآت',
      icon: '◫',
      adminOnly: true,
      children: [
        { path: '/admin/review-queue', label: 'طلبات المراجعة', icon: '⚑' },
        { path: '/admin/change-requests', label: 'طلبات التعديل', icon: '⮂' },
      ],
    },
    { path: '/admin/individuals', label: 'الأفراد', icon: '◑', adminOnly: true },
    { path: '/admin/organizers', label: 'المنظمون', icon: '◭', adminOnly: true },
    { path: '/admin/operators', label: 'المشغلون', icon: '◰', adminOnly: true },
    { path: '/admin/contracts', label: 'العقود', icon: '▤', adminOnly: true },
    { path: '/admin/admins', label: 'المستخدمون', icon: '◔', adminOnly: true },
  ];

  /** Labels of the expanded nav groups. Groups start open so nothing hides. */
  private readonly openGroups = signal<Set<string>>(
    new Set(this.nav.filter((i) => i.children).map((i) => i.label)),
  );

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

  protected visibleNav(): NavItem[] {
    return this.nav.filter((item) => !item.adminOnly || this.auth.isAdmin());
  }

  protected isGroupOpen(label: string): boolean {
    return this.openGroups().has(label);
  }

  protected toggleGroup(label: string): void {
    this.openGroups.update((set) => {
      const next = new Set(set);
      if (next.has(label)) {
        next.delete(label);
      } else {
        next.add(label);
      }
      return next;
    });
  }

  protected logout() {
    this.auth.logout();
    this.profileService.clear();
  }
}

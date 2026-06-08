import { Routes } from '@angular/router';
import { adminGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: 'auth/login',
    loadComponent: () =>
      import('./auth/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'auth/callback',
    loadComponent: () =>
      import('./auth/callback.component').then((m) => m.CallbackComponent),
  },
  {
    path: 'auth/logout',
    loadComponent: () =>
      import('./auth/logout.component').then((m) => m.LogoutComponent),
  },
  {
    // Standalone (outside the admin shell): shown to authenticated users who
    // lack the matloob_admin role. Kept guard-free so adminGuard can redirect
    // here without re-triggering itself.
    path: 'auth/forbidden',
    loadComponent: () =>
      import('./auth/forbidden.component').then((m) => m.ForbiddenComponent),
  },
  {
    // The entire admin panel is matloob_admin-only: adminGuard protects the
    // shell, so every child route (dashboard included) requires the role.
    path: '',
    canActivate: [adminGuard],
    loadComponent: () =>
      import('./layout/admin-shell/admin-shell.component').then(
        (m) => m.AdminShellComponent,
      ),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then(
            (m) => m.DashboardComponent,
          ),
      },
      // NOTE: This is an ADMIN-ONLY panel (mirrors the legacy Filament admin's
      // administrative scope). The establishment/operator + individual surfaces
      // — profile, establishments (list/detail/members), opportunities, offers,
      // evaluations, applicants — are intentionally UNROUTED. Their components
      // remain on disk under ./features/* but are not reachable here; the
      // catch-all below redirects any stale deep-link back to the dashboard.
      {
        path: 'admin/review-queue',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/establishments/review-queue.component').then(
            (m) => m.ReviewQueueComponent,
          ),
      },
      {
        path: 'admin/review-queue/:id',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/establishments/review-detail.component').then(
            (m) => m.ReviewDetailComponent,
          ),
      },
      {
        path: 'admin/change-requests',
        canActivate: [adminGuard],
        loadComponent: () =>
          import(
            './features/establishments/change-request-queue.component'
          ).then((m) => m.ChangeRequestQueueComponent),
      },
      {
        path: 'admin/admins',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/admin-users/admin-user-list.component').then(
            (m) => m.AdminUserListComponent,
          ),
      },
      {
        path: 'admin/admins/new',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/admin-users/admin-user-create.component').then(
            (m) => m.AdminUserCreateComponent,
          ),
      },
      {
        path: 'admin/admins/:id',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/admin-users/admin-user-detail.component').then(
            (m) => m.AdminUserDetailComponent,
          ),
      },
      {
        path: 'admin/individuals',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/individuals/individuals-list.component').then(
            (m) => m.IndividualsListComponent,
          ),
      },
      {
        path: 'admin/organizers',
        canActivate: [adminGuard],
        data: {
          role: 'organizer',
          title: 'المنظمون',
          subtitle: 'المنشآت التي يمكنها إنشاء وإدارة الفعاليات.',
          emptyHeading: 'لا يوجد منظمون',
        },
        loadComponent: () =>
          import('./features/establishments-list/establishments-list.component').then(
            (m) => m.EstablishmentsListComponent,
          ),
      },
      {
        path: 'admin/operators',
        canActivate: [adminGuard],
        data: {
          role: 'operator',
          title: 'المشغلون',
          subtitle: 'المنشآت المشغّلة (الدور الافتراضي لكل منشأة).',
          emptyHeading: 'لا يوجد مشغلون',
        },
        loadComponent: () =>
          import('./features/establishments-list/establishments-list.component').then(
            (m) => m.EstablishmentsListComponent,
          ),
      },
      {
        path: 'admin/contracts',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/contracts/contracts-list.component').then(
            (m) => m.ContractsListComponent,
          ),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];

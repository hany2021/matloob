import { Routes } from '@angular/router';
import { adminGuard, authGuard } from './core/auth/auth.guard';

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
    path: '',
    canActivate: [authGuard],
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
      {
        path: 'profile',
        loadComponent: () =>
          import('./features/profile/profile.component').then(
            (m) => m.ProfileComponent,
          ),
      },
      {
        path: 'establishments',
        loadComponent: () =>
          import(
            './features/establishments/establishment-list.component'
          ).then((m) => m.EstablishmentListComponent),
      },
      {
        path: 'establishments/:id',
        loadComponent: () =>
          import(
            './features/establishments/establishment-detail.component'
          ).then((m) => m.EstablishmentDetailComponent),
      },
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
        path: 'opportunities',
        loadComponent: () =>
          import('./features/opportunities/opportunity-list.component').then(
            (m) => m.OpportunityListComponent,
          ),
      },
      {
        path: 'opportunities/new',
        loadComponent: () =>
          import('./features/opportunities/opportunity-form.component').then(
            (m) => m.OpportunityFormComponent,
          ),
      },
      {
        path: 'opportunities/:id',
        loadComponent: () =>
          import('./features/opportunities/opportunity-detail.component').then(
            (m) => m.OpportunityDetailComponent,
          ),
      },
      {
        path: 'opportunities/:id/edit',
        loadComponent: () =>
          import('./features/opportunities/opportunity-form.component').then(
            (m) => m.OpportunityFormComponent,
          ),
      },
      {
        path: 'opportunities/:id/applicants',
        loadComponent: () =>
          import('./features/applications/applicant-list.component').then(
            (m) => m.ApplicantListComponent,
          ),
      },
      {
        path: 'applicants/:applicantId',
        loadComponent: () =>
          import('./features/applications/applicant-detail.component').then(
            (m) => m.ApplicantDetailComponent,
          ),
      },
      {
        path: 'offers',
        loadComponent: () =>
          import('./features/offers/offer-list.component').then(
            (m) => m.OfferListComponent,
          ),
      },
      {
        path: 'offers/:id',
        loadComponent: () =>
          import('./features/offers/offer-detail.component').then(
            (m) => m.OfferDetailComponent,
          ),
      },
      {
        path: 'evaluations',
        loadComponent: () =>
          import('./features/evaluations/evaluation-list.component').then(
            (m) => m.EvaluationListComponent,
          ),
      },
      {
        path: 'evaluations/new',
        loadComponent: () =>
          import('./features/evaluations/evaluation-form.component').then(
            (m) => m.EvaluationFormComponent,
          ),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];

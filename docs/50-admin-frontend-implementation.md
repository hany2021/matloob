# 50 · Admin frontend — implementation snapshot

The admin app lives at `admin/`. This document describes what shipped
and what's deliberately out of scope so future work can pick up
without rereading the commit history.

## Stack

- Angular 21 (LTS), strict TypeScript, standalone components, SCSS.
- `angular-oauth2-oidc` for OIDC/IdentityServer integration.
- Signal-based reactive state; functional `HttpInterceptorFn` and
  `CanActivateFn` guards.
- Lazy-loaded routes via `loadComponent`.
- `APP_INITIALIZER` runs OIDC discovery before the first route
  activates so authenticated routes don't race the redirect.

No external state library, no NgRx, no zone.js gymnastics. Each
feature owns its own state.

## Layout

```
admin/src/app/
├─ auth/                 OIDC bootstrap + login / callback / logout pages
├─ core/
│  ├─ auth/              AuthService, authGuard, adminGuard
│  ├─ config/            APP_CONFIG view onto environment
│  ├─ http/              ApiClient, auth + ProblemDetails interceptors
│  ├─ models/            Wire-shape DTOs by domain
│  └─ services/          Feature services that wrap ApiClient
├─ features/             Page components by domain
├─ layout/admin-shell/   Shell with sidebar, topbar, establishment picker
└─ shared/components/    Reusable UI (toast, dialog, upload, empty state)
```

Routes are declared centrally in `app.routes.ts` and all lazy-loaded.

## Authentication

`AuthService` configures `OAuthService` with the values from
`environment.ts`. The shell guards every authenticated route with
`authGuard`; admin-only routes additionally use `adminGuard`. Role
detection is from the `role` claim (string or array). The `matloob_admin`
role gates the review queues, change requests, and any future
admin-only screen.

## API client

`ApiClient` is the only place that knows `APP_CONFIG.apiBaseUrl`. Every
feature service injects it and constructs paths relative to that base.

Two interceptors run on every request:

1. `authInterceptor` — attaches `Authorization: Bearer …` and a JSON
   `Accept` header for any request hitting `apiBaseUrl`.
2. `problemDetailsInterceptor` — parses RFC 7807 errors and routes
   them: `0` → "network error" toast, `401` → re-login from current
   URL, `423` → warning toast (the `establishment_suspended` lock),
   `5xx` → error toast, everything else → forwarded so the inline UI
   can handle it.

## Wire-shape conventions

The .NET backend uses **two** JSON conventions, and the admin honors
both:

- Legacy / Laravel-compat composite reads (Opportunity, Offer,
  Application, Evaluation, the `/api/establishments/me/profile` slice)
  emit **snake_case** via explicit `JsonPropertyName` attributes.
- Newer endpoints (admin establishment queue, self-list, change
  requests, members, profile, evaluations list) emit **camelCase** via
  the FastEndpoints default web policy.

DTO files mirror this — `establishment.ts` and
`admin-establishment.ts` use camelCase; `opportunity.ts`,
`offer.ts`, `application.ts`, `evaluation.ts` use snake_case. The
service layer treats them as wire shapes, not domain models, so
nothing is reshaped on read.

## Active establishment

Every opportunity / offer / evaluation endpoint is scoped to a single
establishment. The admin shell sources the active establishment from
`ProfileService.activeEstablishmentId` (signal, defaults to the user's
first active membership). When the user belongs to more than one, the
topbar surfaces a dropdown that calls `setActiveEstablishment(id)`.

The opportunity / offer / evaluation list pages all react to the
signal via `effect()` so switching establishments triggers a refetch.

## Asset uploads

`AssetService` wraps the Local Assets API (`/api/v1/assets`):

| Operation | Endpoint                                |
| ---       | ---                                     |
| Upload    | `POST /api/v1/assets` (multipart)       |
| Read      | `GET /api/v1/assets/{id}` (binary)      |
| Metadata  | `GET /api/v1/assets/{id}/metadata`      |
| Delete    | `DELETE /api/v1/assets/{id}`            |

No signed-storage-url flow. The reusable `AssetUploadComponent`
sequences multi-file uploads, surfaces per-file failures, and emits
the resulting asset id back to the caller. Parent pages link the asset
to their record (e.g. opportunity media via
`POST /opportunities/{id}/assets`).

## Feature coverage (phase-by-phase)

| Phase | Slice           | Pages                                                          |
| ----- | --------------- | -------------------------------------------------------------- |
| B     | Auth + shell    | /auth/{login,callback,logout}, dashboard                       |
| D     | Profile         | /profile (read-only)                                           |
| E     | Establishments  | /establishments, /establishments/:id, /admin/review-queue/[:id], /admin/change-requests |
| G     | Opportunities   | /opportunities, /new, /:id, /:id/edit                          |
| H     | Applications    | /opportunities/:id/applicants, /applicants/:applicantId        |
| I     | Offers          | /offers (sent/received/pending tabs), /:id, /new               |
| J     | Evaluations     | /evaluations, /new                                             |

## Deliberately out of scope

These were left out to keep the v1 admin focused on what the backend
actually supports today:

- **Profile edit endpoints** — Laravel had personal-info / education /
  experience PATCHes; those slices aren't migrated yet
  (docs/40-api-migration-readiness.md §6/§7). /profile is read-only.
- **Bulk-create opportunities** — backend supports a
  `{ event_id, opportunities: [...] }` shape; admin only exposes
  single-create.
- **Reference-data typeaheads** — category / nationality / city /
  job-title pickers accept raw GUIDs for now. The reference slice will
  surface these as dropdowns later.
- **Events** — the Events slice isn't migrated; the opportunity form
  accepts an optional `event_id` so backend defaults can apply.
- **Ajeer, contracts, invoices** — gone (docs/25-ajeer-disposition.md).
  Offer detail surfaces `accepted_at` instead of `contract.created_at`.
  No contract_id anywhere; evaluations attach to `offer_id`.
- **Notifications** — no NOTIF slice in this build. Phase NOTIF-1 is
  the next milestone.
- **Filament, Laravel admin** — explicitly not built. The admin is
  Angular-only, against the new .NET backend.

## Running locally

```bash
cd admin
npm install
npm start         # ng serve at http://localhost:4200
npm run build     # ng build, defaults to development configuration
```

Environment is configured in `src/environments/environment.ts`. The
production build uses `environment.prod.ts` whose values are
placeholders (`REPLACE_AT_DEPLOY_TIME`) — the deploy pipeline rewrites
them.

The Identity authority and API base URL default to localhost ports
that line up with running the .NET backend out of
`backend/src/Matloob.Api`.

## Build budgets

`angular.json` bumps the production budgets to 1 MB warning / 2 MB
error. Initial bundle today is ~1.5 MB raw — under both thresholds.
Lazy chunks are small (~2 kB for placeholder pages, ~20 kB for the
heavier detail pages).

# 00 · Matloob — project overview for the team lead

This document is a single-pass walkthrough of the new Matloob system for
an engineering manager or team lead picking it up cold. It covers what
exists, why it exists, how to run it locally, and what is intentionally
out of scope.

For implementation-level deep dives, see the rest of `docs/`:

- [`15-establishment-onboarding-spec.md`](15-establishment-onboarding-spec.md)
- [`20-api-compatibility-matrix.md`](20-api-compatibility-matrix.md)
- [`21-api-authorization-responses.md`](21-api-authorization-responses.md)
- [`25-ajeer-disposition.md`](25-ajeer-disposition.md)
- [`30-data-migration-plan.md`](30-data-migration-plan.md)
- [`40-api-migration-readiness.md`](40-api-migration-readiness.md)
- [`41-legacy-request-compatibility.md`](41-legacy-request-compatibility.md)
- [`50-admin-frontend-implementation.md`](50-admin-frontend-implementation.md)
- [`60-identity-setup.md`](60-identity-setup.md)

---

## 1 · Project purpose

Matloob is being rebuilt to replace the existing PHP/Laravel backoffice
(Filament) with a .NET-based system and a fresh Angular admin. The
legacy Laravel repo continues to power the production frontend during
the transition; this project is the **new** backoffice and API, kept
compatible with the public frontend where it has to be and otherwise
designed cleanly.

Concretely:

- The PHP/Laravel codebase is **reference-only**. We do not modify it.
- The new system is a .NET 10 API + Angular admin + PostgreSQL, with
  shared authentication through the existing NEC IdentityServer (Duende).
- The public frontend kept calling the same endpoints during migration;
  legacy request shapes were preserved where it was cheap to do so.
- Anything that doesn't carry forward (Qiwa, Ajeer, contracts, invoices,
  signed-storage URLs, Redis, MinIO/S3) is deliberately gone — see
  [`docs/25-ajeer-disposition.md`](25-ajeer-disposition.md).

---

## 2 · High-level architecture

```
┌──────────────────┐     1. login redirect         ┌────────────────────┐
│                  │ ────────────────────────────▶ │                    │
│   Angular Admin  │                                │   IdentityServer   │
│  (localhost:4200)│ ◀──────────────────────────── │  STS Duende        │
│                  │     2. authorization code      │ (localhost:44310)  │
└────────┬─────────┘                                └─────────┬──────────┘
         │ 3. exchange code → bearer token                    │
         │                                                    │ SQL Server
         │ 4. API calls + Authorization: Bearer …             │ (1433)
         ▼                                                    ▼
┌──────────────────┐     5. validates token         ┌────────────────────┐
│  Matloob API     │ ────────────────────────────▶ │  IdentityServer    │
│  .NET 10         │                                │  JWKS endpoint     │
│ (localhost:5180) │                                └────────────────────┘
└────────┬─────────┘
         │ 6. EF Core / Npgsql
         ▼
┌──────────────────┐
│   PostgreSQL     │           ┌────────────────────────────┐
│  matloob db      │           │  Local file storage         │
│ (localhost:54321)│           │  ./_assets (Assets API)     │
└──────────────────┘           └────────────────────────────┘
```

What is **not** in the picture (and intentionally so):

- No Qiwa integration.
- No Ajeer / contracts / invoices.
- No Redis.
- No MinIO / S3 / signed storage URLs.
- No background message broker.

---

## 3 · Repositories / paths

| Path | What it is | Modify? |
| --- | --- | --- |
| [`d:/Sure/matloob/`](.) | **The new project root.** Backend + Angular admin + docs all live here. | ✅ yes |
| [`d:/Sure/matloob/backend/`](../backend/) | The new .NET 10 API (`Matloob.Api`, `Matloob.Domain`, `Matloob.Contracts` + tests). | ✅ yes |
| [`d:/Sure/matloob/admin/`](../admin/) | The new Angular 21 admin SPA. | ✅ yes |
| `d:/Sure/eservices-backend/` | The **existing** NEC IdentityServer (Duende + Skoruba) repo. Not ours; we touched it once for a Development-only local-login bypass. | ⚠ only if explicitly required |
| `d:/Sure/EServicesPortal (1)/` | Docker compose + `.bak` backups for the IdentityServer SQL Server databases. | ⚠ rarely; backups & restore scripts only |
| (somewhere on disk) `saudi-events-master/` | The **legacy** PHP/Laravel backoffice. Reference only. | ❌ do not modify |

Everything in this overview, unless explicitly stated, lives under
`d:/Sure/matloob/`.

---

## 4 · Backend summary (`backend/`)

| Concern | Choice |
| --- | --- |
| Runtime | .NET 10 |
| HTTP layer | FastEndpoints (vertical-slice handlers) |
| Architecture | Vertical Slice — one folder per feature under `Features/`; no MediatR; cross-feature dependencies go through `Infrastructure/` |
| Persistence | EF Core 10 + Npgsql + `EFCore.NamingConventions` (snake_case) |
| Domain | `Matloob.Domain` aggregates, `BaseAuditableEntity`, soft delete via interceptor |
| Auditing | `AuditingInterceptor` writes `audit_entries`; `ICurrentUser` resolves the bearer sub claim per request |
| Errors | RFC 7807 ProblemDetails for every 4xx/5xx |
| Logging | Serilog (console + file), JSON-structured, request-scoped logs |
| Health | `/health` (liveness, untagged) + `/health/ready` (postgres + IdM JWKS) |
| Auth | `Microsoft.AspNetCore.Authentication.JwtBearer` against IdentityServer; `MatloobPolicies.User` and `MatloobPolicies.Admin` |
| Validation | FluentValidation per slice |
| Storage | Local-disk Assets API (`./_assets`); not signed-storage |
| Outbox | Transactional outbox in `outbox_events`; dispatcher background service ships when enabled |
| Background cleanup | Asset retention job (unreferenced uploads expire) |
| Docs | Swagger via FastEndpoints (Dev only) at `/swagger` |
| Migrations | EF Core. Auto-applied on startup when `Database:AutoMigrate=true` (default in Dev) |
| Seeding | `ReferenceDataSeeder` runs on Dev startup; idempotent |
| Tests | xUnit + WebApplicationFactory (env = `Testing`), 359 passing |

### Where to look first

```
backend/src/Matloob.Api/
  Program.cs                     ← composition root
  Features/                      ← one folder per slice (Opportunities, Offers, …)
  Infrastructure/
    Auth/                        ← JwtBearer + policies
    Persistence/                 ← AppDbContext, migrations, seeder
    Storage/                     ← local Assets API
    Events/                      ← outbox writer + dispatcher
  appsettings.Development.json   ← Dev config (CORS, AutoMigrate, Identity)
```

---

## 5 · Angular admin summary (`admin/`)

| Concern | Choice |
| --- | --- |
| Framework | Angular 21.2 LTS, standalone components, signal-based state |
| Strict TS | On (`strict`, `noImplicitOverride`, `strictTemplates`) |
| Routing | Lazy `loadComponent` everywhere |
| Auth | `angular-oauth2-oidc` with PKCE, code flow; bootstrap via `APP_INITIALIZER` |
| HTTP | Typed `ApiClient` over `HttpClient`; URL prefix from a single `APP_CONFIG.apiBaseUrl` |
| Interceptors | `authInterceptor` (Bearer header), `problemDetailsInterceptor` (0/401/423/5xx side-effects) |
| Error surfacing | `ApiErrorService` toasts 400/403/404/409/422 with code+detail |
| Reusable UI | toast host, confirm-dialog host, asset upload component, empty/loading state |
| API routes | Canonical `/api/v1/...` only — no legacy aliases inside the admin |

### Pages implemented

| Route | Notes |
| --- | --- |
| `/auth/{login,callback,logout}` | OIDC bootstrap |
| `/dashboard` | Landing page with quick links |
| `/profile` | Read-only — edit endpoints aren't migrated yet |
| `/establishments` and `/:id` | Self-list, composite detail, members, pending change request |
| `/admin/review-queue` and `/:id` | Pending-review queue + approve/reject/suspend/reinstate + history |
| `/admin/change-requests` | Pending change requests, approve/reject inline |
| `/opportunities`, `/new`, `/:id`, `/:id/edit`, `/:id/applicants` | Full CRUD + end + delete + asset linking |
| `/applicants/:applicantId` | Applicant detail with "Send offer" pre-fill |
| `/offers`, `/:id`, `/new` | Sent / Received / Pending-action tabs + lifecycle actions |
| `/evaluations`, `/new` | List + "Left to evaluate" panel + form |

See [`docs/50-admin-frontend-implementation.md`](50-admin-frontend-implementation.md)
for the per-phase build snapshot.

---

## 6 · IdentityServer integration

The Angular admin authenticates through the existing NEC IdentityServer
(Duende + Skoruba), source at `d:/Sure/eservices-backend/`. We don't
ship our own STS.

Flow:

1. Angular at `/auth/login` clicks **Sign in** → redirects to STS
   `/connect/authorize` with `client_id=matloob:admin-angular` and PKCE.
2. User authenticates against the local AspNet Identity DB
   (`IdentityServerAdmin` SQL Server database, restored from `.bak`).
3. STS returns an authorization code to
   `http://localhost:4200/auth/callback`.
4. Angular exchanges the code for an access token (1h lifetime) at
   `/connect/token`.
5. Angular attaches `Authorization: Bearer <token>` on every API call.
6. The Matloob API validates the token against the STS JWKS endpoint
   (`Authority` config), enforces `Audience=matloob:api`, and
   `AdminAudience=matloob:admin` on admin routes via the `Policy.Admin`
   authorization policy.

### Local dev client

| Field | Value |
| --- | --- |
| `clientId` | `matloob:admin-angular` |
| Grant type | `authorization_code` + PKCE, no client secret |
| `redirect_uri` | `http://localhost:4200/auth/callback` |
| `post_logout_redirect_uri` | `http://localhost:4200/auth/logout` |
| Allowed CORS origin | `http://localhost:4200` |
| Allowed scopes | `openid profile email roles matloob:api matloob:admin` |
| Authority | `https://localhost:44310` |

Seeded by
[`docs/setup/identity-server-matloob-angular-admin.sql`](setup/identity-server-matloob-angular-admin.sql)
(see [`docs/60-identity-setup.md`](60-identity-setup.md) for the
one-time setup).

### Development-only changes to IdentityServer

The STS upstream production flow hits an Active Directory API on a LAN
IP (`http://10.100.6.4:55200/Users`) that's unreachable from a
developer machine. To make local login work without changing
production paths, **one commit in `eservices-backend`** adds:

- A `TryDevelopmentLocalLoginAsync` short-circuit guarded by
  `IWebHostEnvironment.IsDevelopment()` that signs in against the local
  AspNet Identity DB.
- An optional `DEV_LOCAL_LOGIN_PASSWORD` env-var override that
  converges seeded password hashes on first use.
- Defensive single-decoding of the `returnUrl` (the upstream
  `Login(GET)` re-encodes it, producing a double-encoded value that
  later misroutes).
- A Dev-only switch from `userType=Individuals_Nafath` →
  `userType=Companies` so the OIDC landing page exposes a password
  field.

None of these run when `ASPNETCORE_ENVIRONMENT != Development`. They
live on a local branch in `eservices-backend` and have not been pushed.

### Local credentials

- Username: `admin@skoruba.com`
- Password: `Pa$$word123`

(Source of truth: the restored `IdentityServerAdmin.bak`. The role
assignment `matloob_admin` is applied by
`d:/Sure/eservices-backend/_tmp_matloob_register.sql`.)

---

## 7 · Business modules implemented

### A) InitData

Returns the reference-data blob the public frontend needs to bootstrap
(cities, regions, nationalities, languages, job titles, opportunity
categories, settings, translations, banks, etc.). Backed by the seeder
above. Endpoint: `GET /api/v1/init-data`.

### B) Assets

Local file storage replaces the legacy "signed storage URL" pattern.
The frontend uploads through the API, the API persists the file on
disk under `./_assets`, and writes a row in `assets` with the original
filename, content type, size, SHA-256, visibility, and purpose.

| Operation | Endpoint |
| --- | --- |
| Upload | `POST /api/v1/assets` (multipart) |
| Download | `GET /api/v1/assets/{id}` |
| Metadata | `GET /api/v1/assets/{id}/metadata` |
| Delete | `DELETE /api/v1/assets/{id}` |

`AssetPurpose` is a closed enum (`Generic`, `AuthorizationLetter`,
`CommercialRegistration`, `LegacyMedia`) — purpose-specific buckets
land if/when they're needed.

### C) Establishments

Full lifecycle from owner-created draft → admin-approved active
record:

- **Registration**: create draft, update basic info, upload
  authorization letter and commercial registration documents, submit
  for review, discard.
- **Admin review**: list pending, view detail + history, approve,
  reject (with reason), suspend (with reason), reinstate.
- **Members**: list, add, update (role + active), remove.
- **Change requests**: owner creates → updates → submits → admin
  approves / rejects.
- **Self-read**: list mine, composite detail with documents +
  members + pending change request.
- **Suspension semantics**: reads stay open, writes return `423 Locked`
  with code `establishment_suspended` (see
  [`docs/21-api-authorization-responses.md`](21-api-authorization-responses.md)).

### D) Opportunities · Applications · Offers · Evaluations

The core marketplace flow, end-to-end:

- **Opportunities**: establishment-owned CRUD, end, delete, asset
  attachments. Bulk-create is supported on the API (for legacy
  request compatibility) but only single-create is exposed in the admin.
- **Applications**: user / establishment apply; per-opportunity
  applicants list; cross-opportunity browse list.
- **Offers**: send, accept, reject, request cancellation, approve /
  reject cancellation, sponsor accept / reject. Sent / Received /
  Pending-action tabs in the admin.
- **Evaluations**: attach to `offer_id` only (Q-EVAL-1 — Contracts no
  longer exist). "Other evaluation" lookup surfaces the counterparty's
  evaluation once they've left one.

**Intentionally absent:** Ajeer (eligibility, audience), contracts
(`contract_path`, `notice_path`, `show_print_notice`), invoices,
pending-invoice, contracts-regulations, Qiwa lookups. See
[`docs/25-ajeer-disposition.md`](25-ajeer-disposition.md) for the
disposition decisions; the post-Ajeer top-level `accepted_at` field on
offers replaces the dropped nested `contract.created_at`.

---

## 8 · Legacy compatibility

Two URL families coexist for every migrated read/write:

- **Canonical** (`/api/v1/...`) — the shape future clients should
  call. The Angular admin uses these exclusively.
- **Laravel-compat** (`/api/users/...`, `/api/establishments/...`) —
  the path the public frontend (still Laravel-served) calls today.
  Same handler, same response, sometimes with shape adapters.

Legacy request adapters that earn their keep:

| Adapter | Why |
| --- | --- |
| Bulk opportunity create (`{ event_id, opportunities: [...] }`) | Public frontend posts multiple opportunities per event in one call |
| Bare establishment offer reject (no body) | Old frontend sent `POST .../reject` with no JSON; new handler tolerates that and uses an empty cancellation reason |
| Multipart establishment evaluation | Legacy evaluation form was multipart; canonical takes JSON. Handler sniffs Content-Type and routes either way |
| `X-Commissioner-UUID` header | Legacy "which establishment is me" header treated as an alias for `X-Establishment-Id` / `?establishment_id` |

See [`docs/41-legacy-request-compatibility.md`](41-legacy-request-compatibility.md)
for the per-endpoint compatibility audit (six fixes shipped end of OAO-9,
359/359 backend tests green after).

**Removed and not ported (final):** Qiwa, Ajeer eligibility, invoices,
contracts, signed-storage-url, pending-invoice, contracts-regulations.
Any inbound request that depended on these now returns 410/404 with a
clear ProblemDetails code.

---

## 9 · Currently running URLs (local dev)

| Service | URL |
| --- | --- |
| PostgreSQL (Matloob data) | `localhost:54321` |
| SQL Server (IdentityServer DBs) | `localhost:1433` |
| IdentityServer discovery | https://localhost:44310/.well-known/openid-configuration |
| IdentityServer login (direct) | https://localhost:44310/Account/LoginDb?userType=Companies |
| Matloob API health | http://localhost:5180/health |
| Matloob API readiness | http://localhost:5180/health/ready |
| Matloob API Swagger | http://localhost:5180/swagger |
| Angular admin login | http://localhost:4200/auth/login |

(STS uses a .NET dev certificate. On first run accept it once via
`dotnet dev-certs https --trust` or the browser warning.)

---

## 10 · Local run order

```powershell
# 1. Docker Desktop running.

# 2. PostgreSQL (Matloob data).
cd d:/Sure/matloob
docker compose -f docker/docker-compose.yml up -d

# 3. SQL Server (IdentityServer DBs).
cd "d:/Sure/EServicesPortal (1)"
docker compose up -d

# 4. IdentityServer STS.
$cs = 'Server=localhost,1433;Database=IdentityServerAdmin;User Id=sa;Password=EServ1ces@2025!;TrustServerCertificate=True;Encrypt=False'
foreach ($n in 'ConfigurationDbConnection','PersistedGrantDbConnection','IdentityDbConnection','AdminLogDbConnection','AdminAuditLogDbConnection','DataProtectionDbConnection') {
    [Environment]::SetEnvironmentVariable("ConnectionStrings__$n", $cs, 'Process')
}
[Environment]::SetEnvironmentVariable('ConnectionStrings__HangfireConnection', 'Server=localhost,1433;Database=NecHangfireDashboard;User Id=sa;Password=EServ1ces@2025!;TrustServerCertificate=True;Encrypt=False', 'Process')
[Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Development', 'Process')
cd d:/Sure/eservices-backend/src/NEC.IdentityServer.STS.Identity
dotnet run --no-launch-profile --urls https://localhost:44310

# 5. Matloob backend (new terminal). Auto-applies migrations + seeds reference data on boot.
cd d:/Sure/matloob/backend
dotnet run --project src/Matloob.Api

# 6. Angular admin (new terminal).
cd d:/Sure/matloob/admin
npm start

# 7. Open http://localhost:4200/auth/login → Sign in → admin@skoruba.com / Pa$$word123
```

One-time setup steps (SQL Server restore, base Matloob client seed,
Angular admin client seed, dev-cert trust) are documented in
[`docs/60-identity-setup.md`](60-identity-setup.md). They only need to
run on a fresh machine.

---

## 11 · Testing status

| Suite | Status |
| --- | --- |
| Backend (`dotnet test`) | **359 / 359 passing** |
| Backend (`dotnet build`) | Clean — 0 errors, 0 warnings |
| Admin (`npm run build`) | Clean — initial bundle ~1.5 MB, only deprecation hints for `*ngIf` / `*ngFor` (Angular 21 prefers `@if` / `@for`) |
| Admin (`npm test`) | 2 / 2 passing |
| Admin (`tsc --noEmit`) | Clean |
| Manual browser smoke test | **Next** |

### Suggested browser smoke checklist

1. Open `http://localhost:4200/auth/login` → **Sign in**.
2. Authenticate at the STS Companies tab with `admin@skoruba.com` /
   `Pa$$word123`.
3. Callback lands on `/dashboard` with the **admin** badge.
4. `/profile` — read-only account view loads.
5. `/establishments` — should be empty for the seeded admin (admin
   isn't a member of any establishment); the page should render the
   empty state, not spin.
6. `/admin/review-queue` — pending list (likely empty, that's fine).
   Click a row if one exists; exercise Approve / Reject / Suspend /
   Reinstate as data permits.
7. `/admin/change-requests` — same pattern.
8. `/opportunities`, `/offers`, `/evaluations` — should show the "No
   active establishment" empty state for the admin user (verified
   render, not spinning).
9. Create + sign in as a regular establishment owner (via the public
   frontend or by SQL-seeding a member row), then re-test 6–8.
10. Asset upload from opportunity detail.
11. Send offer from an applicant detail → exercise accept / reject /
    cancel.
12. Evaluation form from the "Left to evaluate" panel.
13. Sign out.

---

## 12 · What is still deferred

Out of scope on purpose for this slice; tracked for next sessions:

- **Profile edit / mutator APIs** — `/api/v1/profile` PATCH and the
  related personal-info / education / experience slices aren't migrated
  yet (see [`docs/40-api-migration-readiness.md`](40-api-migration-readiness.md) §6 / §7).
  Admin's `/profile` is therefore read-only.
- **Notifications** — no NOTIF-1 routes yet. Email/SMS adapters are
  not wired.
- **Events slice** — opportunities currently accept an optional
  `event_id`; the Events business module (admin UI for events, event
  type seasonality, sponsorship) isn't built.
- **Reference-data typeaheads** — category / nationality / city /
  job-title pickers in the admin accept raw GUIDs. Will become real
  dropdowns when the reference slice surfaces them.
- **Full data migration** — MySQL (legacy) → PostgreSQL (new) plan is
  written in [`docs/30-data-migration-plan.md`](30-data-migration-plan.md);
  the migrator itself hasn't been run end-to-end.
- **Production IdentityServer client** — the matloob-prod admin client
  with real redirect URIs, CORS, and SAP/Nafath flows hasn't been
  configured.
- **Deployment pipelines** — no CI/CD, no Helm/IIS/Docker-Compose for
  production. Local dev only.
- **Angular 21 control-flow migration** — `*ngIf`/`*ngFor` to
  `@if`/`@for` sweep. Build warns, doesn't fail.
- **UI polish** — empty states, loading skeletons, responsive
  layouts past the desktop default.
- **End-to-end user-acceptance testing** — not started; smoke
  checklist in §11 is the entry point.

---

## 13 · Important notes / risks

- **Dev-only IdentityServer bypass.** The `TryDevelopmentLocalLoginAsync`
  short-circuit in `eservices-backend` is guarded by
  `IWebHostEnvironment.IsDevelopment()`. It must not reach non-dev
  environments. Code review on that commit before any STS deploy.
- **`eservices-backend` is a foreign repo.** Our commits there live on
  a local branch and have not been pushed. Coordinate with the
  IdentityServer team before sharing.
- **Legacy Laravel repo is reference-only.** No edits land there.
- **Admin uses canonical `/api/v1/...` exclusively.** Don't introduce
  Laravel-compat calls in the admin; that's only for the public
  frontend.
- **No remote push has happened.** All commits (matloob + eservices-backend)
  exist only on this machine until pushed. Push policy is whatever the
  team decides; no `git push` has been run.
- **Auto-migrate in non-dev environments is OFF by default.** Set
  `Database__AutoMigrate=true` on a host explicitly if you want
  startup-time migration outside Dev. Production is expected to run
  migrations from a dedicated job before rolling out new app code
  (see comment in `Program.cs`).
- **CORS allow-list is dev-only.** `Cors:AllowedOrigins` is set in
  `appsettings.Development.json`. Production is expected to be
  same-origin or behind a reverse proxy.

---

## 14 · Summary

The new Matloob system is end-to-end runnable on a developer machine:
PostgreSQL + SQL Server (for IdentityServer) up via Docker, the
IdentityServer STS, the .NET 10 API, and the Angular admin. Login
works against the seeded admin user, the API auto-migrates and seeds
on boot, and the admin shell drives the full establishment / opportunity
/ offer / evaluation flow against canonical `/api/v1` routes. Backend
tests are green (359 / 359) and the admin build is clean.

Next step is manual browser smoke testing per §11, followed by data
migration (§12), production IdentityServer wiring, and deployment.

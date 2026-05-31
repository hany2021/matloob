# Session Resume — Matloob backoffice migration (.NET API ⇄ Next.js public frontend)

> Handoff doc to continue work in a fresh session. Last updated after Services+Products, Events, Notifications, the global { data } envelope + Laravel 422, the polymorphic `media` table, and a full frontend coverage sweep.
> **Start at §6 (CURRENT STATE & NEXT STEPS)** — it has the live status, standing conventions, and the prioritized backlog. §0–§5 are the original mission/context.

---

## 0. Mission & hard constraints

- **Goal:** run the new stack (.NET 10 API + Angular admin + Next.js public frontend) and make the **new .NET API satisfy every call the existing Next.js public frontend makes**.
- **HARD CONSTRAINT — do NOT refactor the frontend.** The frontend is the contract we protect. When something doesn't line up, change the API to match the frontend, not the other way around.
- **Dropped by design (do NOT re-implement, do NOT flag as missing):**
  - **Qiwa** integration — replaced by local establishment self-registration + admin approval.
  - **Ajeer** — all Ajeer flows, `ajeer_*` fields, invoices/billing (billing existed only for Ajeer contracts), `contracts-regulations` (its data was Qiwa saudization% + Ajeer contract%).
  - **Contracts** table — an accepted **Offer IS the contract** in the new model.
  - **SMS** — keep the abstraction (`ISmsSender`/`NoOpSmsSender`) but no real provider. Don't drop the `'sms'` channel from notification fanout.
- **EF migrations:** use `dotnet ef migrations add` ONLY. Never hand-write migrations. **Never use `--no-build`** (see Gotchas).

---

## 1. Running services & how to start them

| Service | URL | Start command |
|---|---|---|
| Postgres | `localhost:54321` db `matloob` user `matloob` pw `matloob_dev_password` | Docker (container `matloob-postgres`) — must be running |
| .NET API | `http://localhost:5180` | `cd "backend/src/Matloob.Api" && dotnet run --project Matloob.Api.csproj --urls http://localhost:5180` |
| Next.js public frontend | `http://localhost:3001` (root `/` → 307 → `/ar`, normal) | `cd repos/matloob-frontend && npm run dev -- -p 3001` |
| Angular admin | `http://localhost:4200` | `cd admin && ng serve` (not currently needed for profile work) |

Repo roots (Windows):
- .NET + Angular: `C:\National Events Center - NEC\Matloob\repos\matloob-backoffice (.net + angular)`
- Next.js frontend: `C:\National Events Center - NEC\Matloob\repos\matloob-frontend`
- OLD Laravel app (reference for contracts): `C:\National Events Center - NEC\Matloob\repos\matloob-backoffice`

---

## 2. Critical environment facts

- **IdentityServer (NEC IdM):** `http://10.100.6.4:55310` — set in API `appsettings.Development.json` (`Identity.Authority` + `Identity.Issuer`) and frontend `.env.development.local` (`NEXT_PUBLIC_IDENTITY_SERVER_AUTHORITY`). `RequireHttpsMetadata=false` in dev.
- **Frontend OAuth client:** `matloob:front-dev`, scope `openid profile matloob:api`, redirect `http://localhost:3001/`. API base `NEXT_PUBLIC_API_URL=http://localhost:5180/api/`.
- **Auth policies:** `MatloobPolicies.User` (role `matloob_user`), `MatloobPolicies.Admin` (role `matloob_admin` + aud `matloob:admin`). The test IdM account `mmegahed.c@nec.gov.sa` carries BOTH roles, so the same login can act as user (`:3001`) and admin (`:4200`).
- **Tokens expire ~60 min.** When manually curling, pull a fresh `Bearer` from the browser Network tab.

---

## 3. What's DONE this session (all verified)

1. **CORS** for `http://localhost:3001` incl. `AllowCredentials()` (frontend axios sends `withCredentials:true`, a Sanctum holdover). `Program.cs`. Verified preflight returns `Access-Control-Allow-Credentials: true`.
2. **JWT role fix** — `AuthRegistration.cs` sets `jwt.MapInboundClaims = false`. Without it, .NET remapped the `role` claim to the legacy WIF URI before authz ran, so `RequireRole("matloob_user")` 403'd despite a valid token. Verified `finish-onboarding` → 204.
3. **Notifications stub** — `Features/Notifications/` (UnreadCount / List / MarkAsRead) for both `users/*` and `establishments/*`. Returns `{count:0}` / empty Laravel-pagination envelope / `{data:null}`. The real module is unported (only the outbox dispatcher exists). Verified `{"count":0}`.
4. **Feature audit vs. the two Arabic user manuals** — implementable gaps identified vs. intentionally-dropped (Qiwa/Ajeer). Core loops (opportunity→application→offer→accept/cancel→evaluate, and establishment registration→approval) are fully built.
5. **User-profile editing slice — Phases A & B complete + Phase C implemented (needs final verify):** see §4.

The Send-Offer screen was removed from the Angular admin earlier (offer creation is frontend-only, matching the old architecture); the .NET `SendOfferEndpoint` was kept.

---

## 4. User-profile editing slice — current state

Chosen first slice (user picked it). Old Laravel contracts were extracted field-for-field; new-codebase conventions matched (DDD aggregates, EF configs, FastEndpoints slices, Asset GUID flow replacing Spatie media).

### Phase A — domain + schema ✅ DONE (migration applied to Postgres)
- **Domain** `Matloob.Domain/Users/`: entities `UserEducation`, `UserExperience`, `UserCertificate`, `UserSkill`, `UserLanguageProficiency`, `UserProfession`, `SupportiveDocument`, `BankAccount`; enums `Gender`, `EducationDegree`, `ProficiencyLevel`, `ExperienceType`; `UserProfileWire` (enum ↔ Laravel token + `*Label()`). `User` gained 11 columns/FKs (`IdNumber`,`Gender`,`Age`,`DateOfBirth`,`Bio`,`AdditionalPhone`,`YearsOfExperience`,`ProfileCompleted`,`CityId`,`RegionId`,`NationalityId`,`PhotoAssetId`) + behavior methods (`UpdatePersonalInfo`, `SetPhoto`, `SetYearsOfExperience`, `SetProfileCompleted`, `SetIdentityAttributes`).
- **Persistence** `Infrastructure/Persistence/Configurations/Users/`: `UserProfileConverters` (enums stored as Laravel tokens), 8 entity configs (tables `user_education`,`user_experiences`,`user_certificates`,`user_skills`,`user_languages`,`user_professions`,`supportive_documents`,`bank_accounts`; partial-unique indexes; Asset FKs `Restrict`). 8 DbSets in `AppDbContext`.
- **Migration** `20260530084328_UserProfileSchema` — APPLIED. (Duplicate `onboarded` column was stripped from it because the prior `UserOnboarded` migration already added it.)

### Phase B — GET /profile real projection + `{ data }` envelope ✅ DONE (359/359 tests pass)
- `Features/Common/DataEnvelope.cs` — reusable Laravel `{ "data": ... }` wrapper.
- `Features/Profile/Show/GetProfileEndpoint.cs` returns `DataEnvelope<ProfileResponse>` and delegates projection to **`Features/Profile/Show/ProfileReadMapper.cs`** (extracted so mutators can reuse it — they all return the full UserResource). Nested DTOs (Ref/AssetRef/BankAccount/Language/Skill/Education/Experience/Certificate/Profession/SupportiveDocument) live in `GetProfileEndpoint.cs`. Real `profile_complete_percentage` + `uncompleted_profile_sections` (4×25% per Laravel `UserSupport`).
- `tests/.../Profile/ProfileCompatibilityTests.cs` updated to read under `data`.

### Phase C — the 7 mutators + per-id deletes ⚠️ IMPLEMENTED, NEEDS FINAL VERIFY
Files exist and routes are registered (API boots with them); **confirm a clean build + full `dotnet test` in the new session.** Folders under `Features/Profile/`:
- `UpdatePersonalInfo/` (PATCH `personal-info` — name/email/phone/additional_phone/bio + city/region + bank-account upsert + IBAN/bank check)
- `UpdateLanguagesSkills/` (skills full-replace; languages sync)
- `UpdateExperiences/` (years_of_experience + upsert/delete-not-in-set)
- `UpdateEducation/` (additive upsert + `copy` asset)
- `UpdateCertificates/` (upsert/delete-not-in-set + `copy` asset)
- `UpdateInterest/` (professions sync + supportive documents)
- `UpdatePhoto/` (POST multipart `_method=PATCH` → photo Asset)
- `DeleteItems/` (DELETE `user-skills/{id}`, `user-experiences/{id}`, `user-certificates/{id}`)
- `Common/` helpers: `ProfileMutationSupport`, `ProfileAssetSupport`, `MultipartArrayParser` (parses Laravel `education[0][copy]` nested multipart).

**All mutators must return `DataEnvelope<ProfileResponse>` (Laravel returns the full UserResource).** Reuse `ProfileReadMapper.BuildAsync`.

### Phase D — tests + frontend smoke ✅ TESTS DONE (388/388 pass), frontend smoke ⏳ PENDING
- **Phase C re-verified** in a fresh session: clean build, all 7 mutators return `DataEnvelope<ProfileResponse>` via `ProfileReadMapper.BuildAsync`; deletes return 204 (frontend refetches).
- **29 new integration tests** added — `tests/.../Profile/`:
  - `ProfileMutatorsApiFactory.cs` — InMemory + production audit/soft-delete interceptors (so deletes hide via the global query filter) + a temp `Storage:AssetsRoot` (so photo/file uploads write to throwaway storage). Fake auth via `X-Test-User`; user row auto-provisioned by `CurrentUserSyncMiddleware` on first request; one distinct `sub` per test.
  - `ProfileMutatorsTests.cs` — happy-path (each returns `{ data: <profile> }`, GET reflects), 422 manual-check paths (IBAN/unknown city/unknown language/parent-category/invalid degree/gpa/date), precognition→204-no-mutation, bank-account upsert (stays 1 row), skills full-replace, experiences clear, certificate delete-not-in-set, photo asset URL, and the 3 per-id deletes (204 + GET reflects, 404 unknown, 401 anon).
- **Two findings worth carrying forward (NOT fixed — out of Phase D scope):**
  1. **Empty multipart body → 500.** Posting a multipart request with *zero parts* makes `ReadFormAsync` throw → 500 instead of a clean 422 (hit on photo/interest/certificates). Harmless in practice — the frontend always sends at least the spoofed `_method=patch` part, so the body is well-formed. A defensive `try/catch` around `ReadFormAsync` in the 3 multipart endpoints would harden it.
  2. **Auto-validator returns 400, not 422.** FastEndpoints' built-in FluentValidation failure path returns **400** (`AddFastEndpoints()` uses defaults), while the endpoints' *manual* checks return **422** to match Laravel. So a fully-missing-fields submit gets 400 where Laravel gave 422. Reconcile this when doing the global envelope/compat work (set `c.Errors.StatusCode = 422` in `UseFastEndpoints`, or a global config) — but verify it doesn't break other endpoints/tests first.
- **Still pending:** drive the real frontend profile page end-to-end (needs a fresh Bearer from the browser; tokens expire ~60 min).

---

## 5. ✅ DONE — systemic `{ data }` envelope shim (whole-API scope)

**Was:** the API returned **bare** bodies on every read, but the frontend is typed `ApiResponse<T> = { data: T }` / `ApiResponseWithPagination<T> = { data, meta, links }`. Every read screen except profile would have failed to parse.

**Built (user chose "wrap the ENTIRE API" for one uniform shape):**
- `Features/Common/ResponseEnvelopeShim.cs` — wraps every **2xx JSON** response on **all `/api/*` routes** (legacy + canonical `/api/v1/*` + admin): single object → `{ data }`, collection → `{ data, meta, links }` (single-page Laravel meta computed from the collection count + `?page`). Wired as the FastEndpoints global `c.Serializer.ResponseSerializer` in `Program.cs` (operates on the DTO pre-serialization — no body buffering).
- `Features/Common/IBypassEnvelope.cs` — marker for DTOs the shim must NOT touch. Applied to `DataEnvelope<T>` + the new `PaginationEnvelope` (already-enveloped), and `UnreadCountResponse` + `MarkAsReadResponse` (intentionally bare / self-enveloped). Non-2xx (ProblemDetails/errors), 204, and binary downloads pass through untouched.
- `Features/Common/Pagination.cs` — canonical `PaginationEnvelope` + `PaginationMeta`/`PaginationLinks` (exact Laravel snake_case field names: `current_page, from, last_page, links, path, per_page, to, total` / `first, last, prev, next`). Notifications feed refactored to reuse these.
- Tests: `tests/.../Common/ResponseEnvelopeTests.cs` (single→`{data}`, list→`{data,meta,links}`, unread-count stays bare, notifications not double-wrapped, 401 not wrapped) + `EnvelopeTestExtensions.cs` (`DataOf()` / `UnwrapData()`). **Full suite green: 393/393.**

**Scope decision rationale:** the Next.js frontend only calls legacy `/api/users/*` + `/api/establishments/*` (it rewrites `organizers/→establishments/` onto base `/api/`, never `/api/v1/`). Legacy-only would have sufficed for the frontend (38 test updates), but the user chose whole-API uniformity (~110 test updates) so the resource shape is identical on legacy and canonical.
**⚠️ Implication:** `/api/v1/admin/*` and all other `/api/v1/*` are now enveloped too — the **Angular admin** (when wired up) must read `response.data.data`. Non-frontend consumers of any `/api/*` route now get `{ data }`.

**Mechanics for the test migration (for future similar work):** existing tests asserting bare shapes were updated to read under `data` via `EnvelopeTestExtensions.DataOf()`; list legacy-vs-canonical parity tests compare `UnwrapData()` on both (the wrapper's `meta.path` differs by route); shared helper `Helpers.ReadIdAsync` unwraps `data` so most setup paths needed no per-test edits.

---

## 6. CURRENT STATE & NEXT STEPS (read this first)

**Branch:** `feature/api-migration-services-products` (NOT merged to main). ~33 commits, each a clean phase. **Full test suite: 432/432 green.** Run `git log --oneline` for the slice history. Dev Postgres was offline this session — migrations auto-apply on next API start (`Database:AutoMigrate=true` in Dev).

### Done this session (all committed, all tested)
- **Profile editing slice** (Phases A–D) — schema, `GET /profile` projection, the 7 mutators + per-id deletes, all `DataEnvelope<ProfileResponse>`. §4.
- **Global `{ data }` envelope shim** — `Features/Common/ResponseEnvelopeShim` wired as the FastEndpoints `ResponseSerializer`; wraps **every** 2xx `/api/*` body (`{data}` / `{data,meta,links}`). `IBypassEnvelope` opts out already-enveloped/bare DTOs. §5.
- **Laravel-style 422 validation** — `c.Errors.StatusCode=422` + `ValidationErrorResponse` → `{ message, errors:{ snake_case:[...] } }`. Business 400s pass explicit status, unaffected.
- **Services + Products** — establishment-owned CRUD (`establishments/me/services|products`), me-profile populated. Migration `…ServicesProductsSchema`.
- **Events (core)** — `Event` aggregate + `event_opportunity_category` pivot; grouped list / show / progressive multipart create-update / delete / end / joined-events / types / suggested-locations / suggested-attendees. **Event uploads** via the media table (below). Migration `…EventsSchema`.
- **Notifications (NOTIF-1)** — `notifications` table + real list/unread-count/mark-as-read; outbox fanout (`IOutboxHandler` + `NotificationOutboxHandler`: offer.accepted/rejected → establishment DB notif, offer.created → new-offer NoOp SMS). `ISmsSender`/`NoOpSmsSender`. Migration `…NotificationsSchema`.
- **Polymorphic `media` table over Asset** (Spatie-media equivalent) — `Media` (`asset_id`+`model_type`+`model_id`+`collection_name`+`order`); `MediaSupport` helper. `Asset` stays the canonical blob. **Go-forward attachment mechanism** (see Standing conventions below). Migration `…MediaSchema`.

### Standing conventions (DON'T relearn — see also memory)
- **Match legacy scope:** implement ONLY what the old Laravel project had; don't exceed it. (One pre-existing exception kept by user choice: products have update+delete though legacy didn't.)
- **Attachments:** every upload → creates an `Asset` row (bytes in `IFileStorage`) and links it. New attachments use the **`media`** table via `MediaSupport`; don't add new per-owner join tables. Existing per-owner links (`*_asset_id` FK cols, `opportunity_assets`, `evaluation_assets`) left as-is.
- **Envelope + 422** are global; new endpoints get them for free.
- **EF migrations:** `dotnet ef migrations add` only, never `--no-build`.

### 🔎 Frontend coverage sweep — remaining gaps (prioritized backlog)
Cross-referenced every Next.js call vs. implemented routes. Almost everything is covered. Outstanding, in priority order:

1. **Establishment profile editing — 5 endpoints (frontend-breaking, top priority).** The frontend PATCHes these directly but the new API only has read-only `GET me/profile`:
   - `PATCH establishments/me/profile/general-info` (multipart)
   - `PATCH establishments/me/profile/contact-info`
   - `PATCH establishments/me/profile/experience`
   - `PATCH establishments/me/profile/bank-account` — **needs backing**: no establishment bank-account record exists yet (me-profile returns `bank_account: null` placeholder).
   - `PATCH establishments/me/profile/logo` (multipart) — **needs backing**: no logo storage; wire via the asset/`media` flow (`logo: null` placeholder today).
   The `Establishment` entity already has the general-info/contact/experience fields (set at registration `basic-info`); these three are mostly just edit endpoints. Old Laravel had all five (`UpdateProfileLogoController` + general-info/contact-info/bank-account/experience controllers).
2. **`GET establishments/events/{id}/drafted`** — "open event in editable form" read; not implemented.
3. **Opportunity & event success-criteria** — `SuccessManagementCriterion` is opportunity-bound and **no write path creates criteria at all** (tables exist, read returns `[]`). Building it = the whole criteria feature (create + uploads via `success_management_criterion_assets`); event criteria additionally need an Event split-FK on the criterion.
4. **Event step-4 nested opportunities** — wizard step accepted but not persisted.
5. **Missing notification events** — emit `offer-is-active`, `opportunity fulfilled/expired`, `event started/ended` outbox events so their (already-designed) legacy notifications fire.
6. **Verify, likely dead:** `PATCH/DELETE evaluations/{id}` (backend is create/read only — confirm the frontend actually edits/deletes evals); `POST/PATCH/DELETE users/opportunities` (users don't author opportunities — almost certainly dead route constants).

### Not gaps
- **Intentionally dropped** (Qiwa/Ajeer/billing): `establishments/invoices` (+`/{id}`,`/issue`), `contracts-regulations`, `offers/pending-invoice`, `offers/ajeer/check-eligibility`.
- **Auth**: `users/auth/login`, `me/logout` → IdentityServer/OIDC, not this API.
- Confirmed covered (don't re-flag): `sent-offers`/`received-offers`/`offers/pending-action`, `opportunities/categories`, `other-evaluation` (GET), all of profile/services/products/events/notifications/OAO.

### Other wrap-up
- **Live frontend smoke-test** — needs a fresh Bearer from the browser (tokens expire ~60 min). Reads are enveloped + validation is 422, so previously-broken read screens + forms should work.
- **Consolidate the branch** — review / merge `feature/api-migration-services-products` (or open a PR) and apply migrations to dev Postgres.

---

## 7. GOTCHAS (these bit us — avoid re-learning)

- **NEVER `dotnet ef ... --no-build`.** It ran a stale assembly and corrupted migration state (phantom `PendingModelChangesWarning`, duplicate migrations). Always let it build.
- **`UserOnboarded` migration is untracked in git** and was applied last session, but its model-snapshot update was never committed — so the committed snapshot lacked `onboarded`. New migrations may try to re-add it; strip the duplicate `onboarded` add/drop if it reappears.
- **Orphaned `dotnet run` locks the DLL** → build error MSB3021/MSB3027 "file locked by Matloob.Api (PID)". Fix: `taskkill //PID <pid> //F` (PID is in the error text), then rebuild. The Task tool's IDs sometimes desync from the real OS process.
- **`Program.cs` auto-migrates on startup in Development** (`Database:AutoMigrate=true`), so `dotnet run` applies pending migrations.
- Root `http://localhost:3001/` 307-redirects to `/ar` (next-intl) — that's normal, not "down".

---

## 8. Key commands

```bash
# Build API only
cd "backend/src/Matloob.Api" && dotnet build Matloob.Api.csproj -c Debug
# Add a migration (NEVER --no-build)
cd "backend/src/Matloob.Api" && dotnet ef migrations add <Name>
# Apply migrations
cd "backend/src/Matloob.Api" && dotnet ef database update
# Full test suite
cd backend && dotnet test tests/Matloob.Api.Tests/Matloob.Api.Tests.csproj
# List all API routes (dev only)
curl -s http://localhost:5180/swagger/v1/swagger.json | grep -oE '"/api/[^"]+"' | sort -u
```

---

## 9. Reference docs in-repo
- `docs/40-api-migration-readiness.md` — the team's own migration plan/backlog (§4 pending slices, §5 dropped, §6 blockers, §7 order). Authoritative.
- `docs/20-api-compatibility-matrix.md`, `docs/25-ajeer-disposition.md`, `docs/15-establishment-onboarding-spec.md`.
- Old Laravel profile contracts were extracted from `repos/matloob-backoffice/app/Http/{Controllers,Requests,Resources}/Users/...` and `database/migrations/`.

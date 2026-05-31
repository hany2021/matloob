# Session Resume — Matloob backoffice migration (.NET API ⇄ Next.js public frontend)

> Handoff doc to continue work in a fresh session. Last updated end of the profile-editing build.
> Read this top-to-bottom before doing anything.

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

## 6. IMMEDIATE NEXT STEPS (new session)

1. ✅ **DONE — Phase C verified + Phase D tests written.** Clean build, **388/388** tests pass (was 359; +29 Phase D mutator tests). See §4 Phase D.
2. ⏳ **Smoke-test the profile mutators from the running frontend** (or curl with a fresh Bearer) — the one remaining profile-slice item. Needs a fresh `Bearer` from the browser Network tab (tokens expire ~60 min). The integration tests cover the contract, so this is now a confidence check rather than the primary verification.
3. ✅ **DONE — global envelope shim (§5), whole-API scope. Full suite 393/393.**
4. ✅ **DONE — validation errors now Laravel-style 422.** `Program.cs` `UseFastEndpoints`: `c.Errors.StatusCode = 422` + `c.Errors.ResponseBuilder = ValidationErrorResponse.FromFailures`. New `Features/Common/ValidationErrorResponse.cs` produces `{ message, errors: { snake_case_field: [...] } }` (maps FluentValidation PascalCase property names → snake_case to match the frontend's `laravel-precognition` field keys; leaves Laravel bracket keys like `education[0][degree]` untouched). Business-rule 400s are unaffected — they all pass an **explicit** status to `Send.ErrorsAsync`/`ProblemWriter`; only the auto-validator path moved 400→422 (2 tests updated). Full suite 393/393.
5. ➡️ **NEXT slices** (chosen-but-deferred): **Services+Products**, **Events**, **Notifications (real)**. (Profile was first per the user.)
6. ⏳ Frontend end-to-end smoke of the whole flow (needs a fresh Bearer; tokens expire ~60 min) — now that reads are enveloped and validation errors are 422, the previously-broken read screens + form validation should work.

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

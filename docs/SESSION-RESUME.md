# Session Resume — Matloob backoffice migration (.NET API ⇄ Next.js public frontend)

> Handoff doc to continue work in a fresh session. Last updated after the **individual establishment registration flow** (frontend) + **admin review/preview wiring** + the **admin `{ data }` envelope-unwrap fix** (see §6 top, ⭐ THIS SESSION) — on top of Services+Products, Events, Notifications, the global { data } envelope + Laravel 422, the polymorphic `media` table, and a full frontend coverage sweep.
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

**Branches (two repos, different remotes):**
- **Backoffice** (`matloob-backoffice (.net + angular)`, remote = github.com/hany2021/matloob): `feature/api-migration-services-products` **+ mirrored `dev`**, HEAD `14f544c`. The ~54 migration commits + this session's establishment-registration-type (backend) and admin review/preview wiring. **Full .NET test suite: 492/492 green** (this session's backend deltas are tiny + no migration; the new admin/frontend UI is verified live in-browser, not by the .NET suite).
- **Frontend** (`matloob-frontend`, remote = internal NEC DevOps server): `feature/individual-establishments` **+ mirrored `dev`**, HEAD `fd7eb69`.

Migrations auto-apply on API start (`Database:AutoMigrate=true` in Dev). Run `git log --oneline` per repo for the slice history.

### Live stack is currently RUNNING (manual QA in progress)
Postgres in Docker (`docker compose -f docker/docker-compose.yml up -d`, host :54321). API via `dotnet run` (:5180), Angular admin via `npm start` (:4200), Next.js public frontend via `npm run dev -- -p 3001`. **IdentityServer (NEC IdM) `http://10.100.6.4:55310`** — reachable; same IdM account logs into :3001 (user) and :4200 (admin). No app Dockerfiles for API/admin (only the frontend has one + Postgres is dockerized) — running hybrid by user choice.

### Done — individual establishment registration (frontend) + admin review/preview wiring  ⭐ THIS SESSION
End-to-end "register an establishment → admin approves → it becomes usable" cycle, **verified live in-browser** (login → fill → upload docs → submit → admin review queue → approve → status `معتمدة` + appears in the account switcher as Owner).

**Registration-type decision (important — see also memory `establishment-registration-type-canmanageevents`):** there is **NO organizer/operator concept** in either the legacy Laravel app or the new backend (legacy `establishment_type` was a hardcoded `'establishment'` string from a now-dropped Qiwa call; both schemas only ever had `is_sponsor` + `can_manage_events`). So نوع التسجيل is a **single "منظم فعاليات" checkbox mapped to `can_manage_events`**; **"مشغل" (operator) is the default for every establishment** (no separate flag). Card shows «منظم ومشغل» when true else «مشغل».

**Backend (no migration — reuses the existing `can_manage_events` column):**
- `Establishment.UpdateBasicInfo` + `UpdateBasicInfoRequest`/`UpdateBasicInfoEndpoint` accept `CanManageEvents` (so the public checkbox persists during Draft/Rejected; admins can still flip it). 
- `ListMine` (`GET /api/v1/establishments`) now also returns `canManageEvents`, `area`, `economicActivity`, `createdAt`, `updatedAt` for the registration cards.
- Self-service surface used: create draft `POST registration/drafts`→`{id}`; save `PATCH registration/{id}/basic-info`; read `GET {id}`; submit `POST registration/{id}/submit` (gate: name, CR number, laborOfficeId, sequenceNumber, city, email, phone **+ BOTH documents** + CR-uniqueness); discard `DELETE registration/{id}` (Draft-only soft-delete). Docs = 2 fixed slots (CommercialRegistration + AuthorizationLetter): upload bytes `POST /api/v1/assets` then link.

**Admin (Angular, in `admin/`):**
- **`ApiClient` now unwraps the global `{ data }` envelope** (`admin/src/app/core/http/api-client.ts`, every verb, guards 204/null). This was the bug making admin screens render empty (review queue read `.items` off `{data:{...}}`). Fixes review queue/detail, establishments, opportunities, offers, evaluations uniformly. No service double-unwraps (audited).
- **`establishment-detail` (`/establishments/:id`)** gained status-aware **Approve/Reject/Suspend/Reinstate** (admin-gated via `auth.isAdmin`) — previously those lived only on the review-queue detail page.
- **Document Preview/Download** on both `establishment-detail` and `review-detail`: establishment docs are **Private** assets (a plain link 401s), so `AssetService.downloadBlob` fetches bytes with the bearer (HttpClient + auth interceptor) → `openInNewTab` previews inline, `saveAs` downloads.

**Frontend (Next.js, `matloob-frontend` repo):** account-dropdown actions (منشآتي / تسجيل منشأة, individual-only); My Establishments list (status badges, registration type, location/CR/dates, skeletons, empty state, discard-with-confirm); full RTL registration form (single منظم فعاليات checkbox, basic/contact/location sections, document upload + in-app preview via auth blob, save-as-draft, submit); create-draft → redirect to form (uses `mutateAsync` to survive React-18 StrictMode double-mount); continue/discard drafts; toast `offset` so it clears the navbar. Reads via the `{ data }` envelope; surfaces backend 4xx (no fake success). New files under `app/[locale]/(AuthRoutes)/individual/establishments/`, `app/_components/Pages/Individual/establishments/`, `queryhooks/individual/establishments/`, `types/individual/establishments.ts`.

**Not built (no backend storage — intentionally omitted, not faked):** representative/owner section, organizer event-types, operator service-types, extra/national-address attachments.

### Done — fix(cors): expose Precognition headers cross-origin
Live bug: every laravel-precognition form (individual profile personal-info, contact-info, bank, experience, opportunity create, event wizard, evaluations) crashed with *"Did not receive a Precognition response"*. Root cause: the Precognition middleware **set** `Precognition`/`Precognition-Success` but CORS only **exposed** `Content-Disposition`, so cross-origin (:3001→:5180) the browser hid them from JS. Fix: added both to `WithExposedHeaders` in `Program.cs`. **Don't remove laravel-precognition** (≈17 components + shared form infra + axios wiring; violates the no-frontend-refactor rule) — the API speaks Precognition correctly; this was the only gap. Verified live; integration tests are same-origin so never caught it.

### Done — API-driven QA golden path (mapped to matloob-business-qa-v2.md §6)
`QaLifecycleGoldenPathTests` (Opportunities) + `QaOnboardingEventsGoldenPathTests` (Establishments) drive the hiring lifecycle through real endpoints in dependency order, labelled per TC: onboarding (E10–E13), event create (E01), opportunity-needs-event (E03), apply (E04), send/accept/cancel offer (O01–O06), evaluate (V01–V02). 15 scenarios green. **QA findings:** (a) **TC-V02** — evaluation is allowed from `Accepted` (gated on Accepted|WaitingForEvaluation|Completed), so it is NOT blocked before contract end (diverges from BR-13); (b) **TC-E02** — no operator/organizer (`CanManageEvents`) gate on event creation, only non-membership 404s; (c) admin roles/permissions/users/export (TC-M02–M05) not built in the new stack. **Correction to the status-sync note below:** evaluation does NOT depend on the sweep — it already works from `Accepted`; the sweep's value is lifecycle correctness (expiry, events ending, the proper `WaitingForEvaluation` state), not unblocking evaluation.

### Done — standalone opportunity create (multipart criteria + uploads)
The frontend's "add opportunity to an existing event" page (`dashboard/opportunities/new`) posts multipart (`event_uuid` + nested `opportunities[i][…]` + per-item criteria/uploads); `CreateOpportunityEndpoint` was JSON-only and dropped them. Added a multipart branch reusing `EventOpportunitiesSupport`/`SuccessCriteriaSupport`/`OpportunityBuildSupport`, but **adds** to the event (new `EventOpportunitiesSupport.AddAsync`, vs the wizard's `ReplaceAsync = delete + Add`); validates event ownership (422), emits `opportunity.created` per item. To serve **both** JSON (single/bulk/precognition pings) and multipart on one route, converted to `EndpointWithoutRequest` + Content-Type branching (`AllowFileUploads` 415s JSON — same fix the profile array endpoints use); JSON deserialized manually, precognition → 204. 4 tests in `MultipartOpportunityCreateTests.cs`.

### Done — time-driven status sync (#4 functional half)
The action half of the lifecycle state machine existed (accept/publish/end endpoints); the **time half** did not — statuses derived from dates never advanced because nothing fires when a date rolls over. Most importantly an Accepted offer never reached `WaitingForEvaluation`, which the evaluation endpoints require → evaluations couldn't start on their own. Built `Infrastructure/StatusSync/`: a **`StatusSyncService`** (new-system equivalent of legacy `SyncEventsStatuses`/`SyncOffersStatuses`) driven by a **`StatusSyncBackgroundService`** on a timer (opt-in `StatusSync:Enabled`, same shape as the asset-cleanup worker; idles by default). One sweep: Event Upcoming→Active / Active→Finished; Opportunity Upcoming→Active / Upcoming|Active→Finished; Offer Pending→Expired (validity passed) / Accepted→WaitingForEvaluation (job end). New intention-revealing domain transitions (`Event.MarkStarted/MarkFinished`, `Opportunity.MarkStarted/MarkFinished`, `Offer.MarkWaitingForEvaluation`) — `MarkFinished` → `Finished` (distinct from manual `Ended`, preserves planned `EndDate`). Each rule is a one-way status move → naturally idempotent. 6 unit tests (pinned clock, isolated in-memory DBs). **Notifications are the deferred FYI layer** — see backlog #2.
- Also fixed a stale test: `InitDataEndpointTests` now reads the **bare** init-data body (it's `IBypassEnvelope` for the frontend), not the `{ data }` wrapper.

### Done — backlog sweep (this turn)
- **#5 verified-dead routes** — evaluations update/delete + users/opportunities CRUD + establishments/me/opportunities update/delete confirmed uninvoked. No code (see Not-gaps).
- **#1 establishment experience STORE** — `POST me/profile/experience` + new `EstablishmentExperience` table (split category by `Type`) + `experiences[]` projection. Migration `EstablishmentExperienceSchema`. (`StoreExperience/`; route split from the PATCH years endpoint by verb.)
- **#2 event success-criteria (partial)** — `SuccessManagementCriterion` given a **split-FK** (nullable `opportunity_id` + new `event_id`; `ForOpportunity`/`ForEvent`; migration `SuccessCriterionEventOwner`). New **`SuccessCriteriaSupport`** parses the nested multipart (`…[success_criteria][i][output|success_criteria|comment]` + `[uploads][]`), validates, persists (delete-then-create, uploads via `success_management_criterion_assets`), projects the frontend shape. Wired into event step-3; projected in event read + drafted `step_three`. **Remaining:** the standalone `POST me/opportunities` is still JSON-only and drops `success_criteria` + uploads (multipart) — see backlog #1.
- **#3 event-wizard nested opportunities** — `EventOpportunitiesSupport` parses `opportunities[i][...]` (+ classification/gender sets, uploads, nested criteria), validates, replaces the event's opportunities (each event-linked, category auto-attached, uploads → `opportunity_assets`, non-vacancy criteria persisted). Shared `OpportunityBuildSupport`. Drafted `opportunities[]` now projects via `OpportunityReadMapper`.

### Done — `GET establishments/events/{id}/drafted` (open draft in the wizard form)
The frontend's `getEventForm` read. Mirrors Laravel `ShowDraftedEventController`/`DraftedEventResource`: progressive payload emitting `step_one`..`step_four` up to `steps_done` (cumulative 1..4 band) + `steps_done` + `opportunities`. `step_two.uploads` via `MediaSupport`; `ResolveForRead` (404 if not theirs). Step-three success criteria + step-four nested opportunities are unbacked → empty arrays; steps not yet reached are omitted (`JsonIgnore WhenWritingNull`). `GetDraftedEventEndpoint.cs`; 5 tests in `EventsTests.cs`.

### Done — establishment profile editing slice (all 5 endpoints, committed + tested)
The 5 `PATCH establishments/me/profile/*` endpoints the frontend called but the API lacked. Every frontend mutator ignores the response body and refetches `GET me/profile`, so each returns the full refreshed profile (`DataEnvelope<EstablishmentMeProfileResponse>`) via the new shared **`EstablishmentProfileReadMapper`**; precognition requests short-circuit to 204; all resolve via `EstablishmentResourceGuards.ResolveForWrite` (active member/admin, 423 while Suspended).
- **general-info** (multipart) / **contact-info** (JSON, cross-establishment uniqueness → 422) / **experience** (years_of_experience) — no schema; new post-approval edit methods on the `Establishment` aggregate (`EditGeneralInfo`/`EditContactInfo`/`SetYearsOfExperience`), distinct from the Draft-only `UpdateBasicInfo`.
- **bank-account** — reshaped `BankAccount` to **ownerless + shared** by both owners via owner-side FKs (`User.BankAccountId` / `Establishment.BankAccountId`, unique 1:1). Wire contract unchanged on both sides (user bank JSON byte-identical). Migration `BankAccountSharedOwnership` moves the column with a data backfill before dropping `bank_accounts.user_id`. me/profile now projects `bank_account`.
- **logo** (multipart) — Asset (Public) + polymorphic `media` table (modelType `Establishment`, collection `logo`) via `MediaSupport`, replace semantics. me/profile now projects `logo` as the asset URL. No migration.
- 17 integration tests in `EstablishmentProfileEditTests.cs`.

### Done previous session (all committed, all tested)
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

1. **Notifications layer (FYI only — the LAST remaining item; scheduler's status-sync half is DONE).** The time-driven transitions all exist (`StatusSyncService`); what's left is emitting the 5 legacy notifications as a thin layer on top. All 5 are `via:['database']` (OfferIsActive also mail+sms) — pure FYI, no business logic. Recipients (from legacy): EventStarted/Ended → the event's establishment; OpportunityExpired/Fulfilled → the opportunity's applicants; OfferIsActive → the offer's recipient. Plan: emit an outbox event at each `StatusSyncService` transition (+ `OpportunityFulfilled` is event-driven, fired on offer-accept when the last slot fills, NOT time-based) → add `case`s in `NotificationOutboxHandler` with recipient fan-out. New event-type constants needed: `EventEventTypes` (new), `OpportunityEventTypes.Expired`, `offer.is_active`. **`OfferIsActive` is the one spot needing a marker** (an accepted offer has no distinct "active" status, so a time-scan would re-fire) → **open decision**: a new offer status (`Active`) vs a `notified_at` column + small migration. Everything else is one-shot via the status transition.

### Not gaps
- **Intentionally dropped** (Qiwa/Ajeer/billing): `establishments/invoices` (+`/{id}`,`/issue`), `contracts-regulations`, `offers/pending-invoice`, `offers/ajeer/check-eligibility`.
- **Auth**: `users/auth/login`, `me/logout` → IdentityServer/OIDC, not this API.
- **Verified dead route constants (no frontend invocation, confirmed this session):** `PATCH/DELETE evaluations/{id}` (frontend only calls `evaluations.create` + list/show); `POST/PATCH/DELETE users/opportunities` (users never author opportunities); `PATCH/DELETE establishments/me/opportunities/{id}` (frontend uses only `create` + `end`, both implemented in `Features/Opportunities/Mine/`).
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

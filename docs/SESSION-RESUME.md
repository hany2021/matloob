# Session Resume — Matloob backoffice migration (.NET API ⇄ Next.js public frontend)

> Handoff doc to continue work in a fresh session. Last updated after making the **organizer→applicant→offer→contract hiring flow functional end-to-end** (live-verified; see §6 top ⭐ THIS SESSION) — fixing **13 backend bugs** in one commit `c4c05ad` (My-Opportunities grouped shape + event + applicants; browse-single 404 relaxation; full applicant-profile projection; send-offer UTC/Precognition/profession-FK/salary; offer status wire for accept-reject + nested event; offer-list status/date/city filters). **All API-side — the frontend was untouched.** On top of: the event-creation wizard (prior session), individual establishment registration + admin review/preview, Services+Products, Events, Notifications, the global { data } envelope + Laravel 422, the polymorphic `media` table. **Suite 512/512.** Both repos' `dev` are **pushed and in sync** with their remotes.
> **Latest session (HEAD `e7d5cdd` + this doc):** closed the three remaining code-backlog items — the **`event: null` hydration sweep** (18 read paths now hydrate the owning event via the shared sidecar), **offer-list name search** (`sender_name` / `applicant_name`), and the **Notifications FYI layer** (the LAST migration item — all 5 legacy DB notifications, no schema change). Suite **512 → 522**. Still pending: drive accept/reject/evaluate **live in-browser** (was blocked by IdM reachability — see §2; now reachable).
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
- **Backoffice** (`matloob-backoffice (.net + angular)`, remote = github.com/hany2021/matloob): `dev`, HEAD `e7d5cdd` (backlog close-out) + this doc commit — **pushed to `origin/dev` (in sync).** **Full .NET test suite: 522/522 green.**
- **Frontend** (`matloob-frontend`, remote name **`github`** = github.com/EventsCenter/matloob-frontend — NOT `origin`; push with `git push github dev`): `dev`, HEAD `72d6fee` — pushed (in sync). One frontend change this session: the evaluation-endpoint fix (`72d6fee`, see live-QA notes below).

Migrations auto-apply on API start (`Database:AutoMigrate=true` in Dev). Run `git log --oneline` per repo for the slice history.

### ⭐ THIS SESSION — closed the 3 remaining code-backlog items (`e7d5cdd`, suite 512→522)
All API-side, no migrations, no frontend changes. One commit `e7d5cdd`.
- **`event: null` hydration sweep.** The owning event is now hydrated **once** in the shared `OpportunityReadQueries.LoadSidecarAsync` (added `Event` to `OpportunityReadBundle`); **18 read paths** that route through it now pass `bundle.Event` instead of emitting `event: null` (user/establishment browse, applications, evaluations, my-opportunities, drafted-event projection, create/update/end write-returns). Converged the 3 sites that loaded events their own way (`OfferReadMapper`, browse-single, `ListMineOpportunities` — dropped its batch dict) onto `bundle.Event`. *Trade-off:* the sidecar is already N+1, so this is +1 event query per opportunity (lists capped 200/500). Regression test `Single_PopulatesOwningEvent`.
- **Offer-list name search.** `sender_name` (received → sender establishment) + `applicant_name` (sent → applicant user OR establishment) added to `OfferListFilterSupport.ApplyFilters`, matching legacy `bySenderName`/`byApplicantName`. Case-insensitive `ToLower().Contains` (InMemory can't translate `ILike`). 2 tests (match + non-match each side).
- **Notifications FYI layer — the LAST migration item.** Emits the 5 legacy DB notifications, **no schema change**: `event.started`/`event.finished` + `opportunity.expired` from `StatusSyncService` transitions (it now injects `IOutboxWriter`, flushes before SaveChanges); `offer.is_active` + `opportunity.fulfilled` **event-driven at accept** in both accept endpoints (`OfferLifecycleQueries.EnqueuePostAcceptEventsAsync`). Fulfilled threshold reproduces legacy `reachedRequiredPersonnel` (count offers in `OfferStatusSets.Active = {Accepted, CancellationRequested, PendingSponsorCancellationApproval}` ≥ `RequiredPersonnel`); fan-out goes to **unserved** applicants only (the hired one holds an active offer → excluded). New constants `EventEventTypes`, `OpportunityEventTypes.Expired`, `OfferEventTypes.IsActive`; 5 new `NotificationOutboxHandler` cases; messages use the legacy `en` strings verbatim. Tests: 2 StatusSync emission + 5 fanout integration (via the real dispatcher).
  - **⭐ Resolved the open `OfferIsActive` marker decision:** legacy fired it once on contract-print (NOT a time-scan), and contracts are dropped (accept = active), so it fires **event-driven at accept** — **no new `Active` status, no `notified_at` column, no migration.** (Re-fires only on over-fill, which matches legacy `checkIfOpportunityIsFulfilled`.)

### ⭐ Prior session — organizer→applicant→offer→contract flow made functional (live-verified, `c4c05ad` pushed)
Drove the hiring lifecycle live in-browser as the multi-establishment account (Rotana org + OwnerProjectManager individual) and fixed **13 backend bugs** — all API-side, frontend untouched, all live-verified. One commit `c4c05ad` (backoffice `dev`, pushed to origin). Suite **504 → 512** (+8 tests). Each fix:
- **My Opportunities list** (`establishments/me/opportunities`) — returned a flat array → frontend rendered index-keyed "untitled" tabs with no rows. Now the grouped double-nested shape `data[status][status].{data,status_label,…}` (mirrors the events-list fix) via new `GroupedOpportunitiesResponse`; + **`event`** population (cards deref `opportunity.event.name`); + **`applicants`** array (cards read `applicants.length`, NOT `applicants_count`).
- **Browse-single opportunity** (`establishments/opportunities/{id}`) — 404'd own / non-browsable / for_vacancy opportunities; legacy `show` is a plain route-model-bind. Dropped all 3 guards (404 only when missing) so the organizer detail page opens its own opportunity; + `event`.
- **Applicant profile under an opportunity** (`me/applicants/{id}`) — applier was an `{id,name,email}` stub → N/A everywhere. Now the full `UserResource` / establishment profile via `ProfileReadMapper` / `EstablishmentProfileReadMapper` (`ApplicationReadMapper`; affects all 6 application-read endpoints).
- **Send offer** (`offers/send`) — four stacked failures: (1) `offer_validity` DateTimeOffset carried +03:00 → Npgsql `timestamptz` is UTC-only → `.ToUniversalTime()`; (2) no Precognition short-circuit → step-1 "next" persisted a real offer then 409'd → `204` validate-only guard; (3) profession FK — the المهنة select sends an **opportunity-category id** in `job_title_id` (the ajeer→job_titles branch is dead), so route it to `job_title_category_id` (its real FK), validate (422), project as `job_title.title`; (4) compute `monthly_salary` + `number_of_working_days` for vacancy offers = `daily_wage × inclusive-day-count` (matches the wizard `diff(end,start)+1`; legacy used exclusive `diffInDays`, so wizard 900 vs detail 750 — aligned to the frontend per user "no preference").
- **Offer read** — status was PascalCase `ToString()` ("Pending") but the frontend keys `statusConfigs[offer.status]` lowercase → empty status card, **no accept/reject buttons**. Added `OfferStatusWire.ToWire()` (lowercase snake_case, legacy values) + `TryParse`. Also hydrate the nested-opportunity `event` (contract detail reads `opportunity.event.name`).
- **Sent/received offer lists** — ignored every query filter ("التصفية لا تعمل" — the حالة العقد checkboxes did nothing). New `OfferListFilterSupport.ApplyFilters` applies `status[]` / `offer_creation_date` / `city[]` (legacy `OfferQueryBuilder` parity; handles both `key=` and bracketed `key[]=`).

**Note — organizer offers UX (not a bug):** the العقود page defaults to the **شركات** tab (org-applicant offers, usually empty); individual-applicant offers live under the **أفراد** card. Rejected offers appear there too, filterable via the **مرفوض** status checkbox.

### Done previous session — event-creation wizard made fully functional (create → opportunity → publish), live-verified
The Next.js event wizard never worked end-to-end against the new API. Found + fixed a stack of layered bugs (each its own entry below); the full **create event → add opportunity → publish** flow is now verified live in-browser (published event reached `status: active`). Batch of commits — **backoffice** (`28a2b74` JSON accept · `5a4d3a3` upsert-on-id · `8494756` indexed 422 keys · `11ad6e8` grouped-list shape + Precognition-Validate-Only · `60f3508` opportunity-500 dup-tracking) and **frontend** (`623d7df` X-Commissioner-UUID · `0803023` global 422 toast · `254a834` scoped opportunity validate · `ce2ee2d` TextArea loop/data-loss · `f719ca5` opportunity modal-close). Suite **504/504**. **Still open:** push these `dev` commits to both remotes (user has been keeping them local); a fresh frontend smoke with a non-expired Bearer.

### Done — multi-establishment context fix + event-wizard JSON fix (THIS SESSION, live-verified)
Two live bugs found while QA-ing as a **multi-establishment** account (`OwnerProjectManager`, owns "Rotana" + "منشأة الاختبار"). Both verified in-browser end-to-end.

**1. Frontend — `X-Commissioner-UUID` dropped on precognition forms (`matloob-frontend`, `services/api.ts`).**
Multi-establishment profile saves (bank-account, contact-info, experience) 400'd with `establishment_context_required`. Root cause: `attachToken()` decided "is this an establishment API call?" with `/\/(establishments|organizers|operators)\//` — which requires a **leading slash**. Read calls (`getProfile`, …) build **absolute** URLs via `getEndpoint()` (`…/api/organizers/me/profile`) so the regex matched and the header was attached; but laravel-precognition `useForm` posts **relative** URLs (`organizers/me/profile/bank-account`, no leading slash), so the regex missed them and the `X-Commissioner-UUID` header was never sent → the API (which has the user in 2 establishments) couldn't resolve context → 400. Single-establishment users were saved by the API's auto-pick fallback, which is why earlier live passes missed it. **Fix:** anchor the regex on start-or-slash — `/(^|\/)(establishments|organizers|operators)\//` — so it matches relative URLs too. One-line change; fixes every precognition form at once. The API side already accepts `X-Commissioner-UUID` (it's priority 3 in `EstablishmentContextResolver`, treated as an establishment-id alias). Verified live: bank-account PATCH `400 → 200`, header now `f5c538ab…` (Rotana).

**2b. Backend — event create POSTed a NEW draft on every wizard step (`CreateEventEndpoint`, found completing the wizard review).** The wizard **never PATCHes** — it always POSTs to `establishments/events` via the precognition `useForm('post', …)`, echoing the accumulated draft's `id` in the body from step two onward, and expects an **upsert** (legacy `EventService` ctor: `Event::whereUuid($data['id'])->first()` then update-or-create). `CreateEventEndpoint` ignored the body `id` and always `new Event(...)` → every step spawned a duplicate draft (the source of the stray "untitled" events). **Fix:** read the top-level `id` (`EventWriteSupport.EventId`), and if an event with that id exists **for the caller's establishment**, update it in place (200) instead of creating (201); unknown/foreign id falls through to create-new (matches legacy's null→create, and the establishment scope prevents cross-tenant hijack). +1 test (`CreateEvent_Json_WithExistingId_UpdatesInPlace_NoDuplicate`). **Suite 500/500.** Verified live: step 1 POST→201 (`5910761f`), step 2 POST→**200 same id**, step 3 POST→**200 same id** — one event across all steps, no duplicates.

**⚠️ Known frontend issue (NOT fixed — out of no-refactor scope) — `PrecognitionControlled/TextArea.tsx` "Maximum update depth exceeded" render loop.** Reproducible on the wizard steps that use that TextArea (step 1 description, step 3 success-criteria): submitting floods the console with a tight setState loop (`form.setData` at TextArea.tsx:25) and **breaks the forward step transition** — the API call still succeeds (200) but the UI stays on the step. Workaround used to finish the review: reopen the draft (`events/new/{id}`), which starts the stepper at `steps_done` (step 4), bypassing the step-3 TextArea. Backend coverage for steps 4 (categories) + 5 (publish) is via integration tests since the UI can't be driven cleanly past step 3.

**Investigation (two minimal fixes tried live, BOTH failed — reverted, frontend left untouched):**
- *No-op guard* in `PrecognitionControlled/TextArea.tsx` (`if (val === get(form.data, name)) return;` before `setData`) — **did not fix**. The overlay still fired on `setData`, i.e. the guard's equality check was false: `val !== get(form.data, name)` **every cycle**. So it is **not** a no-op repeat — it's an **oscillation between two different values**: `setData(name, A)` then `get(form.data, name)` reads back `B≠A`, the controlled `<textarea>` re-emits `A`, `setData(A)`, … A round-trip mismatch between laravel-precognition's `setData` and lodash `get` on nested-array paths (`step_three.success_criteria.0.success_criteria`).
- *Stabilizing `StepperProvider`'s `onStepChange`* via `useCallback` in `CreateEventPage` (the effect `useEffect(…, [activeStep, onStepChange])` re-fires on the inline-arrow's new identity each render) — **also did not fix**; the page still hung in the loop (JS thread pegged, tab never reached `document_idle`).
- **Corrected root cause (after reading `laravel-precognition-react` source):** it is **NOT** a data-path bug. `form.setData(key, value)` uses lodash `set` (nested) and keeps `payload.current` in sync with the `data` useState; `form.submit()` sends `payload.current` and does **not** mutate `form.data`. So the read/write round-trips correctly. The guard's `val !== get(form.data, name)` happens because the loop is a **React controlled-input reconciliation** issue: during the submit's synchronous multi-setState burst (setErrors/setValid/setActiveStep/setData('id')…), the controlled `<textarea value={get(form.data,name)}>` has its `onChange` re-fired with the DOM value (≠ the controlled value), each firing `setData` → re-render → repeat. The base `TextField` passes the **event** to onChange; the base `TextArea` passes only the **value**, so a controlled TextArea can't cheaply distinguish a genuine user edit from React's replay. A real fix means reworking the controlled TextArea (uncontrolled/`defaultValue`, debounced setData, or event-based onChange so `event.isTrusted` can gate it) — actual frontend refactoring, deferred to the frontend owner per the hard no-refactor constraint. Documented; not attempted further.

**2. Backend — event create/update 415'd the wizard's JSON (`CreateEventEndpoint` + `UpdateEventEndpoint`).**
The event wizard's first "التالي" died with **415**. Root cause: both endpoints called `AllowFileUploads()`, which restricts them to `multipart/form-data` and 415s anything else. The wizard's laravel-precognition `useForm` posts **`application/json`** for file-less steps (step one) and validation pings, switching to multipart only when a step actually carries `File`s. So step 1 + every ping 415'd, blocking event creation for **all** users. **Fix (mirrors `CreateOpportunityEndpoint`'s Content-Type branching):** new **`EventFormReader`** reads the request as one `IFormCollection` — multipart verbatim via `ReadFormAsync`, or JSON flattened into the same Laravel bracket keys `EventWriteSupport`/`SuccessCriteriaSupport`/`EventOpportunitiesSupport` already parse (`step_one[name]`, `step_four[opportunities_categories][0]`, `opportunities[0][name]`; `null` steps → no keys so `HasStep` stays false; malformed JSON → 400). Dropped `AllowFileUploads()` on both endpoints; single validate/apply path preserved. **+7 integration tests** in `EventsTests.cs` (JSON: step-1→201, precognition-ping→204-no-event, missing-type→422, malformed→400, step-2 schedule, step-4 categories/array-flatten, multi-step+publish→upcoming). **Full suite 499/499** (was 492). Verified live: step-1 JSON `415 → 201` (draft created, `steps_done:1`), wizard advances to step 2; maps render (the pulled Google Maps key).

### Done — success-criteria 422 visibility (indexed error keys + global 422 toast)
The success-criteria wizard step (`معايير الفعالية`) 422'd **silently** — legitimately (legacy minimums: `output` ≥10, `success_criteria` ≥30, `comment` ≥30 if filled; the frontend doesn't enforce mins client-side), but the user never saw why. Two reasons + two fixes:
- **API — non-indexed error keys (`SuccessCriteriaSupport.Validate`).** It emitted `step_three.success_criteria.output`, but the frontend field is `step_three.success_criteria.0.output` (indexed; legacy Laravel `*` expands to the index), so `get(form.errors, name)` missed it → no inline error. **Fix:** emit `…success_criteria.{index}.output` (added `Index` to `CriterionInput`, set in `Parse`, used in `Validate`). Covers both `step_three.success_criteria.*` and `opportunities.{i}.success_criteria.*`. +1 test asserting the indexed key. **Suite 501/501.**
- **Frontend — no global 422 surface (`services/api.ts`).** The global interceptors only handled 421; the event wizard's `.catch` even suppresses the 422 toast (relies on inline errors), and `getStepWithErrors` (utils.ts) doesn't include `step_three`. So a 422 there was fully silent. **Fix:** `notify422()` on both axios clients toasts the returned `message`/first error on real-submit 422s; **skips laravel-precognition validation pings** (they carry the `Precognition` header and drive inline errors), so no per-keystroke noise. Backend test-verified; live toast not yet re-confirmed (session dropped out of establishment mode + the step-3 TextArea loop blocks clean UI drive).

### Note — "adding one opportunity posts all event data" (step 4) is laravel-precognition, not a bug
`FourthStep.onOpportunitySubmit` calls `form.validate(undefined)` (validate **all** fields) when you finish an opportunity in the modal. laravel-precognition sends the **whole** `form.data` (every step) on any validate/submit — so the request carries all event data. It's a **validate-only ping** (204/422, `Precognition` header, no persistence), and the real save is a single upsert on Next/Publish — no duplication. **Real side-effect to watch:** because it validates everything, an unrelated invalid step (e.g. a short success-criterion) makes the modal's `onSuccess` not fire, so a valid opportunity can't be added until the other step is fixed. A clean fix (scope to `opportunities.{idx}` + have the API respect `Precognition-Validate-Only`) is deferred — flagged for decision.

### Done — saved events not shown in the events screen (grouped-shape mismatch) + opportunity validate coupling
- **Events list returned the wrong shape (`ListEventsEndpoint` / `GroupedEventsResponse`).** The new API returned **bare arrays** (`data.drafted = [...]`), but the frontend (`dashboard/events/page.tsx`, faithful to legacy `GroupedEventResource`) reads **`data[status][status].{data,status_label,status_icon,card_type}`** — double-nested. So `data.drafted.drafted` was `undefined` → tabs rendered as **"untitled" with no events** (events appeared lost). **Fix:** reshape to the legacy double-nested form — added `EventGroup { status_label, status_icon, card_type, data }`, and `GroupedEventsResponse` now holds 4 single-entry dicts (`{ "drafted": { "drafted": EventGroup } }`, etc.) built in `ListEventsEndpoint` with per-status labels. **Verified live:** the Drafted tab now lists all saved drafts with names + progress; tab titles read Active/Upcoming/Drafted/Ended (no "untitled"). Tests updated to the nested shape.
- **Opportunity step validated the whole form (cross-step coupling).** `FourthStep.onOpportunitySubmit` called `form.validate(undefined)` (validate-all) → an unrelated invalid step (a short success-criterion) returned 422 → the modal's `onSuccess` never fired → a **valid** opportunity couldn't be added. **Fix (two-sided):** frontend now `form.validate(`opportunities.${idx}`)` (scoped), and the API honours **`Precognition-Validate-Only`** — new `EventWriteSupport.FilterToValidateOnly` keeps only errors under the requested field(s) (bracket→dot normalized), applied in both event endpoints. So validating one opportunity no longer fails on other steps. (The request still carries the full payload — that's laravel-precognition's design — but it's a validate-only ping, no persistence.) +3 tests. **Suite 503/503.**

### Done — 500 when adding an opportunity (duplicate EventOpportunityCategory tracking)
Adding ANY opportunity in the wizard 500'd: `InvalidOperationException: The instance of entity type 'EventOpportunityCategory' cannot be tracked because another instance with the same key {EventId, OpportunityCategoryId} is already being tracked.` Root cause: in one request, **`step_four` apply** adds the category pivot AND the **nested-opportunity `AddAsync`** adds the SAME pivot — because `AddAsync`'s DB query doesn't see `step_four`'s pending (unsaved) add. Plus `step_four` did remove-all-then-add-all, so re-adding a still-tracked (Deleted) composite key also threw on re-submit. **Fixes:** (1) `EventWriteSupport` step_four now **diffs** (remove de-selected, add only new) instead of remove-all/add-all; (2) `EventOpportunitiesSupport.AddAsync` builds its existing-category set from **both persisted rows and the change tracker's `.Local`** (excluding Deleted), so it won't re-add a pivot `step_four` already staged. +1 regression test (step_four category + opportunity of the same category → 201, not 500). **Suite 504/504.** (The empty/real file upload was a red herring — `ProfileAssetSupport.SaveAsync` handles 0-byte files fine; the 500 fired with or without a file.)

### Pulled from `dev` — teammate commits (2026-06-03, `hnibrahim@jahez.net`)
Both repos fast-forwarded to the HEADs above. Two substantive commits came in:
- **Backoffice `a2f9bab` — `fix(establishments): compute real profile completion percentage`.** The establishment `me/profile` screen always showed **0%** because `ProfileCompletePercentage` was hard-coded to `0` in `EstablishmentProfileReadMapper`. Now computed faithful to legacy `EstablishmentSupport::getEstablishmentProfileCompletePercentage` — **5 sections × 20%**: (1) ≥1 service OR product, (2) general info populated (economic activity/area/description/city), (3) contact info complete (phone AND additional contact number AND email), (4) bank account linked, (5) years of experience set. Legacy `establishment_profiles`/`contact_infos` were collapsed onto the `Establishment` row, so "relation exists" became field-populated checks. `MeProfileCompatibilityTests` updated `0 → 20` (freshly approved est. has only general-info/city filled). No migration.
- **Frontend `778cc14` — `chore(env): add Google Maps JS API key for dev`.** Adds `NEXT_PUBLIC_GOOGLE_MAPS_API_KEY` to `.env.development` so the location-picker maps (registration/opportunity/event forms) render. Note: committed `.env.development` points `NEXT_PUBLIC_API_URL` at staging (`https://enjzfe-stg1/`); local `.env.development.local` overrides it back to `http://localhost:5180/api/` (Next.js per-key precedence), and the maps key flows through since `.local` doesn't redefine it.

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
Cross-referenced every Next.js call vs. implemented routes. **All code-backlog items are now DONE** (`e7d5cdd`). The only outstanding work is **live in-browser QA** of the accept/reject/evaluate flow (was blocked on IdM reachability; now reachable — see §2 + the auto-memory `idm-requires-nec-network`). Drive applicant-accept → `WaitingForEvaluation` → evaluation, plus reject, end-to-end.

1. ✅ **DONE — Notifications FYI layer** (the last remaining item). All 5 legacy DB notifications emitted with no schema change — see the ⭐ THIS SESSION entry above. The open `OfferIsActive` marker decision was resolved by firing it event-driven at accept (no migration).

**Live QA done this session (in-browser, logged in as OwnerProjectManager individual):**
- ✅ **Accept flow driven end-to-end.** Reset the live test offer `0129bcef` to Pending, opened its contract detail (renders correctly: event name in title via the hydration sweep, "بانتظار الرد" pending-status card, salary 900 SAR, both parties), clicked قبول العرض → confirm → offer went **Accepted**, and the new accept-time emissions fired in `outbox_events` at the same timestamp: **`offer.is_active` + `opportunity.fulfilled`** (alongside `offer.accepted`). The accepted detail then renders "الطلب مقبول" + إلغاء العقد. **Fixture restored to Pending afterward.**
- ✅ **Reject flow driven end-to-end.** Opened the Pending offer detail → رفض → selected a reason ("العرض الوظيفي غير مناسب") → رفض العرض → offer went **Rejected** with `offer_rejection_reason_id` set + `offer.rejected` emitted. **Fixture restored to Pending afterward.**
- ✅ **Evaluation flow driven end-to-end (form bug found AND fixed).** Set the offer to WaitingForEvaluation (opportunity Finished); the contract detail showed the **نموذج التقييم** form (satisfaction rating, "work again?", comment, Matloob-platform rating, confirm). On first submit it **POSTed to `/api/establishments/evaluations` → 400 `establishment_context_required`** — a frontend bug: the shared `EvaluationModal` hardcoded the establishments endpoint, so an *individual* evaluator who is also a member of ≥2 establishments couldn't submit. **Fixed in the frontend** (`matloob-frontend` `dev`, commit `72d6fee`): `EvaluationModal` now picks the endpoint by active account context — `user.userType === Individual → users/evaluations`, else establishments (mirrors the existing `ListOffersToEvaluate` pattern; the API derives the evaluable establishment from the offer, so the payload is unchanged). **Re-verified live: POST `/api/users/evaluations` → 201, evaluation row created** with `evaluator_user_id` (the worker) + `evaluable_establishment_id` (Rotana). The evaluation READ path was already fine (event-hydrated 200).
- 📌 **Finding (config, not code): the OutboxDispatcher + StatusSync background services are NOT enabled in the local dev API run** — emitted outbox rows (incl. older `offer.created`/`offer.rejected`) sit unprocessed, so notifications don't auto-materialize live. The handler logic that turns these events into `notifications` rows IS covered by the passing integration tests (they drive the dispatcher directly). To exercise it live, enable the dispatcher/StatusSync workers (the `*:Enabled` opt-ins).
- ⚠️ **Frontend token-renewal flakiness:** the `/individual/contracts/unevaluated` LIST page intermittently sent a malformed/empty bearer (`JWT not well formed, no dots` → 401 → "حدث خطأ ما!") even with a live session; the contract *detail* route loaded fine. Likely a query firing before the OIDC token is hydrated/renewed. Cosmetic-ish but worth a frontend look.
- 🌐 **IdM `10.100.6.4:55310` is reachable again** (was timing out at session start — first call cold-starts slowly; see auto-memory `idm-requires-nec-network`).

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

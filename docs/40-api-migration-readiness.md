# API migration readiness — pre-flight audit

**Phase 0.5 deliverable.** Snapshot of what's done before the old Laravel API migration sprint starts. Read this together with [20-api-compatibility-matrix.md](20-api-compatibility-matrix.md): that file is the *route-by-route plan*; this file is the *am-I-ready-to-start* checklist.

> Status as of 2026-05-22. Branch `main`, last commit `12b9329`. Tests: 161 passing, 0 failing.

---

## 1. Foundations now complete

| Layer | What's in place | Why it matters for the migration |
|---|---|---|
| **Solution skeleton** | .NET 10, FastEndpoints, EF Core 10 + Npgsql, snake_case naming convention. `Matloob.Api`, `Matloob.Domain`, `Matloob.Contracts`, `Matloob.Api.Tests`. | Every new endpoint slots into the slice pattern under `Features/<area>/<endpoint>/`. |
| **Persistence** | `AppDbContext` with `SoftDeleteInterceptor` + `AuditingInterceptor`. `BaseEntity` (outbox + history rows) vs `BaseAuditableEntity` (aggregates) split. Postgres migrations in `Infrastructure/Persistence/Migrations`. | New aggregates (Opportunity, Applicant, Offer, Evaluation, Notification) follow the same configuration pattern. |
| **Auth** | OIDC JWT bearer (IdentityServer). `[Authorize(Policy = Policy.User)]` / `Policy.Admin`. `ICurrentUser` reads sub/roles/audiences from the principal. | Every migrated route can drop in the right policy without bespoke middleware. |
| **Local users + sync** | `users` table keyed by `IdentityId` (sub claim). `CurrentUserSyncMiddleware` upserts the row on every authenticated request (best-effort, never blocks the handler). | Foreign keys to `users.identity_id` are safe — once a user has logged in once, their row exists. Membership add enforces this with 422 `user_not_found_in_system`. |
| **Init-data** | `GET /api/v1/init-data` with 5-min `IMemoryCache`. Field-for-field parity with Laravel `InitDataController`. | The public frontend's bootstrap call works against the new API today. |
| **Assets (file storage)** | `POST /api/v1/assets` multipart → GUID. Local-disk driver (`LocalFileStorage`); MinIO/S3 deferred. Download/delete/metadata endpoints implemented. `Asset.OwnerUserId` for ownership checks. | Any migrated endpoint that needs file upload (photo, logo, certificates) wires through the same Asset GUID flow. |
| **Establishment onboarding** | Draft → submit → admin approve / reject → suspend / reinstate. Change-requests with per-field approval. Members add / list / update / remove. Self-read + admin queues + review history. See [15-establishment-onboarding-spec.md](15-establishment-onboarding-spec.md). | The hardest authz model (Owner / Member / Admin × Status state machine) is already exercised — the rest of the migration reuses these helpers (`MembershipChecks`, `EstablishmentStatusGuards`). |
| **Outbox / events** | `outbox_events` table, `IOutboxWriter` (transactional enqueue, same-transaction flush), `OutboxDispatcherBackgroundService` (no-op handler, disabled by default). 11 lifecycle event types emitted from approve/reject/suspend/reinstate/add-member/remove-member + the 4 change-request transitions. | When external consumers exist (notifications, search indexer, audit pipeline) we can flip `Outbox:DispatcherEnabled` without rewriting handlers. |
| **Profile compatibility** | `GET /api/v1/profile` + `/api/users/profile` alias return the current user's row plus null/[] placeholders for every legacy `UserResource` relation. `GET /api/users/profile/establishment-list` + `/api/v1/users/profile/establishment-list` alias return active memberships in Approved/Suspended establishments. **PG-only — no Qiwa**. | Public frontend can hit `/api/users/profile` today; backfilling real columns is one PATCH endpoint at a time, not a structural change. |
| **Tests** | 161 integration tests against `WebApplicationFactory` + EF InMemory. `Helpers.SeedLocalUserAsync`, `Helpers.CreateReadyToSubmitDraftAsync` shared. Coverage: auth, init-data, assets, full establishment lifecycle, change-requests, members, suspension, outbox enqueue, user sync, profile. | New endpoints get a parallel `Tests/<area>/` folder and reuse the factory; no infrastructure work needed to start writing tests. |

## 2. Endpoints currently implemented

Authoritative list, generated from `backend/src/Matloob.Api/Features/**`:

### System
- `GET /api/v1/ping`

### Auth / identity
- `GET /api/v1/me`

### Reference
- `GET /api/v1/init-data`

### Assets
- `POST /api/v1/assets`
- `GET /api/v1/assets/{id}`
- `GET /api/v1/assets/{id}/download`
- `DELETE /api/v1/assets/{id}`

### Profile (Phase C — added this sprint)
- `GET /api/v1/profile`
- `GET /api/users/profile` (alias)
- `GET /api/users/profile/establishment-list`
- `GET /api/v1/users/profile/establishment-list` (alias)

### Establishments — registration / self
- `POST /api/v1/establishments/registration/drafts`
- `PATCH /api/v1/establishments/registration/{id}/basic-info`
- `POST /api/v1/establishments/registration/{id}/documents/authorization-letter`
- `POST /api/v1/establishments/registration/{id}/documents/commercial-registration`
- `POST /api/v1/establishments/registration/{id}/submit`
- `DELETE /api/v1/establishments/registration/{id}` (discard draft)
- `GET /api/v1/establishments/mine`
- `GET /api/v1/establishments/mine/{id}`

### Establishments — members
- `POST /api/v1/establishments/{id}/members`
- `GET /api/v1/establishments/{id}/members`
- `PATCH /api/v1/establishments/{id}/members/{memberId}`
- `DELETE /api/v1/establishments/{id}/members/{memberId}`

### Establishments — change requests
- `POST /api/v1/establishments/{id}/change-requests`
- `PATCH /api/v1/establishments/{id}/change-requests/{crId}/basic-info`
- `POST /api/v1/establishments/{id}/change-requests/{crId}/documents/authorization-letter`
- `POST /api/v1/establishments/{id}/change-requests/{crId}/documents/commercial-registration`
- `POST /api/v1/establishments/{id}/change-requests/{crId}/submit`
- `POST /api/v1/establishments/{id}/change-requests/{crId}/cancel`
- `POST /api/v1/admin/establishments/change-requests/{crId}/approve`
- `POST /api/v1/admin/establishments/change-requests/{crId}/reject`
- `GET /api/v1/admin/establishments/change-requests/pending`
- `GET /api/v1/admin/establishments/change-requests/{crId}`

### Admin — establishments
- `GET /api/v1/admin/establishments/pending-review`
- `GET /api/v1/admin/establishments/{id}`
- `GET /api/v1/admin/establishments/{id}/history`
- `POST /api/v1/admin/establishments/{id}/approve`
- `POST /api/v1/admin/establishments/{id}/reject`
- `POST /api/v1/admin/establishments/{id}/suspend`
- `POST /api/v1/admin/establishments/{id}/reinstate`

## 3. Old Laravel endpoints already covered

Cross-referenced against [20-api-compatibility-matrix.md](20-api-compatibility-matrix.md):

| Laravel | New | Status |
|---|---|---|
| `GET /api/init-data` | `GET /api/v1/init-data` | exact, cached |
| `POST /api/signed-storage-url` | `POST /api/v1/assets` | redesigned (Asset GUID flow) |
| `GET /api/test/ping` | `GET /api/v1/ping` + `GET /health` | redesigned |
| `GET /api/test/token` (+ token-info) | `GET /api/v1/me` | redesigned |
| `GET /api/users/profile` | `GET /api/users/profile` (alias to `/api/v1/profile`) | compatible — placeholder relations |
| `GET /api/users/profile/establishment-list` | `GET /api/users/profile/establishment-list` | compatible — PG-only, no Qiwa |
| `POST /api/users/logout` | covered indirectly by IdM end-session — *not yet implemented as an API route* | gap |

Everything else under `routes/users.php`, `routes/establishments.php`, `routes/test_identity.php`, and `routes/web.php` is still on the backlog.

## 4. Old Laravel endpoints still pending (the migration sprint scope)

Counts pulled from the compatibility matrix; mark "PF" = called by the public frontend (the contract we protect).

### Users area (`/api/users/*`)
- **Auth** — `POST /api/users/logout` (1)
- **Profile mutators** (8 routes) — personal-info, photo, user-education, languages-skills, user-experiences, user-certificates, interest, finish-onboarding. All marked `exact` or `compatible`.
- **Opportunities (PF)** — index, show, apply, applications list/show (5 confirmed PF). Store/update/destroy: matrix marks them `removed unless confirmed`.
- **Offers received (PF)** — index, show, accept, reject, cancel, approve-cancellation, reject-cancellation, unevaluated (8 confirmed PF). `ajeer_*` fields stripped from responses. `other-evaluation` is `removed`.
- **Evaluations** — apiResource (5 routes; `destroy` removed-unless-confirmed).
- **Notifications** — index, mark-as-read, unread-count (3, all exact).

### Establishment area (`/api/establishments/*`)
- **`/me/profile`** — Show (compatible), logo (compatible — direct edit), bank-account (compatible — direct edit). `general-info` and `contact-info` PATCH are **redesigned** to go through ChangeRequest. Experience POST/PATCH (2, exact).
- **`/me/services`**, **`/me/products`** — apiResource each (5 + 5 = 10 exact).
- **`/me/opportunities`** — index, store, show, update, destroy, end, applications-list, applicant-show (8 exact).
- **Public opportunities (browse + apply for establishments)** — 8 routes, mostly exact / compatible.
- **Establishment-side offers** — 18 routes covering store, update, accept, reject, cancel-request, cancel-decision, evaluations summary. Compatibility-flag heavy because of Ajeer field strips.
- **Events** — 12 routes. Treat as a self-contained slice.
- **Evaluations** — 5 (apiResource).
- **Notifications** — 3.
- **Contracts regulations** — 1 route (`GET /api/establishments/contracts-regulations`). Keep as a static settings read-out.

### Counts
| Slice | Routes pending | Notes |
|---|---:|---|
| User profile mutators | 8 | All marked exact/compatible. Highest priority — touches the home page. |
| Opportunities (both sides + browse) | ~16 | Aggregate the worker-side + establishment-owner-side together. |
| Offers (both sides) | ~26 | Largest slice. Ajeer field strip applies. |
| Evaluations | ~10 | Two near-identical apiResources (user side + establishment side). |
| Notifications | 6 | Trivial CRUD. |
| Events | 12 | Self-contained, can ship independently. |
| Services + Products | 10 | Trivial apiResource pairs. |
| Establishment profile edits | ~7 | Half redesigned to flow through ChangeRequest. |
| Logout | 1 | OIDC end-session integration. |
| Contracts regulations | 1 | Static read-out. |
| **Total pending** | **≈ 97** | Plus a handful of `removed unless confirmed` rows that may or may not be in scope. |

## 5. Removed endpoints (locked decisions)

From [25-ajeer-disposition.md](25-ajeer-disposition.md) and the compatibility matrix:

- **All Qiwa integration paths.** No proxying, no mirror tables, no `establishment_status` mirror field. Establishment status is owned by the new `establishments.status` column.
- **All Ajeer integration paths.** No outbound writes, no `ajeer_*` fields on any response. Offers complete with status flips; no Contract issuance, no Invoice issuance.
- **Contracts.** The Laravel `contracts` table and all `/api/.../contracts/*` routes are gone. Evaluation scope moves from "contract" to "offer".
- **Invoices.** Existed only to bill Ajeer-flow contracts. The whole `/api/establishments/.../invoices/*` group is removed.
- **Filament admin panel.** Replaced by the new Angular admin SPA. Filament-only routes (`/admin/auth/callback` etc.) removed.
- **Diagnostic routes** — `/api/test/auth-check`, `/api/test/user-info`, `/api/test/identity-server-connection`, `/api/test/create-test-user`. Covered by `/api/v1/me` + `/health`.

## 6. Blocked endpoints / open decisions

These are routes the migration **can start without**, but they need a product/biz decision before they can fully ship. Each is logged here so the migration sprint doesn't stall on them.

| Item | Blocker | Where decision lives |
|---|---|---|
| `/api/users/profile/photo` | Allowed image types + size cap (still pinned to Asset purpose enum). | Q-PF-PHOTO. Default: PNG/JPG ≤ 5 MB. |
| Opportunities lifecycle (publish vs draft) | Old code had implicit "active=true" — does the new system want an explicit Draft → Published transition? | Q-OPP-1. Default: keep implicit, match old behavior. |
| Offers state machine | The matrix lists 7 transitions but the Ajeer-stripped state diagram has not been drawn. | Q-OFFER-1. Owner: Product. |
| `unevaluated` semantics | Was "completed contracts awaiting evaluation"; with contracts gone, what completion signal qualifies an offer? | Q-EVAL-1. Owner: Product. |
| `cancel-cancellation` paths (`approve-cancellation`, `reject-cancellation`) | The "cancellation request" concept is fuzzy without a contract. Confirm whether it lives on the Offer aggregate or disappears. | Q-OFFER-2. |
| Notifications transport | DB-only list works today. Email/SMS/push? | Q-NOTIF-1. Out of scope for the route migration. |
| Logout redirect URL | IdM end-session response must include `post_logout_redirect_uri`. | Q-AUTH-1. Probably reuse the public frontend origin. |
| Filament-style admin queues for non-establishment areas | The new Angular admin scope is still being defined. | Q-ADMIN-1. Outside this sprint. |

## 7. Recommended migration order

Order picked to minimize cross-slice churn and unblock the public frontend in the order it cares about.

1. **`POST /api/users/logout`** — small, unblocks SSO logout end-to-end. (~½ day)
2. **User profile mutators** (8 endpoints) — wire real columns onto the `users` table, then PATCH endpoints. Removes the "null placeholder" disclaimer from `/api/v1/profile`. (~2 days)
3. **Establishment profile reads + safe mutators** — `/me/profile` Show, `/me/profile/logo`, `/me/profile/bank-account`. The Show response already exists in spirit (covered by `/api/v1/establishments/mine/{id}`); add the alias and the small PATCHes. (~1 day)
4. **Services + Products** apiResource pairs (10 endpoints) — easiest CRUD; warms up the slice template for repetitive work. (~1 day)
5. **Opportunities** (browse + establishment-owner side, ~12 endpoints) — first slice that depends on a new aggregate; sets the pattern for Applications + Offers + Events. (~3 days)
6. **Applications** (worker apply + establishment review, ~6 endpoints). (~1.5 days)
7. **Offers** (both sides, ~26 endpoints) — biggest slice. Tackle in 3 sub-batches: store/show/list → state transitions → cancellation/eval-related. (~5 days)
8. **Events** (12 endpoints) — self-contained, can run in parallel with Offers if a second contributor is available. (~3 days)
9. **Evaluations** (both sides, ~10 endpoints) — depends on Offers being in place. (~2 days)
10. **Notifications** (6 endpoints) — trivial CRUD against a new aggregate. (~1 day)
11. **Contracts regulations** static endpoint (1 endpoint, ~30 min).

Rough total: **≈ 20 working days** for a single contributor at the steady-state pace observed in the establishment slice.

## 8. Route aliases needed for public-frontend compatibility

The new API is canonical under `/api/v1/*`. The public frontend still hits some legacy paths. For each, expose the legacy URL as an additional route on the same endpoint (FastEndpoints supports multiple `Routes(...)`):

| Legacy URL | Canonical URL | Status |
|---|---|---|
| `/api/init-data` | `/api/v1/init-data` | **alias pending** — add when migrating the route slice |
| `/api/users/profile` | `/api/v1/profile` | **alias added** (Phase C) |
| `/api/users/profile/establishment-list` | `/api/v1/users/profile/establishment-list` | **alias added** (Phase C) |
| `/api/users/profile/personal-info` and the other PATCH profile routes | `/api/v1/users/profile/personal-info` etc. | **alias pending** — add during user-profile-mutators batch |
| `/api/users/opportunities/*`, `/api/users/offers/*`, `/api/users/evaluations/*`, `/api/users/notifications/*` | `/api/v1/...` equivalents | **alias pending** during their respective batches |
| `/api/establishments/me/*` | `/api/v1/establishments/me/*` | **alias pending** during the establishment-side batches |

> **Rule of thumb:** when migrating any Laravel route classified `exact` or `compatible` in the matrix, register BOTH the canonical and the legacy URL on the new endpoint. Drop the legacy alias only when the public frontend confirms it has moved.

## 9. Risks before starting the migration sprint

| Risk | Likelihood | Mitigation |
|---|---|---|
| **Response-shape drift** — `UserResource` and the various `OfferResource`/`OpportunityResource` flavors have ~30 fields each in Laravel. Easy to miss one. | High | For each migrated route, write a HAR-driven contract test that snapshots the Laravel response and asserts the .NET response matches (modulo the explicitly-stripped Ajeer/Qiwa fields). Capture HARs in `docs/captures/` *before* writing the endpoint. |
| **Authorization drift** — Laravel mixes middleware (`identity.auth`, `identity.role:matloob_user`, `establishment.context`) with controller-level `authorize()` calls. | Med | Use the existing helpers (`MembershipChecks`, `Policy.User`, `Policy.Admin`) and run [21-api-authorization-responses.md](21-api-authorization-responses.md) as the response-code contract for unauthorized/forbidden. |
| **N+1 leakage** — Laravel resources do eager loading lazily; the EF equivalents will silently issue extra queries if the projection isn't shaped at the LINQ level. | Med | Default to projected DTOs in every read endpoint (`.Select(e => new XResponse(...))`). No `.AsEnumerable()` before filtering. |
| **Soft-delete + audit interceptor conflicts** — new aggregates inheriting `BaseAuditableEntity` participate in both filters; new join tables / lookup rows must inherit `BaseEntity` instead. | Low–Med | Pattern is documented and exercised (Outbox, ReviewHistory). When in doubt, follow `OutboxEvent` for an immutable row and `EstablishmentMember` for an aggregate-bound mutable row. |
| **`/api/users/profile` placeholder fields** — public frontend may try to read e.g. `bank_account.iban` and crash. | Low | The Phase C endpoint returns `null` and `[]` for every relation; serialized shape is stable. Each migrated mutator backfills its slice. Note this in the public-frontend ticket. |
| **Establishment status read-side semantics** — public frontend lists may filter on Approved-only; admin lists on everything. Easy to get wrong by copy/paste. | Med | The compatibility matrix already labels each route's expected statuses. Wire them as a guard at the endpoint top rather than scattering `WHERE status IN (...)`. |
| **Outbox dispatcher disabled in prod** — anything that depends on outbound notifications won't fire until the handler is wired. | Med | Document explicitly that `Outbox:DispatcherEnabled=false` is the current default. The handler stub lives in `OutboxDispatcherService.DispatchHandlerNoOp`. |
| **MinIO/S3 not present** — Asset storage is local disk, which is fine for dev/staging but won't survive horizontal scaling. | Med | Out of scope for this migration sprint; flag in the next ops sprint. `AssetStorageDriver` already has the enum slot. |
| **Establishment edit-in-place vs ChangeRequest** — Laravel public frontend calls `PATCH /me/profile/general-info` directly; new flow requires a ChangeRequest. | High | When migrating that endpoint, decide whether it 503/410s or transparently opens a CR. Q-EST-1 in the compatibility matrix. |

## 10. Final readiness verdict

- **Solution skeleton, persistence, auth, init-data, assets, establishments end-to-end, outbox, local users, profile compatibility** — green.
- **Tests** — 161/161 passing locally.
- **Spec docs** — onboarding spec, compatibility matrix, authorization-response policy, ajeer disposition, data-migration plan, and this readiness audit are all up to date on `main`.
- **Open blockers for the migration sprint** — none structural. All open items in §6 are per-endpoint product decisions that can be unblocked one at a time without restructuring the codebase.

> **Recommendation:** start the old Laravel API migration sprint now, following the order in §7.

---

## 11. First migration sprint outcomes (2026-05-22)

This section is appended after the first migration sprint completed groups 1–4 (users/profile compatibility, establishment compatibility, settings/reference, media). Counts in §2–§4 above are pre-sprint; the items below are net new.

### 11.1 Endpoints migrated / aligned

| Endpoint | Disposition | Notes |
|---|---|---|
| `GET /api/v1/profile` + `GET /api/users/profile` | aligned | Response refactored to Laravel `UserResource` snake_case shape with the full field set (`id`, `name`, `email`, `id_number`, `gender`, `nationality`, `age`, `date_of_birth`, `hijri_date_of_birth`, `bio`, `phone_number`, `additional_phone_number`, `years_of_experience`, `passport_copy`, `photo`, `professions[]`, `experiences[]`, `certificates[]`, `skills[]`, `education[]`, `city`, `region`, `bank_account`, `languages[]`, `supportive_documents[]`, `participations[]`, `profile_complete_percentage`, `evaluations[]`, `reviews[]`, `rate`, `total_reviews`, `onboarded`, `uncompleted_profile_sections[]`). Unmigrated relations land as `null`/`[]`/`0`. `identity_id` is a new-client extension. |
| `GET /api/users/profile/establishment-list` + `GET /api/v1/users/profile/establishment-list` | aligned | Snake_case Laravel `EstablishmentResource` shape (`id`, `name`, `type`, `logo`, `labor_office_id`, `sequence_number`). `status` + `role` are new-client extensions. |
| `GET /api/establishments/me/profile` + `GET /api/v1/establishments/me/profile` | **new** legacy alias | Returns the Laravel composite `EstablishmentResource + ProfileResource` shape. Establishment resolves from `?establishment_id={guid}` → `X-Establishment-Id` header → single-active-membership auto-pick. Ambiguous → 400 `establishment_context_required`. No membership → 404. |

### 11.2 Legacy aliases now active

- `/api/users/profile` → `/api/v1/profile`
- `/api/users/profile/establishment-list` → `/api/v1/users/profile/establishment-list`
- `/api/establishments/me/profile` → `/api/v1/establishments/me/profile`
- `/api/init-data` → `/api/v1/init-data` (pre-sprint)

### 11.3 Endpoints intentionally NOT shipped this sprint

| Endpoint | Reason | Reopen when |
|---|---|---|
| `POST /api/users/logout` | Q-AUTH-1 — IdM end-session URL + `post_logout_redirect_uri` undecided. | Auth team confirms the redirect target. |
| `PATCH /api/users/profile/personal-info` | Needs `users` schema additions (id_number, gender, dob, …) + nationality/city/region FK lookups. Q-PROFILE-MUTATORS. | Schema decision per field; lookup tables are already present. |
| `PATCH /api/users/profile/photo` | Q-PF-PHOTO — image type + size cap unset. | Product decision. |
| `PATCH /api/users/profile/user-education` | Needs `user_education` table + `UpdateOrCreateUserEducationRequest` mapping. | When schema lands. |
| `PATCH /api/users/profile/languages-skills` | Needs `user_languages` + `user_skills` join tables. | When schema lands. |
| `PATCH /api/users/profile/user-experiences` | Needs `user_experiences` table. | When schema lands. |
| `PATCH /api/users/profile/user-certificates` | Needs `user_certificates` table. | When schema lands. |
| `PATCH /api/users/profile/interest` | Needs `user_professions` join + `UpdateInterestRequest` validation. | When schema lands. |
| `PATCH /api/users/profile/finish-onboarding` | Needs `users.onboarded` column. | When schema lands. |
| `PATCH /api/establishments/me/profile/general-info` | Q-EST-1 — Approved establishments must flow through ChangeRequest; behavior of in-place PATCH on Approved is undecided (405 vs auto-CR vs Draft-only). | Product decision. |
| `PATCH /api/establishments/me/profile/contact-info` | Same as general-info — redesigned to ChangeRequest. | Product decision. |
| `PATCH /api/establishments/me/profile/logo` | Q-EST-2 — logo bypass-CR rule not confirmed. Also needs `logo_asset_id` column. | Product decision + schema. |
| `PATCH /api/establishments/me/profile/bank-account` | No `bank_accounts` table in the new system yet. | When schema lands. |
| `POST/PATCH /api/establishments/me/profile/experience` | No `establishment_experiences` table. | When schema lands. |
| `GET /api/establishments/contracts-regulations` | Laravel response was Qiwa-derived saudization% + Ajeer-derived contract%. Both data sources removed; nothing meaningful to return. Q-CONTRACTS-REGULATIONS. | Product decides whether to (a) remove entirely, (b) return a static constant, or (c) replace with new business rule. |
| `POST /api/signed-storage-url` | Marked **redesigned**, not **compatible** — the Laravel pre-sign + PUT flow does not map onto a one-step multipart POST. Asking the public frontend to keep the legacy 2-step flow would require reintroducing S3, which is explicitly forbidden. | Q-PF-5 — coordinate cutover with the public frontend team; no compat shim. |

### 11.4 New open questions discovered

| ID | Question |
|---|---|
| Q-EST-CONTEXT | When the caller has 0 or many memberships at `/api/establishments/me/profile`, is auto-pick + 400-on-ambiguity the right behavior? Or should the frontend always send `X-Establishment-Id` / `?establishment_id`? |
| Q-PROFILE-MUTATORS | Are the 8 user-profile PATCHes still in scope? They imply 6+ new tables (user_education, user_skills, user_languages, user_experiences, user_certificates, user_professions) plus several `users` columns. Confirm the field-by-field schema before next sprint. |
| Q-CONTRACTS-REGULATIONS | Per §11.3 — does the public frontend still call `/contracts-regulations`? If yes, what should it return now? |

### 11.5 Sprint result summary

- **Commits added** (newest first):
  - `e55993d` feat(establishments): add /me/profile legacy alias
  - `481d91e` feat(profile): align response shape with Laravel UserResource
- **Tests added:** 8 new in `MeProfileCompatibilityTests`; existing 8 in `ProfileCompatibilityTests` updated for snake_case. **Total: 169 passing / 161 before.**
- **Build:** clean (0 warnings, 0 errors).

---

## 12. Phase OAO-1 outcomes (2026-05-22)

Domain + persistence foundation for Opportunities / Applications / Offers / Evaluations is in place. No endpoints.

- **Aggregates added:** `Opportunity`, `OpportunityApplication`, `Offer`, `Evaluation` plus side tables (`OpportunityAsset`, `SuccessManagementCriterion(+Asset)`, `OfferCancellationRequest`, `EvaluationAsset`).
- **Migrations:** `OpportunitiesInitial`, `ApplicationsInitial`, `OffersInitial`, `EvaluationsInitial`.
- **Ajeer-stripped from day one:** no `ajeer_*` columns, no `Contracts` / `Invoices` tables, evaluations reference `OfferId` not `contract_id`. Sponsor flow kept as internal-only status machine.
- **DB-only constraints:** CHECK constraints on the split-FK pairs (application applicant, cancellation requester, evaluation evaluable/evaluator) plus partial-unique indexes for "one application per pair" and "one open cancellation per offer."
- **Tests:** +49 domain + persistence-shape tests under `tests/Matloob.Api.Tests/Oao/`; final test count 218.

## 13. Phase OAO-2 outcomes (2026-05-22) — read endpoints

Read-only endpoints for Opportunities + Applications + the establishment-side application reads. No write endpoints; no Offer / Evaluation / Notification endpoints.

### 13.1 Endpoints shipped

Every endpoint is registered at both the legacy Laravel URL and a canonical `/api/v1/...` URL.

| Slice | Legacy URL | Canonical URL |
|---|---|---|
| User browse list | `GET /api/users/opportunities` | `GET /api/v1/users/opportunities` |
| User browse show | `GET /api/users/opportunities/{id}` | `GET /api/v1/users/opportunities/{id}` |
| Establishment browse list | `GET /api/establishments/opportunities` | `GET /api/v1/establishments/{establishmentId}/browse/opportunities` |
| Establishment browse show | `GET /api/establishments/opportunities/{id}` | `GET /api/v1/establishments/{establishmentId}/browse/opportunities/{id}` |
| Browse categories | `GET /api/establishments/opportunities/categories` | `GET /api/v1/establishments/{establishmentId}/browse/opportunity-categories` |
| Owner list | `GET /api/establishments/me/opportunities` | `GET /api/v1/establishments/{establishmentId}/opportunities` |
| Owner show | `GET /api/establishments/me/opportunities/{id}` | `GET /api/v1/establishments/{establishmentId}/opportunities/{id}` |
| User applications list | `GET /api/users/opportunities/applications` | `GET /api/v1/users/opportunities/applications` |
| User application show | `GET /api/users/opportunities/applications/{applicantId}` | `GET /api/v1/users/opportunities/applications/{applicantId}` |
| Estab. browse applications list | `GET /api/establishments/opportunities/applications` | `GET /api/v1/establishments/{establishmentId}/browse/applications` |
| Estab. browse application show | `GET /api/establishments/opportunities/applications/{applicantId}` | `GET /api/v1/establishments/{establishmentId}/browse/applications/{applicantId}` |
| Own-opportunity applicants list | `GET /api/establishments/me/opportunities/{id}/applications` | `GET /api/v1/establishments/{establishmentId}/opportunities/{id}/applications` |
| Own-opportunity applicant show | `GET /api/establishments/me/applicants/{applicantId}` | `GET /api/v1/establishments/{establishmentId}/applicants/{applicantId}` |

### 13.2 Compatibility notes

- **Snake_case throughout** via `[JsonPropertyName]`. Field set matches the Laravel `OpportunityResource` / `OpportunityApplicationResource` 1:1 with the exceptions below.
- **Dropped fields:** `contracts_count` (no Contracts), nested `contract` object (no Contracts), `contract_path` / `notice_path` / `show_print_notice` (only on the Offer slice in OAO-5; mentioned here for completeness).
- **Placeholder fields** (null / [] / 0 until the matching slice migrates): `event` (Event slice not landed), `nationality` (minimal projection only — full nationality resource on the worker profile side), `status_label` / `card_type` / `status_icon` / `gender_label` / `establishment_classification_label` (i18n hook deferred), `success_criteria.uploads` (criterion assets shipped but media URLs not), `issuer.logo`.
- **`establishment_classification` + `gender`:** emitted as arrays (`["small", "medium"]`) matching the Laravel model accessor that exploded the legacy CSV column.
- **`is_applied`:** computed for the current principal — the worker's sub on the user endpoints; the resolved establishment on the establishment-side endpoints.
- **Establishment context resolution (legacy routes):** new shared `EstablishmentContextHelper` handles `?establishment_id` → `X-Establishment-Id` → single-active-membership auto-pick. Ambiguous returns 400 with code `establishment_context_required`. No membership returns 404. Canonical routes read the id from the URL path.

### 13.3 Application status derivation

Mirrors `ApplicantSupport::getApplicationStatus`: an application's `status` is `accepted` when any `Offer` row exists for that `application_id`, otherwise `pending`. `status_label` is reserved for the future localisation pass.

### 13.4 Open question resolved in this sprint

| ID | Question | Default applied |
|---|---|---|
| Q-OAO-GROUPED-RESPONSE | Laravel `GroupedOpportunityResource` returned the owner-list grouped by status. Do we mirror? | **No** — owner list emits a flat array; clients group client-side from the `status` field. Matches the established Laravel-compat shape (snake_case array of full resources) and keeps the wire shape uniform across owner + browse + user-side. |

### 13.5 Sprint result summary

- **Commits added (oldest → newest):**
  1. `242f4c2` feat(opportunities): add shared opportunity response DTOs
  2. `acf5df3` feat(opportunities): add user opportunity browse endpoints
  3. `514c451` feat(opportunities): add establishment browse endpoints
  4. `dbcb08d` feat(opportunities): add owner opportunity read endpoints
  5. `7c04ca5` feat(applications): add application read response DTOs
  6. `9c4d988` feat(applications): add user application read endpoints
  7. `e219c78` feat(applications): add establishment application read endpoints
  8. `4727f7f` test(opportunities): add opportunity read compatibility tests
- **Tests added:** 60 (across `UserOpportunityBrowseTests`, `EstablishmentBrowseTests`, `MineOpportunityTests`, `UserApplicationReadTests`, `EstablishmentApplicationReadTests`, `OaoReadCompatibilitySweepTests`). **Total: 278 passing / 218 before.**
- **Build:** clean (0 warnings, 0 errors).

### 13.6 Gaps / TODOs

- Event slice not migrated → `opportunity.event` is null. When Events land the FK on `opportunities.event_id` activates, the nested object hydrates, and the `byApplicableEvent()` Laravel filter can be ported.
- Nationality / region / city full resources deferred → minimal `{id, name}` projection for now.
- `?season` and `?recommended` query filters on the user browse defer until the Events / personalisation slices land.
- Issuer logo and opportunity upload URLs ship as null / asset GUIDs; signed-URL surfacing arrives with the asset-download cleanup.

## 14. Phases OAO-3 → OAO-7 outcomes (2026-05-23) — full OAO write coverage

OAO write endpoints + offer lifecycle + evaluations + outbox emission. Test count rose from 278 → 328 over OAO-3/4/5/6, with OAO-9 adding more on top (see §16).

### 14.1 Endpoints shipped

**OAO-3 (Opportunity writes — owner-side):**
- `POST /api/establishments/me/opportunities` + canonical
- `PATCH /api/establishments/me/opportunities/{id}` + canonical
- `DELETE /api/establishments/me/opportunities/{id}` + canonical (soft delete)
- `PATCH /api/establishments/me/opportunities/{id}/end` + canonical
- `POST /api/establishments/me/opportunities/{id}/assets` + canonical

**OAO-4 (Apply):**
- `POST /api/users/opportunities/{id}/apply` + canonical
- `POST /api/establishments/opportunities/{id}/apply` + `.../browse/opportunities/{id}/apply`

**OAO-5 (Offers — internal-only, no Ajeer/contracts):**
- `GET /api/users/offers` + show
- `GET /api/establishments/received-offers` + show
- `GET /api/establishments/sent-offers` + show
- `GET /api/establishments/offers/pending-action` (with `?type` filter)
- `POST /api/establishments/offers/send`
- `POST /api/(users|establishments)/offers/{id}/accept` and `/reject`
- `POST /api/(users|establishments)/offers/cancel` and `/{id}/approve-cancellation`, `/{id}/reject-cancellation`
- `GET /api/establishments/offers/{id}/pending-sponsor-approval`
- `POST /api/establishments/offers/{id}/sponsor/accept` and `/sponsor/reject`

**OAO-6 (Evaluations):**
- `GET /api/(users|establishments)/evaluations` + show + create
- `GET /api/(users|establishments)/offers/unevaluated`
- `GET /api/(users|establishments)/offers/{id}/other-evaluation`

All endpoints register both the Laravel legacy URL and a canonical `/api/v1/...` URL.

### 14.2 Removed Laravel endpoints (per docs/25-ajeer-disposition.md)

- `GET /api/establishments/offers/ajeer/check-eligibility` — Ajeer integration removed.
- `GET /api/establishments/offers/pending-invoice` — Invoices removed.
- `GET /api/establishments/invoices/*` (index/show/issue) — Invoices removed.
- `GET /api/establishments/contracts-regulations` — Qiwa + Ajeer data sources removed (Q-CONTRACTS-REGULATIONS still open).
- `POST/PUT/DELETE /api/users/opportunities` — apiResource auto-routes never implemented in Laravel.
- `POST/PUT/DELETE /api/users/offers` + `/api/establishments/received-offers` + `/sent-offers` apiResource auto-routes — same.
- `PATCH/DELETE /api/users/evaluations/{id}` + `/api/establishments/evaluations/{id}` apiResource auto-routes — evaluations are one-shot.

### 14.3 DTO compatibility notes

- Snake_case throughout via `[JsonPropertyName]`.
- `OfferResponse` is brand new and Laravel-mirrored MINUS `contract`, `contract_type`, `contract_type_label`, `contract_path`, `notice_path`, `show_print_notice`, every `ajeer_*`. Adds top-level `accepted_at`.
- `OfferCancellationRequestDto` exposes `requested_by_type` + `requested_by_id` instead of the Laravel polymorphic morph.
- `EvaluationResponse` drops the `contract` nested resource entirely; carries `offer_id` instead.

### 14.4 Status machines (final)

**Opportunity:** Drafted (legacy import only) → Upcoming or Active (start_date auto-derived) → Ended (explicit) / Finished (passive).

**Application:** No status column. Derived as `accepted` when at least one Offer exists for the application, else `pending`.

**Offer:**
- Initial: `Pending` (no sponsor) or `PendingSponsorApproval` (with sponsor).
- `PendingSponsorApproval` → `Pending` (sponsor accept) or `SponsorRejected` (sponsor reject).
- `Pending` → `Accepted`, `Rejected`, `Expired`.
- `Accepted` → `CancellationRequested` (no sponsor) or `PendingSponsorCancellationApproval` (with sponsor).
- Cancellation request → `Canceled` (approve) or `Accepted` (reject; reopenable).
- `Accepted` → `WaitingForEvaluation` (when first evaluation lands) → `Completed` (both sides evaluated).
- Invalid transitions surface as 422 `invalid_offer_status_transition`.

**Evaluation:** Immutable. One row per (offer, evaluator user) and per (offer, evaluator establishment). Both sides evaluated → Offer.Completed.

### 14.5 Outbox events emitted

Wired inline in every write endpoint, persisted in the same transaction as the aggregate change:

- `opportunity.created`, `opportunity.updated`, `opportunity.ended`, `opportunity.deleted`
- `application.submitted`
- `offer.created`, `offer.sponsor_approval_pending` (when sponsor is set on send)
- `offer.accepted`, `offer.rejected`
- `offer.cancellation_requested`, `offer.cancellation_approved`, `offer.cancellation_rejected`
- `offer.sponsor_accepted`, `offer.sponsor_rejected`
- `evaluation.submitted`

`offer.expired` and `offer.completed` slots exist in `OfferEventTypes` but are not emitted by an HTTP endpoint today (expiry is compute-on-read; completion happens implicitly during the second evaluation's SaveChanges).

### 14.6 Remaining blocked items

| Item | Blocker |
|---|---|
| `/api/establishments/contracts-regulations` rewrite | Q-CONTRACTS-REGULATIONS — Qiwa saudization% + Ajeer contract% data not available. |
| `/api/(users|establishments)/notifications/*` HTTP routes | Q-NOTIF-TRANSPORT — currently the outbox is the only fan-out surface. |
| User profile mutators (8 PATCHes) | Q-PROFILE-MUTATORS + Q-PF-PHOTO — multiple new tables. |
| Establishment profile mutators | Q-EST-1 + Q-EST-2 + missing bank/experience tables. |
| `POST /api/users/logout` | Q-AUTH-1. |
| Event slice | Q-EVENT-SLICE — pending product/team direction. `Opportunity.event_id` is a bare Guid until then. |
| Background job to flip Pending → Expired | Q-OFFER-EXPIRY — compute-on-read is sufficient for now. |

### 14.7 Commits added (OAO-3 → OAO-7)

1. `c932724` feat(opportunities): add owner create opportunity endpoint
2. `3d78181` feat(opportunities): add owner update/delete/end endpoints
3. `a47f2b4` feat(opportunities): add opportunity asset link endpoint
4. `3e30eb5` test(opportunities): add opportunity write compatibility tests
5. `405e282` feat(applications): add user apply endpoint
6. `7fc3262` feat(applications): add establishment apply endpoint
7. `ebb0ab6` fix(applications): translate duplicate application constraint to conflict
8. `9865cb3` test(applications): add apply lifecycle tests
9. `b126095` feat(offers): add shared offer response DTOs
10. `6a94071` feat(offers): add offer read endpoints
11. `e48e1ce` feat(offers): add send offer endpoint
12. `d2064a4` feat(offers): add accept and reject endpoints
13. `f5a2068` feat(offers): add cancellation request endpoints
14. `fab72b8` feat(offers): add sponsor approval endpoints
15. `99e3e26` test(offers): add offer lifecycle tests
16. `e56606d` feat(evaluations): add shared evaluation response DTOs
17. `3660637` feat(evaluations): add user evaluation endpoints
18. `546ff3b` feat(evaluations): add establishment evaluation endpoints
19. `26b452b` feat(offers): add unevaluated and other-evaluation endpoints
20. `850de11` test(evaluations): add evaluation lifecycle tests
21. `ab142fe` test(oao): add outbox emission tests

### 14.8 Sprint result summary

- **Tests added (OAO-3 → 7):** 50 (write compat 14, apply 11, offer lifecycle 13, evaluation 8, outbox 4). **Total at end of OAO-7: 328.**
- **Build:** clean.

## 15. Recommended next phase after OAO

Three options, in suggested order:

1. **Notifications HTTP routes** (Phase NOTIF-1) — small surface (3 routes per side), DB-only reads, unblocks the public frontend's badge counter. Q-NOTIF-TRANSPORT still gates external delivery (email/SMS/push) but the read endpoints + outbox subscriber pattern can ship first.
2. **Events slice** (Phase EVENT-1) — unblocks `opportunity.event` hydration, the `byApplicableEvent()` filter, the suggested-locations / suggested-attendees endpoints, and a chunk of init-data fields.
3. **Profile mutators** (Phase USER-PROFILE) — Q-PROFILE-MUTATORS owns several schema additions (user_education, user_skills, user_languages, user_experiences, user_certificates, user_professions + `users` column extensions). Heaviest of the three; valuable to unblock the profile page.

> **Recommendation: start NOTIF-1.** Smallest, no upstream blockers, lights up the badge the public frontend already reads.

---

### Recommended next prompt

**"Notifications HTTP routes migration — Phase NOTIF-1"**

Scope: ship the 6 user + establishment notification routes (list / mark-as-read / unread-count) reading from a new `notifications` table populated by an outbox subscriber that mirrors the OAO event stream. Defer external transport (email/SMS/push) until Q-NOTIF-TRANSPORT is decided. Tests per endpoint. Stop and report when done.

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

### Recommended next prompt

**"Old Laravel API migration sprint"**

Scope: walk routes/users.php and routes/establishments.php in the order from §7, register canonical + legacy URLs on each new endpoint per §8, ship tests per slice, leave §6 items in their respective endpoints as `// TODO Q-<id>` until the product decision lands.

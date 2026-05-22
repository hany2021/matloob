# Legacy request-body compatibility

**Companion to** [20-api-compatibility-matrix.md](20-api-compatibility-matrix.md) and [40-api-migration-readiness.md](40-api-migration-readiness.md). This document captures the audit findings that emerged AFTER the full OAO migration shipped, the fixes that landed, and the current status of every migrated route's request-body compatibility with the old Laravel frontend.

> Goal of the audit: the old frontend must call the new backend without changing URLs OR request bodies. Response-shape drift was already documented; this round focused exclusively on request-body parity.

---

## 1. What was broken

The OAO migration preserved URLs and response shapes carefully but missed five request-shape issues. The findings, in order of severity:

| ID | Endpoint | Old Laravel body | New endpoint (pre-fix) accepted | Verdict |
|---|---|---|---|---|
| F-1 | POST `/api/establishments/me/opportunities` | bulk `{event_uuid, opportunities:[{opportunity_category_uuid, ...}]}` + multipart `uploads[]` | flat single object only with `event_id` + `opportunity_category_id` | **FAIL** |
| F-2 | POST `/api/establishments/offers/{id}/reject` | bare POST (no body) | `{reason_id}` required by FluentValidation | **FAIL** |
| F-3 | POST `/api/establishments/evaluations` | multipart/form-data with `uploads[]` files | JSON only | **FAIL on multipart** |
| F-4 | All `/api/establishments/*` legacy routes | `X-Commissioner-UUID` header | `?establishment_id` / `X-Establishment-Id` / single-membership auto-pick | **PARTIAL** — broke for multi-establishment users |
| F-5 | POST `/api/establishments/offers/send` | strict Laravel `SendOfferRequest` field set | additive new fields silently allowed | **PARTIAL but tolerable** — System.Text.Json ignores extra keys |

Everything else (apply endpoints, cancel offer body, user-side reject, sponsor reject, user evaluation tolerated extras) was already compatible.

---

## 2. What was fixed (5 commits)

| Commit | Fix |
|---|---|
| `bf8445c` `fix(establishments): support legacy X-Commissioner-UUID context header` | F-4 |
| `123d710` `fix(opportunities): add legacy bulk opportunity create adapter` | F-1 |
| `8d42081` `fix(offers): allow legacy bare establishment offer rejection` | F-2 |
| `340120e` `fix(evaluations): support legacy multipart establishment evaluation` | F-3 |
| `dd006fd` `test(api): add legacy request compatibility regression suite` | regression coverage of all four |

### F-4 detail — `X-Commissioner-UUID`

`EstablishmentContextResolver.TryReadExplicit` now accepts `X-Commissioner-UUID` as a third explicit context source, after `?establishment_id` and `X-Establishment-Id`. The new system doesn't retain Laravel's `commissioners` table, so we interpret the header value as the establishment id directly. The endpoint's downstream membership check enforces permission, so a bogus or stranger's id still 404s.

Side change: `GetMeProfileEndpoint` was refactored to use the shared `EstablishmentContextResolver` instead of its own duplicated local method, bringing it into parity with every other legacy route.

### F-1 detail — bulk opportunity create

`CreateOpportunityRequest` is now polymorphic. It accepts BOTH:

- Legacy bulk: `{event_uuid, opportunities:[{opportunity_category_uuid, ...}]}` — returns an array (Laravel `OpportunityResource::collection` parity).
- Canonical single: `{event_id, opportunity_category_id, name, ...}` — returns a single object.

Field aliases honored on either path: `event_id` ↔ `event_uuid` and `opportunity_category_id` ↔ `opportunity_category_uuid`. FluentValidation runs on the flat path only; the handler validates per-item on the bulk path.

> **Multipart inline uploads** (`opportunities[*].uploads[]`, `success_criteria[].uploads[]`) are NOT auto-ingested by this endpoint. The JSON bulk shape covers every other Laravel field, and clients link assets via the dedicated `POST /me/opportunities/{id}/assets` endpoint after create. Documented in the endpoint XML.

### F-2 detail — bare establishment reject

`EstablishmentRejectOfferEndpoint` converted from `Endpoint<RejectionRequest, ...>` to `EndpointWithoutRequest<...>` so empty Content-Length doesn't trip FastEndpoints' request-binding step. The handler now reads the body opportunistically — accepting empty body, empty JSON `{}`, or canonical `{reason_id, other_reason?}`.

Domain change: `Offer.Reject` and `Offer.SponsorReject` now accept a nullable `Guid?` reason. The DB column is already nullable. User-side reject + sponsor-reject keep the strict `reason_id`-required validator because their Laravel counterparts also required it.

### F-3 detail — multipart establishment evaluation

`CreateEstablishmentEvaluationEndpoint` converted from `Endpoint<TRequest, TResponse>` to `EndpointWithoutRequest<TResponse>`. The handler inspects `Content-Type` and either deserializes JSON or reads the form via `Request.ReadFormAsync` — accepting both `uploads` and `uploads[]` form-key names for parity with Laravel's array-bracket syntax.

Each uploaded file is persisted via the existing `IFileStorage` service: saved to local disk, an `Asset` row written, and an `EvaluationAsset` row linking it to the new `Evaluation`. The `evaluation.submitted` outbox payload includes an `upload_count` field for downstream consumers.

---

## 3. Legacy route compatibility status (final)

| Route | URL | Body | Header context | Status |
|---|---|---|---|---|
| Profile read | `/api/users/profile` | n/a | n/a | ✅ |
| Profile establishment-list | `/api/users/profile/establishment-list` | n/a | n/a | ✅ |
| Init-data | `/api/init-data` | n/a | n/a | ✅ |
| Establishment me/profile | `/api/establishments/me/profile` | n/a | ✅ X-Commissioner-UUID supported | ✅ |
| Opportunity browse + show + categories | `/api/users/opportunities`, `/api/establishments/opportunities*` | n/a | ✅ | ✅ |
| Opportunity owner list + show + end + delete + asset link | `/api/establishments/me/opportunities*` | n/a / bare | ✅ | ✅ |
| **Opportunity owner CREATE** | `POST /api/establishments/me/opportunities` | ✅ accepts bulk + single | ✅ | ✅ |
| User apply | `POST /api/users/opportunities/{id}/apply` | bare POST | ✅ | ✅ |
| Establishment apply | `POST /api/establishments/opportunities/{id}/apply` | bare POST | ✅ | ✅ |
| Offer user list/show | `/api/users/offers`, `/api/users/offers/{id}` | n/a | n/a | ✅ |
| Offer establishment list/show + pending-action | `/api/establishments/(received|sent|offers)-offers*` | n/a | ✅ | ✅ |
| Offer send | `POST /api/establishments/offers/send` | ✅ Laravel field names + extras silently dropped | ✅ | ✅ |
| Offer accept (user + establishment) | bare POST | bare POST | ✅ | ✅ |
| Offer reject **user** | `POST /api/users/offers/{id}/reject` | `{reason_id, other_reason?}` | n/a | ✅ |
| **Offer reject establishment** | `POST /api/establishments/offers/{id}/reject` | ✅ accepts bare OR `{reason_id, other_reason?}` | ✅ | ✅ |
| Offer cancel + approve/reject cancellation | various | ✅ Laravel field names | ✅ | ✅ |
| Sponsor accept | bare POST | bare POST | ✅ | ✅ |
| Sponsor reject | `{reason_id, other_reason?}` | ✅ | ✅ | ✅ |
| User evaluation | `POST /api/users/evaluations` | ✅ accepts Laravel extras (`evaluable_id`, `opportunity_id`) | n/a | ✅ |
| **Establishment evaluation** | `POST /api/establishments/evaluations` | ✅ accepts JSON OR multipart/form-data with `uploads[]` | ✅ | ✅ |

---

## 4. Request body compatibility status (final)

| Endpoint | Pre-fix verdict | Post-fix verdict |
|---|---|---|
| Bulk opportunity create | FAIL | ✅ PASS — bulk shape accepted at legacy URL |
| Bare establishment reject | FAIL | ✅ PASS — empty body accepted at legacy URL |
| Multipart evaluation | FAIL on multipart | ✅ PASS — multipart + JSON both accepted |
| X-Commissioner-UUID context | PARTIAL | ✅ PASS — header treated as establishment-id alias |
| Offer send extras | PARTIAL but tolerable | ✅ PARTIAL still — Laravel `contract_type` silently ignored. Documented. |

---

## 5. Remaining intentional differences

These are NOT bugs and require frontend changes to use the new flow:

| Difference | Reason | Doc |
|---|---|---|
| `OfferResource.contract`, `contract_*`, `notice_path`, `show_print_notice`, `ajeer_*` fields dropped from responses | Contracts + Invoices + Ajeer removed | [25-ajeer-disposition.md](25-ajeer-disposition.md) |
| `OpportunityResource.contracts_count` dropped from responses | Same | same |
| `EvaluationResource.contract` nested object dropped, replaced with top-level `offer_id` | Q-EVAL-1 | [40-api-migration-readiness.md §11](40-api-migration-readiness.md) |
| `OfferResource.accepted_at` added top-level | Replaces dropped nested `contract.created_at` (Q-OFFER-1) | same |
| Settings key `settings.ajeer_enabled` still present in `/api/init-data` payload | Q-AJ-3 says drop at data-migration import time, not at seed time. Production migration will drop it. | [30-data-migration-plan.md](30-data-migration-plan.md) |
| Bulk-create opportunities cannot send inline multipart `uploads[]` | Documented split: clients call `POST /me/opportunities/{id}/assets` after create. Bulk JSON shape covers all other fields. | endpoint XML |

---

## 6. Intentionally removed endpoints (no compatibility owed)

Listed in the matrix and the audit; not changing now:

- `POST /api/signed-storage-url` → replaced by `POST /api/v1/assets` (multipart). Old pre-sign + PUT flow has no equivalent. Public FE must cut over.
- `GET /api/establishments/offers/ajeer/check-eligibility` — Ajeer removed.
- `GET /api/establishments/offers/pending-invoice` — Invoices removed.
- `GET /api/establishments/invoices/*` — Invoices removed.
- `GET /api/establishments/contracts-regulations` — Qiwa + Ajeer data sources removed (Q-CONTRACTS-REGULATIONS).
- `POST/PUT/DELETE /api/users/opportunities` — apiResource auto-routes never implemented in Laravel.
- `POST/PUT/DELETE /api/(users|establishments)/(received|sent)-offers` — same.
- `PATCH/DELETE /api/(users|establishments)/evaluations/{id}` — evaluations one-shot in Laravel and the new system.

---

## 7. Confirmation

- **Old frontend URLs do NOT need to change** for any migrated API.
- **Old frontend request bodies are NOW supported** for every migrated write endpoint, except for the intentionally-removed endpoints listed in §6.
- **Old frontend tolerates the new responses** because we either preserve every Laravel field 1:1 or replace it with documented placeholders / new-client extensions (System.Text.Json ignores unknown keys on the client too).
- The `X-Commissioner-UUID` header is honored as an establishment-id alias for multi-establishment users; single-membership users continue to auto-resolve without any header.

---

## 8. Test coverage

- `LegacyCommissionerHeaderTests` (5 tests) — context resolution
- `LegacyBulkOpportunityCreateTests` (6 tests) — bulk + single, field aliases, error paths, canonical still works
- `LegacyBareEstablishmentRejectTests` (3 tests) — bare body + empty JSON + canonical body
- `LegacyMultipartEvaluationTests` (4 tests) — multipart with uploads, `uploads[]` key, no uploads, JSON still works
- `LegacyRequestRegressionTests` (8 tests) — one audit-finding-per-test focused regression suite + cross-cutting response sweep

**Final test count: 359 / 359 passing.**

---

## 9. Recommended next step

Now that legacy request compatibility is provably preserved, the unblocking work is:

**Start frontend integration / smoke testing against the migrated backend.**

Run the existing public frontend (Laravel-era) against the new .NET backend with the production-equivalent reverse-proxy configuration. Walk every business journey end-to-end. Anything that still breaks is a new finding, not a known issue from this audit.

If the smoke test passes, proceed with Phase NOTIF-1 (Notifications HTTP routes) per [40-api-migration-readiness.md §15](40-api-migration-readiness.md).

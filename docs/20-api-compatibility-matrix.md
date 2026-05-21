# API compatibility matrix

**Phase 0 deliverable 1.** Source-of-truth list of every Laravel route in the read-only `matloob-backoffice` repo and what we do with it in the new .NET API. **Frozen after sign-off; changes require explicit approval.**

## Legend

| Disposition | Meaning |
|---|---|
| **exact** | Same URL, same method, same request shape, same response shape. Behavior identical. |
| **compatible** | Same URL, same method. Minor internal differences (e.g. fields that referenced Ajeer disappear from response). Public-frontend impact: zero or one CSS class. |
| **redesigned** | Same business intent, different URL or shape. Public/admin frontend must update. |
| **removed** | Endpoint deleted. Callers must drop the call. |
| **new** | Brand-new endpoint, no Laravel equivalent. |

| Consumer | Who calls this route |
|---|---|
| **PF** | Public frontend (Angular SPA in another repo) — the contract we protect |
| **EF** | Establishments frontend (if a separate SPA exists) — also a public client |
| **Filament** | Old admin panel (being replaced by new Angular admin) |
| **dev** | Dev-only / debug |
| **unknown** | Need confirmation — find a HAR file or grep the public-frontend repo |

> **Important.** The Laravel admin SSO callback (`/admin/auth/callback`) is the OIDC return URL for the Filament panel. The new Angular admin will register its own redirect URI in IdM; the Laravel callback is removed.

---

## Authentication & infrastructure

### `routes/api.php`

| Method | Laravel route | Controller | Request | Resource | Purpose | Consumer | Disposition | New endpoint | Notes |
|---|---|---|---|---|---|---|---|---|---|
| GET | `/api/init-data` | `InitDataController::__invoke` | — | 16 inline resources | Composite bootstrap of all lookup data + translations + settings | PF | **exact** | `GET /api/v1/init-data` | Response cached 5 min via IMemoryCache. Field-for-field parity. |
| POST | `/api/signed-storage-url` | `SignedStorageUrlController` | (Laravel built-in pre-signed S3) | URL DTO | Issue a pre-signed S3 upload URL | PF | **redesigned** | `POST /api/v1/assets` | Local-storage upload returns the asset GUID directly. No pre-sign concept. Public frontend must change from "PUT to S3 then PATCH with key" to "POST multipart, get GUID, PATCH with GUID." |
| GET | `/api/test/ping` | inline | — | `{status, message, time}` | Liveness probe | dev | **redesigned** | `GET /health` | Standard health-check endpoint. |
| GET | `/api/test/token` | inline | — | claim dump | Token-validation diagnostic | dev | **redesigned** | `GET /api/v1/me` | Returns the authenticated user's claims + local user row. |

### `routes/test_identity.php` (all dev-only diagnostics)

| Method | Laravel route | Purpose | Disposition |
|---|---|---|---|
| GET | `/api/test/ping` | Liveness | covered above |
| GET | `/api/test/auth-check` | Middleware probe | **removed** (covered by `/api/v1/me`) |
| GET | `/api/test/token-info` | Full claim dump | **redesigned** as a Development-only `GET /api/v1/dev/token-info` |
| GET | `/api/test/user-info` | Local user lookup probe | **removed** |
| GET | `/api/test/identity-server-connection` | IdM JWKS reachability | **redesigned** as part of `/health/ready` |
| POST | `/api/test/create-test-user` | Force-provision a user from token | **removed** — provisioning happens automatically in the JwtBearer events |

### `routes/web.php`

| Method | Laravel route | Controller | Purpose | Consumer | Disposition | New endpoint | Notes |
|---|---|---|---|---|---|---|---|
| GET | `/print-contract/{contract}` | `PrintContractController` (signed URL) | Render contract PDF | PF | **removed** | — | Contracts entity deleted (Ajeer dependency). See Ajeer disposition doc. |
| GET | `/admin/auth/callback` | `Admin\Auth\SsoCallbackController` | Filament OIDC callback | Filament | **removed** | — | The Angular admin owns its own OIDC callback URL. |
| GET | `/change-offer-status-to-waiting-for-evaluation` | inline | Dev tool | dev | **removed** | — | |
| GET | `/reset-database` | inline | Dev DB wipe | dev | **removed** | — | A dedicated `dotnet ef database drop` is the replacement; no web endpoint needed. |
| GET | `/up` | Laravel built-in | Health | infra | **redesigned** | `GET /health` | |

---

## User-side API — `routes/users.php` (33 routes)

All gated by `identity.auth` + `identity.role:matloob_user`. New equivalent: `[Authorize(Policy = Policy.User)]`.

### Auth

| Method | Laravel route | Controller | Request | Resource | Purpose | Consumer | Disposition | New endpoint | Notes |
|---|---|---|---|---|---|---|---|---|---|
| POST | `/api/users/logout` | `LogoutController` | — | `{redirect_url}` | Terminate Passport tokens + Qiwa SSO logout redirect | PF | **compatible** | `POST /api/v1/users/logout` | Passport is gone. New version revokes the IdM session via OIDC end_session and returns the post-logout URL. Response shape preserved. |

### Profile (10 routes)

| Method | Laravel route | Controller | Request | Resource | Purpose | Consumer | Disposition | New endpoint | Notes |
|---|---|---|---|---|---|---|---|---|---|
| GET | `/api/users/profile` | `ShowProfileController` | — | `UserResource` (`Users/Me/Profiles/`) | Show current user profile | PF | **exact** | `GET /api/v1/users/profile` | |
| GET | `/api/users/profile/establishment-list` | `IndexEstablishmentController` | — | `Users/Me/Profiles/EstablishmentResource` | Currently calls QiwaApi; lists establishments linked to the user | PF | **compatible** | `GET /api/v1/users/profile/establishment-list` | **PG-only.** Returns the user's active memberships in Approved/Suspended establishments. Response shape preserved. Empty array if the user has none — public frontend should show "register your first establishment" CTA (Q-PF-1). |
| PATCH | `/api/users/profile/personal-info` | `UpdatePersonalInfoController` | `UpdatePersonalInfoRequest` | `UserResource` | Update basic profile | PF | **exact** | `PATCH /api/v1/users/profile/personal-info` | |
| PATCH | `/api/users/profile/photo` | `UpdateUserPhotoController` | multipart `photo` | `UserResource` | Set profile photo | PF | **compatible** | `PATCH /api/v1/users/profile/photo` | Internally uploads to Assets API, stores asset GUID on `users.photo_asset_id`. Response shape preserved. |
| PATCH | `/api/users/profile/user-education` | `UpdateOrCreateUserEducationController` | `Users/Me/Profiles/UpdateOrCreateUserEducationRequest` | `UserResource` | Upsert education records | PF | **exact** | `PATCH /api/v1/users/profile/user-education` | |
| PATCH | `/api/users/profile/languages-skills` | `UpdateOrCreateUserSkillAndLanguageController` | `Users/Me/Profiles/UpdateOrCreateUserSkillAndLanguageRequest` | `UserResource` | Upsert languages + skills | PF | **exact** | `PATCH /api/v1/users/profile/languages-skills` | |
| PATCH | `/api/users/profile/user-experiences` | `UpdateOrCreateUserExperienceController` | `UserExperiences/UpdateOrCreateUserExperienceRequest` | `UserResource` | Upsert work experience | PF | **exact** | `PATCH /api/v1/users/profile/user-experiences` | |
| PATCH | `/api/users/profile/user-certificates` | `UpdateOrCreateUserCertificateController` | `UserCertificate/UpdateOrCreateUserCertificateRequest` | `UserResource` | Upsert certificates | PF | **exact** | `PATCH /api/v1/users/profile/user-certificates` | |
| PATCH | `/api/users/profile/interest` | `UpdateInterestController` | request | `UserResource` | Set user interests | PF | **exact** | `PATCH /api/v1/users/profile/interest` | |
| PATCH | `/api/users/profile/finish-onboarding` | `FinishOnboardingController` | — | `UserResource` | Flip `onboarded=true` | PF | **exact** | `PATCH /api/v1/users/profile/finish-onboarding` | |

### Opportunities (8 routes)

| Method | Laravel route | Controller | Request | Resource | Purpose | Consumer | Disposition | New endpoint | Notes |
|---|---|---|---|---|---|---|---|---|---|
| GET | `/api/users/opportunities` | `OpportunityController::index` | (query params) | `OpportunityResource::collection` | Browse public opportunities | PF | **exact** | `GET /api/v1/users/opportunities` | Filters: city, category, salary, dates. |
| POST | `/api/users/opportunities` | `OpportunityController::store` | request | `OpportunityResource` | Currently exists but **unclear use** — individual creating an opportunity? | unknown | **removed unless confirmed** | — | Q-PF-2: does the public frontend call this? If no, remove. |
| GET | `/api/users/opportunities/{opportunity}` | `OpportunityController::show` | — | `OpportunityResource` | Single opportunity | PF | **exact** | `GET /api/v1/users/opportunities/{id}` | |
| PUT/PATCH | `/api/users/opportunities/{opportunity}` | `OpportunityController::update` | request | `OpportunityResource` | Unclear | unknown | **removed unless confirmed** | — | Same as POST above. |
| DELETE | `/api/users/opportunities/{opportunity}` | `OpportunityController::destroy` | — | — | Unclear | unknown | **removed unless confirmed** | — | |
| POST | `/api/users/opportunities/{opportunity}/apply` | `ApplyForOpportunityController` | `Users/Opportunities/ApplyForOpportunityRequest` | `ApplicantResource` | Worker applies to an opportunity | PF | **exact** | `POST /api/v1/users/opportunities/{id}/apply` | |
| GET | `/api/users/opportunities/applications` | `IndexApplicationController` | (query params) | `ApplicantResource::collection` | Worker's own applications list | PF | **exact** | `GET /api/v1/users/opportunities/applications` | |
| GET | `/api/users/opportunities/applications/{applicant}` | `ShowApplicationController` | — | `ApplicantResource` | Application detail | PF | **exact** | `GET /api/v1/users/opportunities/applications/{applicantId}` | |

### Offers (received-side, 10 routes)

| Method | Laravel route | Controller | Request | Resource | Purpose | Consumer | Disposition | New endpoint | Notes |
|---|---|---|---|---|---|---|---|---|---|
| GET | `/api/users/offers` | `ReceivedOfferController::index` | (query params) | `OfferResource::collection` | Worker's incoming offers list | PF | **compatible** | `GET /api/v1/users/offers` | Response shape: drop any `ajeer_*` fields from the OfferResource. See Ajeer disposition doc. |
| GET | `/api/users/offers/{offer}` | `ReceivedOfferController::show` | — | `OfferResource` | Offer detail | PF | **compatible** | `GET /api/v1/users/offers/{id}` | Same `ajeer_*` field strip. |
| POST | `/api/users/offers` | `ReceivedOfferController::store` | — | — | Unclear (apiResource generated, probably unused) | unknown | **removed unless confirmed** | — | |
| PUT/PATCH | `/api/users/offers/{offer}` | `ReceivedOfferController::update` | — | — | Unclear | unknown | **removed unless confirmed** | — | |
| DELETE | `/api/users/offers/{offer}` | `ReceivedOfferController::destroy` | — | — | Unclear | unknown | **removed unless confirmed** | — | |
| POST | `/api/users/offers/{offer}/accept` | `Users/Offers/AcceptOfferController` | — | `OfferResource` | Worker accepts offer | PF | **compatible** | `POST /api/v1/users/offers/{id}/accept` | **Ajeer side-effect removed.** Offer status flips to `Accepted`; no Contract issuance. |
| POST | `/api/users/offers/{offer}/reject` | `Users/Offers/RejectOfferController` | `Base/RejectionRequest` | `OfferResource` | Worker rejects offer | PF | **compatible** | `POST /api/v1/users/offers/{id}/reject` | Ajeer side-effect (none in practice for reject) removed. |
| POST | `/api/users/offers/cancel` | `Users/Offers/CancelOfferController` | `Offers/CancelOfferRequest` | `OfferResource` | Worker requests offer cancellation | PF | **compatible** | `POST /api/v1/users/offers/cancel` | Ajeer reference in CancelOfferService gone — pure internal status transition. |
| POST | `/api/users/offers/{offer}/approve-cancellation` | `Users/Offers/AcceptOfferCancellationRequestController` | — | `OfferResource` | Worker approves a cancellation request the establishment opened | PF | **compatible** | `POST /api/v1/users/offers/{id}/approve-cancellation` | Internal-only state change. |
| POST | `/api/users/offers/{offer}/reject-cancellation` | `Users/Offers/RejectOfferCancellationRequestController` | `Base/RejectionRequest` | `OfferResource` | Worker rejects a cancellation request | PF | **compatible** | `POST /api/v1/users/offers/{id}/reject-cancellation` | Internal-only state change. |
| GET | `/api/users/offers/{offer}/other-evaluation` | `Users/Offers/ShowContractEvaluationController` | — | evaluation resource | View counterparty's evaluation of a contract | PF | **removed** | — | "Contract" no longer exists in the new system. The evaluation endpoint moves under `evaluations` slice scoped to offers. Confirm in Q-EVAL-1. |
| GET | `/api/users/offers/unevaluated` | `Users/Offers/ListUnevaluatedContractController` | — | offer resources | Completed offers awaiting evaluation | PF | **redesigned** | `GET /api/v1/users/offers/unevaluated` | Same intent, but "unevaluated *contract*" → "unevaluated *offer*" since Contracts are gone. |

### Evaluations (5 routes — apiResource)

| Method | Laravel route | Controller | Request | Resource | Purpose | Consumer | Disposition | New endpoint | Notes |
|---|---|---|---|---|---|---|---|---|---|
| GET | `/api/users/evaluations` | `Users/Evaluations/EvaluationController::index` | (query) | evaluation resource | List own evaluations | PF | **exact** | `GET /api/v1/users/evaluations` | |
| POST | `/api/users/evaluations` | `EvaluationController::store` | request | evaluation resource | Submit an evaluation | PF | **exact** | `POST /api/v1/users/evaluations` | |
| GET | `/api/users/evaluations/{evaluation}` | `EvaluationController::show` | — | evaluation resource | Show | PF | **exact** | `GET /api/v1/users/evaluations/{id}` | |
| PUT/PATCH | `/api/users/evaluations/{evaluation}` | `EvaluationController::update` | request | evaluation resource | Update | unknown | **exact unless confirmed unused** | `PATCH /api/v1/users/evaluations/{id}` | |
| DELETE | `/api/users/evaluations/{evaluation}` | `EvaluationController::destroy` | — | — | Delete | unknown | **removed unless confirmed used** | — | |

### Notifications (3 routes)

| Method | Laravel route | Controller | Purpose | Consumer | Disposition | New endpoint |
|---|---|---|---|---|---|---|
| GET | `/api/users/notifications` | `Users/Notification/IndexNotificationController` | List | PF | **exact** | `GET /api/v1/users/notifications` |
| POST | `/api/users/notifications/mark-as-read` | `MarkNotificationAsReadController` | Mark read | PF | **exact** | `POST /api/v1/users/notifications/mark-as-read` |
| GET | `/api/users/notifications/unread-count` | `UnreadNotificationsCountController` | Badge count | PF | **exact** | `GET /api/v1/users/notifications/unread-count` |

---

## Establishment-side API — `routes/establishments.php` (67 routes)

All gated by `identity.auth` + active membership on the URL-identified establishment. The legacy `X-Commissioner-UUID` header + `establishment.context` policy from the Laravel side has been **removed** in the new API — the establishment id is in the URL path (`/api/v1/establishments/{id}/...`) and the per-row check `EstablishmentMember.IsActive AND Establishment.Status ∈ {Approved, Suspended-for-reads}` runs inside each endpoint via the shared `MembershipChecks` helper.

### `/me` group — own establishment

#### Services (5 routes — apiResource)

| Method | Laravel route | Controller | Disposition | New endpoint |
|---|---|---|---|---|
| GET | `/api/establishments/me/services` | `Me/Services/ServiceController::index` | **exact** | `GET /api/v1/establishments/me/services` |
| POST | `/api/establishments/me/services` | `ServiceController::store` | **exact** | `POST /api/v1/establishments/me/services` |
| GET | `/api/establishments/me/services/{service}` | `ServiceController::show` | **exact** | `GET /api/v1/establishments/me/services/{id}` |
| PUT/PATCH | `/api/establishments/me/services/{service}` | `ServiceController::update` | **exact** | `PATCH /api/v1/establishments/me/services/{id}` |
| DELETE | `/api/establishments/me/services/{service}` | `ServiceController::destroy` | **exact** | `DELETE /api/v1/establishments/me/services/{id}` |

#### Products (5 routes — apiResource)

Same shape as Services. All **exact**. New paths under `/api/v1/establishments/me/products/...`.

#### Profile (7 routes)

| Method | Laravel route | Controller | Request | Resource | Purpose | Consumer | Disposition | New endpoint | Notes |
|---|---|---|---|---|---|---|---|---|---|
| GET | `/api/establishments/me/profile` | `Me/Profile/ShowProfileController` | — | `ProfileResource` (composite) | Establishment's own profile | EF | **compatible** | `GET /api/v1/establishments/me/profile` | Three legacy tables collapse into one; response shape preserved field-for-field. `seven_hundred_number` and `establishment_status` (Qiwa-mirror) fields drop out per O-2. |
| PATCH | `/api/establishments/me/profile/general-info` | `UpdateProfileGeneralInfoController` | `UpdateProfileGeneralInfoRequest` | `ProfileGeneralInfoResource` | Edit profile general info | EF | **redesigned** | `POST /api/v1/establishments/{id}/change-requests` then field updates | **Big change.** Approved establishments cannot edit-in-place — must go through ChangeRequest. Public frontend either submits a CR via the new endpoints OR for the in-Draft case, calls the new `PATCH /api/v1/establishments/registration/{id}/basic-info`. See Q-EST-1. |
| PATCH | `/api/establishments/me/profile/logo` | `UpdateProfileLogoController` | multipart | resource | Set establishment logo | EF | **compatible** | `PATCH /api/v1/establishments/me/profile/logo` | Uploads via Assets API; stores asset GUID. Considered a "non-critical" field — allowed to edit in place without ChangeRequest (confirm Q-EST-2). |
| PATCH | `/api/establishments/me/profile/contact-info` | `UpdateProfileContactInfoController` | `UpdateProfileContactInfoRequest` | `ProfileContactInfoResource` | Edit contact phone numbers | EF | **redesigned** | via ChangeRequest | Treated as a substantive change. |
| PATCH | `/api/establishments/me/profile/bank-account` | `UpdateProfileBankAccountController` | `UpdateProfileBankAccountRequest` | `BankAccountResource` | Edit bank account | EF | **compatible** | `PATCH /api/v1/establishments/me/profile/bank-account` | Bank accounts live in a separate table; not part of the onboarding doc / change-request flow. Edits apply immediately. |
| PATCH | `/api/establishments/me/profile/experience` | `UpdateProfileExperienceController` | request | resource | Update an existing experience record | EF | **exact** | `PATCH /api/v1/establishments/me/profile/experience/{id}` | |
| POST | `/api/establishments/me/profile/experience` | `StoreProfileExperienceController` | request | resource | Add a new experience record | EF | **exact** | `POST /api/v1/establishments/me/profile/experience` | |

#### Own opportunities (8 routes)

| Method | Laravel route | Controller | Disposition | New endpoint |
|---|---|---|---|---|
| GET | `/api/establishments/me/opportunities` | `Me/Opportunities/OpportunityController::index` | **exact** | `GET /api/v1/establishments/me/opportunities` |
| POST | `/api/establishments/me/opportunities` | `OpportunityController::store` | **exact** | `POST /api/v1/establishments/me/opportunities` |
| GET | `/api/establishments/me/opportunities/{opportunity}` | `OpportunityController::show` | **exact** | `GET /api/v1/establishments/me/opportunities/{id}` |
| PUT/PATCH | `/api/establishments/me/opportunities/{opportunity}` | `OpportunityController::update` | **exact** | `PATCH /api/v1/establishments/me/opportunities/{id}` |
| DELETE | `/api/establishments/me/opportunities/{opportunity}` | `OpportunityController::destroy` | **exact** | `DELETE /api/v1/establishments/me/opportunities/{id}` |
| PATCH | `/api/establishments/me/opportunities/{opportunity}/end` | `EndOpportunityController` | **exact** | `PATCH /api/v1/establishments/me/opportunities/{id}/end` |
| GET | `/api/establishments/me/opportunities/{opportunity}/applications` | `MyIndexOpportunityApplicationController` | **exact** | `GET /api/v1/establishments/me/opportunities/{id}/applications` |
| GET | `/api/establishments/me/applicants/{applicant}` | `Me/Opportunities/Applications/ShowApplicationController` | **exact** | `GET /api/v1/establishments/me/applicants/{id}` |

### Events (12 routes)

Gated by `can.manage.events`.

| Method | Laravel route | Controller | Disposition | New endpoint |
|---|---|---|---|---|
| GET | `/api/establishments/events` | `Events/EventController::index` | **exact** | `GET /api/v1/establishments/events` |
| POST | `/api/establishments/events` | `EventController::store` | **exact** | `POST /api/v1/establishments/events` |
| GET | `/api/establishments/events/{event}` | `EventController::show` | **exact** | `GET /api/v1/establishments/events/{id}` |
| PUT/PATCH | `/api/establishments/events/{event}` | `EventController::update` | **exact** | `PATCH /api/v1/establishments/events/{id}` |
| DELETE | `/api/establishments/events/{event}` | `EventController::destroy` | **exact** | `DELETE /api/v1/establishments/events/{id}` |
| PATCH | `/api/establishments/events/{event}/end` | `EndEventController` | **exact** | `PATCH /api/v1/establishments/events/{id}/end` |
| GET | `/api/establishments/events/{event}/drafted` | `ShowDraftedEventController` | **exact** | `GET /api/v1/establishments/events/{id}/drafted` |
| GET | `/api/establishments/events/joined-events` | `IndexJoinedEventController` | **exact** | `GET /api/v1/establishments/events/joined-events` |
| GET | `/api/establishments/events/types` | `Types/IndexEventTypeController` | **exact** | `GET /api/v1/establishments/events/types` |
| GET | `/api/establishments/events/suggested-locations` | `IndexSuggestedLocationController` | **exact** | `GET /api/v1/establishments/events/suggested-locations` |
| GET | `/api/establishments/events/suggested-attendees` | `IndexSuggestedAttendeeController` | **exact** | `GET /api/v1/establishments/events/suggested-attendees` |

### Public opportunities — browse, apply (8 routes)

| Method | Laravel route | Controller | Disposition | New endpoint |
|---|---|---|---|---|
| GET | `/api/establishments/opportunities` | `Opportunities/OpportunityController::index` | **exact** | `GET /api/v1/establishments/opportunities` |
| POST | `/api/establishments/opportunities` | `OpportunityController::store` | unclear — **removed unless confirmed** | — |
| GET | `/api/establishments/opportunities/{opportunity}` | `OpportunityController::show` | **exact** | `GET /api/v1/establishments/opportunities/{id}` |
| PUT/PATCH | `/api/establishments/opportunities/{opportunity}` | unclear — **removed unless confirmed** | — | — |
| DELETE | `/api/establishments/opportunities/{opportunity}` | unclear — **removed unless confirmed** | — | — |
| GET | `/api/establishments/opportunities/categories` | `Categories/IndexOpportunityCategoryController` | **exact** | `GET /api/v1/establishments/opportunities/categories` |
| POST | `/api/establishments/opportunities/{opportunity}/apply` | `Applications/StoreOpportunityApplicationController` | **exact** | `POST /api/v1/establishments/opportunities/{id}/apply` |
| GET | `/api/establishments/opportunities/applications` | `Applications/IndexOpportunityApplicationController` | **exact** | `GET /api/v1/establishments/opportunities/applications` |
| GET | `/api/establishments/opportunities/applications/{applicant}` | `Applications/ShowOpportunityApplicationController` | **exact** | `GET /api/v1/establishments/opportunities/applications/{id}` |

### Offers (establishment-side, 18 routes)

| Method | Laravel route | Controller | Request | Resource | Purpose | Consumer | Disposition | New endpoint | Notes |
|---|---|---|---|---|---|---|---|---|---|
| GET | `/api/establishments/received-offers` | `ReceivedOfferController::index` | (query) | `OfferResource::collection` | List | EF | **compatible** | `GET /api/v1/establishments/received-offers` | Strip `ajeer_*` fields. |
| POST | `/api/establishments/received-offers` | unclear | — | — | apiResource store, unlikely used | unknown | **removed unless confirmed** | — | |
| GET | `/api/establishments/received-offers/{offer}` | `ReceivedOfferController::show` | — | `OfferResource` | Show | EF | **compatible** | `GET /api/v1/establishments/received-offers/{id}` | |
| PUT/PATCH | `/api/establishments/received-offers/{offer}` | unclear | — | — | | unknown | **removed unless confirmed** | — | |
| DELETE | `/api/establishments/received-offers/{offer}` | unclear | — | — | | unknown | **removed unless confirmed** | — | |
| GET | `/api/establishments/sent-offers` | `SentOfferController::index` | (query) | `OfferResource::collection` | List sent | EF | **compatible** | `GET /api/v1/establishments/sent-offers` | |
| POST | `/api/establishments/sent-offers` | unclear | — | — | | unknown | **removed unless confirmed** | — | |
| GET | `/api/establishments/sent-offers/{offer}` | `SentOfferController::show` | — | `OfferResource` | Show sent | EF | **compatible** | `GET /api/v1/establishments/sent-offers/{id}` | |
| PUT/PATCH | `/api/establishments/sent-offers/{offer}` | unclear | — | — | | unknown | **removed unless confirmed** | — | |
| DELETE | `/api/establishments/sent-offers/{offer}` | unclear | — | — | | unknown | **removed unless confirmed** | — | |
| POST | `/api/establishments/offers/send` | `SendOfferController` | `SendOfferRequest` | `OfferResource` | Establishment sends offer to applicant | EF | **compatible** | `POST /api/v1/establishments/offers/send` | Strip Ajeer side-effects (eligibility prefetch is gone). |
| POST | `/api/establishments/offers/cancel` | `CancelOfferController` | `Offers/CancelOfferRequest` | `OfferResource` | Cancel offer | EF | **compatible** | `POST /api/v1/establishments/offers/cancel` | |
| GET | `/api/establishments/offers/unevaluated` | `Contracts/Evaluations/ListUnevaluatedContractController` | — | offers | Offers awaiting evaluation | EF | **redesigned** | `GET /api/v1/establishments/offers/unevaluated` | "Contract" wording removed. |
| GET | `/api/establishments/offers/ajeer/check-eligibility` | `Ajeer/CheckContractingEligibility` | — | DTO | Ajeer eligibility precheck | EF | **removed** | — | See Ajeer disposition doc. |
| GET | `/api/establishments/offers/pending-action` | `CancellationRequests/IndexOfferCancellationRequestController` | — | cancellation requests | Cancellations needing approval | EF | **exact** | `GET /api/v1/establishments/offers/pending-action` | |
| GET | `/api/establishments/offers/pending-invoice` | `Invoices/IndexPendingInvoiceOfferController` | — | offers | Accepted offers awaiting invoice issuance | EF | **removed** | — | Invoice concept gone. |
| POST | `/api/establishments/offers/{offer}/accept` | `Establishments/Offers/AcceptOfferController` | — | `OfferResource` | Accept offer | EF | **compatible** | `POST /api/v1/establishments/offers/{id}/accept` | No Ajeer contract issuance. |
| POST | `/api/establishments/offers/{offer}/reject` | `Establishments/Offers/RejectOfferController` | `Base/RejectionRequest` | `OfferResource` | Reject | EF | **compatible** | `POST /api/v1/establishments/offers/{id}/reject` | |
| POST | `/api/establishments/offers/{offer}/approve-cancellation` | `Establishments/Offers/CancellationRequests/AcceptOfferCancellationRequestController` | — | `OfferResource` | Approve user-opened cancel | EF | **compatible** | `POST /api/v1/establishments/offers/{id}/approve-cancellation` | |
| POST | `/api/establishments/offers/{offer}/reject-cancellation` | `Establishments/Offers/CancellationRequests/RejectOfferCancellationRequestController` | `Base/RejectionRequest` | `OfferResource` | Reject cancel | EF | **compatible** | `POST /api/v1/establishments/offers/{id}/reject-cancellation` | |
| GET | `/api/establishments/offers/{offer}/other-evaluation` | `Contracts/Evaluations/ShowContractEvaluationController` | — | evaluation | View counterparty's evaluation | EF | **redesigned** | `GET /api/v1/establishments/offers/{id}/other-evaluation` | Scoped to offer, not contract. |
| GET | `/api/establishments/offers/{offer}/pending-sponsor-approval` | `Sponsors/ShowPendingSponsorApprovalOfferController` | — | `OfferResource` | Sponsor's view of an offer awaiting approval | EF | **exact** | `GET /api/v1/establishments/offers/{id}/pending-sponsor-approval` | Sponsor flow stays — it's an internal state machine, not Ajeer. |
| POST | `/api/establishments/offers/{offer}/sponsor/accept` | `Sponsors/ApprovePendingApprovalOfferController` | — | `OfferResource` | Sponsor accepts | EF | **exact** | `POST /api/v1/establishments/offers/{id}/sponsor/accept` | |
| POST | `/api/establishments/offers/{offer}/sponsor/reject` | `Sponsors/RejectPendingApprovalOfferController` | `Base/RejectionRequest` | `OfferResource` | Sponsor rejects | EF | **exact** | `POST /api/v1/establishments/offers/{id}/sponsor/reject` | |

### Evaluations (5 routes — apiResource)

Same shape as user-side evaluations. All **exact** unless update/destroy are unused.

### Notifications (3 routes)

| Method | Laravel route | Controller | Disposition | New endpoint |
|---|---|---|---|---|
| GET | `/api/establishments/notifications` | `Notifications/IndexNotificationController` | **exact** | `GET /api/v1/establishments/notifications` |
| POST | `/api/establishments/notifications/mark-as-read` | `MarkNotificationAsReadController` | **exact** | `POST /api/v1/establishments/notifications/mark-as-read` |
| GET | `/api/establishments/notifications/unread-count` | `UnreadNotificationsCountController` | **exact** | `GET /api/v1/establishments/notifications/unread-count` |

### Invoices (3 routes) — REMOVED

| Method | Laravel route | Controller | Disposition |
|---|---|---|---|
| GET | `/api/establishments/invoices` | `IndexInvoiceController` | **removed** |
| GET | `/api/establishments/invoices/{invoice}` | `ShowInvoiceController` | **removed** |
| POST | `/api/establishments/invoices/issue` | `IssueInvoiceController` | **removed** |

### Contracts regulations (1 route)

| Method | Laravel route | Controller | Disposition | New endpoint | Notes |
|---|---|---|---|---|---|
| GET | `/api/establishments/contracts-regulations` | `ShowOfferRegulationController` | **compatible** | `GET /api/v1/establishments/offer-regulations` | The route currently returns offer/contract regulation text. Rename "contracts" → "offers" but keep the path as `/contracts-regulations` for compat if the public frontend hard-codes it. Q-PF-3. |

---

## New endpoints (no Laravel equivalent)

### EstablishmentOnboarding (user-side)

| Method | New endpoint | Notes |
|---|---|---|
| POST | `/api/v1/establishments/registration/drafts` | Start a draft establishment |
| PATCH | `/api/v1/establishments/registration/{id}/basic-info` | Edit Draft/Rejected establishment fields |
| POST | `/api/v1/establishments/registration/{id}/documents/authorization-letter` | Upload required doc |
| POST | `/api/v1/establishments/registration/{id}/documents/commercial-registration` | Upload required doc |
| POST | `/api/v1/establishments/registration/{id}/submit` | Submit Draft/Rejected for review |
| DELETE | `/api/v1/establishments/registration/{id}` | Discard Draft (only) |

### Establishment members (user-side)

| Method | New endpoint |
|---|---|
| GET | `/api/v1/establishments/my` |
| GET | `/api/v1/establishments/{id}` |
| GET | `/api/v1/establishments/{id}/members` |
| POST | `/api/v1/establishments/{id}/members` |
| PATCH | `/api/v1/establishments/{id}/members/{memberId}` |
| DELETE | `/api/v1/establishments/{id}/members/{memberId}` |

### EstablishmentChangeRequest (user-side)

| Method | New endpoint |
|---|---|
| POST | `/api/v1/establishments/{id}/change-requests` |
| PATCH | `/api/v1/establishments/{id}/change-requests/{crId}/basic-info` |
| POST | `/api/v1/establishments/{id}/change-requests/{crId}/documents/authorization-letter` |
| POST | `/api/v1/establishments/{id}/change-requests/{crId}/documents/commercial-registration` |
| POST | `/api/v1/establishments/{id}/change-requests/{crId}/submit` |
| DELETE | `/api/v1/establishments/{id}/change-requests/{crId}` |
| GET | `/api/v1/establishments/{id}/change-requests` |
| GET | `/api/v1/establishments/{id}/change-requests/{crId}` |

### Assets (replaces signed-storage-url)

| Method | New endpoint |
|---|---|
| POST | `/api/v1/assets` |
| GET | `/api/v1/assets/{id}` |
| GET | `/api/v1/assets/{id}/metadata` |
| GET | `/api/v1/assets/{id}/signed-url?expiresIn=600` |
| DELETE | `/api/v1/assets/{id}` |

### Admin endpoints (replace Filament)

| Method | New endpoint | Replaces |
|---|---|---|
| GET | `/api/v1/admin/establishments/pending-review` | new |
| GET | `/api/v1/admin/establishments/{id}/review` | new |
| GET | `/api/v1/admin/establishments/{id}/review-history` | new |
| POST | `/api/v1/admin/establishments/{id}/approve` | new |
| POST | `/api/v1/admin/establishments/{id}/reject` | new |
| POST | `/api/v1/admin/establishments/{id}/suspend` | new |
| POST | `/api/v1/admin/establishments/{id}/reinstate` | new |
| GET | `/api/v1/admin/establishments/change-requests/pending` | new |
| GET | `/api/v1/admin/establishments/change-requests/{id}` | new |
| POST | `/api/v1/admin/establishments/change-requests/{id}/approve` | new |
| POST | `/api/v1/admin/establishments/change-requests/{id}/reject` | new |
| GET | `/api/v1/admin/establishments` | Filament EstablishmentResource list |
| GET | `/api/v1/admin/establishments/{id}` | Filament EstablishmentResource view |
| GET | `/api/v1/admin/users` | Filament UserResource list |
| GET | `/api/v1/admin/users/{id}` | Filament UserResource view |
| GET/POST/PATCH/DELETE | `/api/v1/admin/admins` family | Filament AdminResource (also calls IdM Admin API to provision) |
| GET/POST/PATCH/DELETE | `/api/v1/admin/roles` family | Filament RoleResource (Spatie) |
| GET/POST/PATCH/DELETE | `/api/v1/admin/permissions` family | Filament PermissionResource (Spatie) |
| GET/POST/PATCH/DELETE | `/api/v1/admin/banks` family | Filament BankResource |
| GET/POST/PATCH/DELETE | `/api/v1/admin/languages` family | Filament LanguageResource |
| GET/POST/PATCH/DELETE | `/api/v1/admin/nationalities` family | Filament NationalityResource |
| GET/POST/PATCH/DELETE | `/api/v1/admin/seasons` family | Filament SeasonResource |
| GET/POST/PATCH/DELETE | `/api/v1/admin/settings` family | Filament SettingResource |
| GET | `/api/v1/admin/events` family | Filament EventResource |
| GET | `/api/v1/admin/dashboard/*` | Filament dashboard widgets (charts, counts) |
| GET | `/api/v1/admin/{entity}/{id}/audit-log` | Filament auditing relation managers |
| GET | `/api/v1/admin/establishments/export?format=xlsx` | Filament EstablishmentExporter |

---

## Open frontend-side questions

These need answers from the **public-frontend team** before Phase 8 sign-off. They are *not* blockers for Phase 1–7 (skeleton, infra, auth, init-data, assets, profile).

| # | Question |
|---|---|
| Q-PF-1 | `GET /api/users/profile/establishment-list` may return an empty array for users with no registered establishment. Does the public frontend render this gracefully? Does it need a "register your first establishment" CTA wired in? |
| Q-PF-2 | Are the `apiResource opportunities` POST/PUT/DELETE routes under `/api/users/opportunities/*` actually called by the public frontend? They look like apiResource defaults that may not be used. Confirm; if no, mark **removed**. |
| Q-PF-3 | Is `/api/establishments/contracts-regulations` hard-coded in the public frontend? If renamed to `/offer-regulations`, what breaks? |
| Q-PF-4 | Does the public frontend display `ajeer_contract_number` or `ajeer_contract_status` anywhere? If yes, what should it display now? *(My recommendation: hide the field; if the contract did exist, the offer status itself carries the meaning.)* |
| Q-PF-5 | The `signed-storage-url` flow → `POST /api/v1/assets` flow change is **breaking** for the public frontend. How quickly can the frontend team migrate? Is a temporary compat shim required during cutover? |
| Q-PF-6 | `POST /api/users/offers/{offer}/accept` currently returns the OfferResource including `contract` nested object. With Contracts removed, that nested object disappears. Anything in the public frontend rely on it? |
| Q-EVAL-1 | The evaluation flow attaches to "contracts" today. With Contracts removed, evaluations attach to Offers directly. Confirm the evaluation domain model. |
| Q-EST-1 | When `PATCH /api/establishments/me/profile/general-info` is called on an Approved establishment, should the response be (a) 405 "use change-request", (b) auto-create a ChangeRequest from the patch payload, or (c) only allow this endpoint when status=Draft and use ChangeRequest for Approved? *(My recommendation: (c) for clarity.)* |
| Q-EST-2 | Logo update — is logo a "non-critical" field that bypasses ChangeRequest? My default: yes. Confirm. |

---

## Counts summary

| Bucket | Count |
|---|---|
| Routes inspected (Laravel) | ~109 (4 web + 8 api/test + 33 users + 64 establishments) |
| **exact** | ~58 |
| **compatible** | ~22 |
| **redesigned** | ~10 |
| **removed** | ~12 (Ajeer-related + Filament SSO + dev routes + signed-storage-url replaced) |
| **unknown — needs confirmation** | ~12 (apiResource POST/PUT/DELETE that look auto-generated but unused) |
| **new** | ~50 (EstablishmentOnboarding + ChangeRequest + Assets + Admin replacement) |

— end —

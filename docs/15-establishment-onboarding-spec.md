# Establishment Onboarding — business + technical specification

**Status:** confirmed by team (H1–H11 answered).
**Audience:** backend, frontend, admin UX, data migration.
**Stable until:** Phase 8 begins. Changes after that require explicit sign-off.

---

## 1. Goal

Replace the legacy Qiwa-driven establishment discovery with a **manual self-registration + admin-review** workflow. PostgreSQL is the single source of truth.

A logged-in user creates a draft establishment, fills in business details, uploads two required documents, and submits for admin review. After approval, the creator becomes the first Owner and may add other members. Edits to an approved establishment go through a separate **change-request** flow that does **not** disrupt operations.

---

## 2. Status lifecycle

```
                                                  edit
                                          ┌── ChangeRequest ──┐
                                          ▼                   │
                              ┌──── Approved ◄────────────┐   │
                              │       │                   │   │
                              │       │ suspend           │   │
   create draft   submit      │       ▼                   │   │
       │           │          │   Suspended ─ reinstate ──┘   │
       ▼           ▼          │       ▲                       │
     Draft ─── PendingReview ─┤       │                       │
       ▲           ▲          │   admin suspends              │
       │       reject         │                               │
   resubmit       │           │                               │
       │          ▼           ▼                               │
       └────── Rejected   approved                            │
                          establishment ────────── edits ─────┘
                          (operations OK)         via ChangeRequest
```

| State | User edits? | Operational? | Notes |
|---|---|---|---|
| Draft | yes | no | Private to creator |
| PendingReview | no (locked) | no | Admin queue |
| Approved | no (via ChangeRequest only) | yes | Creator becomes first Owner |
| Rejected | yes (edit + resubmit) | no | Resubmit → PendingReview |
| Suspended | no | reads only — writes blocked | Members get 423 Locked on mutations |

Rejected establishments are **never** auto-deleted. The same row transitions back to PendingReview on resubmit.

---

## 3. Establishment fields

### 3.1 Required at submit-for-review

These nine items must be present and valid before `SubmitEstablishmentForReview` succeeds:

| Field | Type | Notes / validation |
|---|---|---|
| `Name` | string, 2–255 | Business legal name |
| `CommercialRegistrationNumber` | string, ≤ 50 | Unique across PendingReview/Approved/Suspended (see §4) |
| `LaborOfficeId` | string, ≤ 50 | Legacy Qiwa equivalent: `labor_office_id` |
| `SequenceNumber` | string, ≤ 50 | Legacy Qiwa equivalent: `sequence_number` |
| `City` | string, ≤ 100 | **Free text in v1** *(confirmed O-3)*. Promotion to FK on `cities` lookup is deferred to a later release. |
| `Email` | string, valid email | Establishment contact email |
| `Phone` | string, ≤ 30 | Legacy: `contact_number` on `establishment_contact_infos` |
| `AuthorizationLetter` document | file asset | Uploaded via Assets API; one active per establishment |
| `CommercialRegistration` document | file asset | Uploaded via Assets API; one active per establishment |

### 3.2 Optional at any time

| Field | Type | Notes |
|---|---|---|
| `CommercialRegistrationExpiry` | date | Recommended to require by admin policy; not enforced in v1 |
| `EconomicActivity` | string | e.g. "Events Management" |
| `SubEconomicActivity` | string | |
| `District` | string | Maps to legacy `neighborhood` |
| `Area` | string | |
| `Street` | string | Maps to legacy `street_name` |

### 3.3 Found in legacy system — recommendation per field

The Laravel schema and the legacy Qiwa DTO carried more fields than the team's H1 list. Each is listed below with a recommendation:

| Legacy field | Legacy source | Recommendation | Rationale |
|---|---|---|---|
| `description` | `establishment_profiles` | **Include as Optional** | Used by the public frontend to render establishment profiles; trivial to keep |
| `LocationTitle` (`location_title`) | `establishment_profiles` | **Include as Optional** | Human-readable place name; goes with lat/lon |
| `Latitude`, `Longitude` | `establishment_profiles.lat`/`lon` | **Include as Optional** | Used for map pins; cheap to carry |
| `BuildingNumber` | `establishment_profiles` | **Include as Optional** | Saudi addressing standard component |
| `PostalCode` | `establishment_profiles` | **Include as Optional** | Saudi addressing standard component |
| `AdditionalNumber` | `establishment_profiles` | **Include as Optional** | Saudi 4-digit "additional" address number |
| `Website` | `establishment_profiles` | **Include as Optional** | Used in profile display |
| `EstablishmentStatus` (Qiwa's own status string — `"Active"`, etc.) | `establishment_profiles` + `EstablishmentInfoDTO` | **Drop** *(confirmed O-2)* | Was a mirror of Qiwa's status. Our internal `Status` enum replaces it. |
| `SevenHundredNumber` | `establishment_profiles` | **Drop** *(confirmed O-2)* | Saudi legacy "700-number" identifier — no consumer in the new flow. |
| `YearsOfExperience` | `establishment_profiles` | **Include as Optional, default 0** | Used in public profile |
| `EstablishmentSize` (Small / Medium / Large) | `establishment_profiles` | **Include as Optional** | Used as a public-profile filter |
| `AdditionalContactNumber` | `establishment_contact_infos` | **Include as Optional** | Two-phone-number support is cheap; keep |
| `IsSponsor` | `establishments` | **Keep — admin-controlled, not user-entered** | Set by admins post-approval, gates sponsor-only flows |
| `CanManageEvents` | `establishments` | **Keep — admin-controlled, not user-entered** | Set by admins post-approval, gates event-management routes |
| `ProfileCompleted` flag | `establishments` | **Drop as a stored column; compute on read** | Derived from "all 3.1-required fields populated AND both documents present" |
| Bank account (`bank_accounts` polymorphic) | separate table | **Out of onboarding scope** | A separate `EstablishmentBankAccount` entity, edited *after* approval, not part of the registration flow |
| Establishment experience (`establishment_experiences`) | separate table | **Out of onboarding scope** | 1-to-many record edited post-approval |

### 3.4 Data model consolidation

The Laravel side splits establishment data across three tables: `establishments`, `establishment_profiles`, `establishment_contact_infos`. In PostgreSQL we **collapse those three tables into one `establishments` table**. The split was a Laravel-relations idiom; it adds no value with EF Core and complicates the change-request flow (which would otherwise need to track changes across three rows).

Bank accounts and experiences stay in their own tables (legitimate 1-to-many).

---

## 4. CommercialRegistrationNumber uniqueness

Partial unique index:

```sql
CREATE UNIQUE INDEX ux_establishment_cr_active
ON establishments (commercial_registration_number)
WHERE status IN ('PendingReview', 'Approved', 'Suspended')
  AND is_deleted = false;
```

**Rules:**
- Two `Draft` establishments may carry the same CR number — no conflict until submission.
- On `Submit`, the validator does a pre-flight uniqueness check and rejects with `409 cr_number_in_use` if another active establishment claims it.
- On `Reject`, the index automatically frees the CR number (status leaves the watched set).
- On `Approve`, the value becomes locked-in.
- `Rejected` and `Draft` do not block reuse (per H10).

**Edge case** — what if user A's Draft has CR `X` and user B's PendingReview also has CR `X`? Both inserts succeed (Draft is not in the index), but A cannot submit until B is rejected (or A changes the CR number). Validator surface error message: "Commercial registration number is already pending review or approved on another establishment."

---

## 5. Documents

Both required documents are stored via the local **Assets API** and referenced by GUID.

| Document type | Allowed MIME | Max size |
|---|---|---|
| `AuthorizationLetter` | `application/pdf`, `image/jpeg`, `image/png` | 10 MB |
| `CommercialRegistration` | `application/pdf`, `image/jpeg`, `image/png` | 10 MB |

**Rules:**
- One **active** document of each type per establishment (partial unique index on `(establishment_id, document_type) WHERE is_deleted = false`).
- Re-uploading replaces the previous one — old `EstablishmentDocument` row soft-deletes; the underlying `Asset` row also soft-deletes (orphan cleanup nightly).
- Document `Visibility` = `private`. Download authorized for:
  - Establishment Owner / Member, OR
  - Admin with `establishments.review` permission.
- Re-upload during a change-request stores the proposed document on the ChangeRequest row; the establishment's live document is replaced only on approval.

---

## 6. Members

### 6.1 Roles

```
Owner | Manager | HR | Accountant | Commissioner | Other
```

### 6.2 Rules

- The user who created the draft becomes the **first Owner** automatically upon admin approval. They are inserted into `establishment_members` with `Role=Owner`, `IsActive=true`, `AddedAt=ApprovedAt`.
- An Owner can add other members. The added user must:
  - Already exist in the local `users` table (i.e. they have authenticated through IdM at least once).
  - Not already be an active member of this establishment.
- **Addition is immediate.** No invitation, no acceptance step.
- An establishment may have **multiple Owners**. The system enforces only one rule: there must always be at least one active Owner. Validators block the removal/deactivation/role-change of the *last* Owner with `409 last_owner_protected`.
- A user can be a member of **multiple establishments** with no cap.
- A user can be the creator/Owner of **multiple establishments**.
- Deactivation (`IsActive=false`) is preferred over hard removal; both are supported. Soft-delete cascade on removal.

### 6.3 Out-of-band invitations

Not in scope for v1. Adding a user who has never logged in returns `422 user_not_found_in_system`.

---

## 7. Edit-after-approval — ChangeRequest design

**Approved establishments are edited via `EstablishmentChangeRequest`.** The live establishment continues operating during review.

### 7.1 ChangeRequest lifecycle

```
Draft → PendingReview → Approved (changes applied) | Rejected (changes discarded) | Cancelled (by submitter)
```

Only one PendingReview ChangeRequest per Establishment at a time (partial unique index).

### 7.2 Fields

Each `Proposed*` column on the ChangeRequest holds the new value, or NULL if the field is unchanged. The two `Proposed*AssetId` columns hold the new document asset id if either document is being re-uploaded.

### 7.3 Behavior on approve

1. For each non-null `Proposed*` field, set the matching field on `Establishment`.
2. If a proposed document asset is present, soft-delete the current `EstablishmentDocument` row of that type and insert a new one pointing at the new asset.
3. Set `AppliedAt = now`, `Status = Approved`.
4. Append to `EstablishmentReviewHistory`.
5. Raise `EstablishmentChangeRequestApproved` event.

### 7.4 Behavior on reject

1. Set `Status = Rejected`, record `ReviewReason`.
2. Proposed document assets become orphans; a nightly job soft-deletes them.
3. Establishment data unchanged.

### 7.5 What can be edited

All §3.1 fields (except documents) and all §3.2 optional fields. Documents can be re-uploaded as part of a change request. `IsSponsor` and `CanManageEvents` are **admin-only** flags — edited directly on the Establishment, never through ChangeRequest.

---

## 8. Suspended behavior

When admin suspends an Approved establishment:

- `Status = Suspended`, `SuspendedAt`, `SuspendedByAdminId`, `SuspensionReason` populated.
- All read endpoints continue to work for members and admins.
- All mutation endpoints (members add/remove, change-request submit, services/products CRUD, opportunities CRUD, offers send/accept/etc.) return **`423 Locked`** with `code: establishment_suspended`.
- Existing pending offers/opportunities/applications **freeze in their current state** — they are **not** auto-cancelled *(confirmed O-1)*. On reinstate, all in-flight work resumes exactly as it was.

Reinstate flips `Status` back to `Approved` and nulls the suspension fields. Reinstate is admin-only.

---

## 9. Draft discard

`DELETE /api/v1/establishments/registration/{id}` succeeds **only** when `Status=Draft`. Other statuses return `409 cannot_delete`.

Soft-delete cascade: `Establishment` → `EstablishmentDocument` → `Asset` (the assets only become orphan candidates because they're now soft-deleted; the daily cleanup job purges files from disk after 30 days).

---

## 10. Authorization

| Action | Required principal |
|---|---|
| Create/edit/submit Draft | Authenticated user (creator) |
| Read own establishment (any status) | Creator OR active member |
| Add/remove/update member | Active Owner of that establishment |
| Submit ChangeRequest | Active Owner |
| Cancel ChangeRequest | Submitter OR any active Owner |
| Read pending-review queue | `matloob_admin` + permission `establishments.review` |
| Approve / Reject onboarding or ChangeRequest | `matloob_admin` + permission `establishments.review` |
| Suspend / Reinstate | `matloob_admin` + permission `establishments.suspend` |

---

## 11. Events raised

| Event | Raised by | Typical handlers |
|---|---|---|
| `EstablishmentDraftCreated` | `CreateEstablishmentDraft` | (informational only) |
| `EstablishmentSubmittedForReview` | `SubmitEstablishmentForReview` | Notify all `matloob_admin` users |
| `EstablishmentApproved` | `ApproveEstablishment` | Create the first Owner member from `CreatedByUserId`; notify creator |
| `EstablishmentRejected` | `RejectEstablishment` | Notify creator with reason |
| `EstablishmentSuspended` | `SuspendEstablishment` | Notify all members; cancel any in-flight commissioner sessions |
| `EstablishmentReinstated` | `ReinstateEstablishment` | Notify all members |
| `EstablishmentMemberAdded` | `AddEstablishmentMember` | Notify the added user |
| `EstablishmentMemberRemoved` | `RemoveEstablishmentMember` | Notify removed user |
| `EstablishmentChangeRequestSubmitted` | `SubmitChangeRequest` | Notify admins |
| `EstablishmentChangeRequestApproved` | `ApproveChangeRequest` | Notify all establishment members |
| `EstablishmentChangeRequestRejected` | `RejectChangeRequest` | Notify the submitter |

---

## 12. Data migration from Laravel

Existing rows are migrated as follows:

### 12.1 Establishments

- All existing `establishments` join `establishment_profiles` + `establishment_contact_infos` into the new single `establishments` table.
- `Status = Approved` (grandfathered).
- `IsLegacyImport = true` — flags rows that may not have AuthorizationLetter / CommercialRegistration documents. The UI shows a banner.
- `ApprovedAt = the original establishments.created_at`.
- `ApprovedByAdminId = NULL` (legacy data has no traceable approver).
- `CreatedByUserId = earliest commissioner's user_id`. If no commissioner exists, `NULL` — admin will need to assign post-migration.

### 12.2 Commissioners → EstablishmentMembers

Per H11:

1. Order commissioners per establishment by `created_at ASC`.
2. **First** commissioner → `Role = Owner`, `IsActive = true`.
3. **All others** → `Role = Commissioner`, `IsActive = true`.
4. `AddedAt = commissioners.created_at`. `AddedByUserId = NULL` (legacy).
5. Soft-deleted commissioners migrate with `IsDeleted = true`.

### 12.3 Documents

Legacy `media` records that point at establishment files do **not** map to either of our two required document types (the legacy system never required them). They migrate as ordinary `Asset` rows with `Purpose = legacy_media`. The two required document slots remain empty until/unless the establishment uploads them later.

### 12.4 Review history

For each migrated establishment, insert one synthetic `EstablishmentReviewHistory` row with `Action = DraftCreated` and `Action = Approved`, `ActorAdminId = NULL`, `Reason = "Migrated from legacy system"`.

### 12.5 Duplicate CommercialRegistrationNumber pre-flight *(confirmed O-6)*

The partial unique index on `commercial_registration_number` would block migration if the legacy data contains duplicates. The migration tool **fails loudly** rather than auto-resolving:

1. Before any insert, run a pre-flight query that groups legacy `establishment_profiles.cr_number` by value and flags any with `count > 1`.
2. If duplicates exist, the migration tool writes `migration-duplicate-cr-report.csv` listing every conflicting row (legacy `establishments.id`, `name`, `cr_number`, `created_at`, commissioner count, last activity timestamp) and **exits non-zero without touching the target database**.
3. The team manually resolves each conflict in the legacy data (or supplies a `duplicate-cr-resolution.csv` mapping legacy id → action: `keep | suspend | exclude`), then re-runs.
4. No silent fix-ups. No "first wins" auto-selection. No "rename to `cr_number_2`" hacks.

This is the only legacy-data quality gate that blocks migration; all other inconsistencies are recorded in reports but do not halt the run.

---

## 13. Validators (FluentValidation summary)

### `CreateEstablishmentDraftValidator`
- Authenticated user.

### `UpdateEstablishmentBasicInfoValidator`
- Status ∈ {Draft, Rejected}.
- Field-level: Name 2–255, Email valid email, Phone ≤ 30 chars, all string fields ≤ 255 unless noted.

### `UploadDocumentValidator` (both types)
- Status ∈ {Draft, Rejected}.
- MIME ∈ allowed list; size ≤ 10 MB.

### `SubmitEstablishmentForReviewValidator`
- Status ∈ {Draft, Rejected}.
- All §3.1 fields populated and valid.
- Both documents present and not soft-deleted.
- CR number unique across PendingReview/Approved/Suspended (excluding self).

### `AddEstablishmentMemberValidator`
- Caller is active Owner of this establishment.
- Target user exists in local `users` table (`users.identity_id IS NOT NULL`).
- Target user is not already an active member.
- Role is one of the allowed enum values.

### `RemoveEstablishmentMemberValidator` / `UpdateMemberRoleValidator`
- Caller is active Owner.
- If the target is an active Owner and is the **last** active Owner, reject with `409 last_owner_protected`.

### `ApproveEstablishmentValidator`
- Status = PendingReview.
- Both documents present.

### `RejectEstablishmentValidator`
- Status = PendingReview.
- Reason non-empty.

### `SuspendEstablishmentValidator` / `ReinstateEstablishmentValidator`
- Caller has `establishments.suspend` permission.
- Status precondition matches.
- Reason non-empty (suspend only).

### `SubmitChangeRequestValidator`
- Caller is active Owner of an Approved establishment.
- No existing PendingReview ChangeRequest.
- At least one `Proposed*` field is non-null OR at least one proposed document is set.
- CR number uniqueness check if changing CR number.

---

## 14. API surface (consolidated)

### User side

| Method | Path |
|---|---|
| POST | `/api/v1/establishments/registration/drafts` |
| PATCH | `/api/v1/establishments/registration/{id}/basic-info` |
| POST | `/api/v1/establishments/registration/{id}/documents/authorization-letter` |
| POST | `/api/v1/establishments/registration/{id}/documents/commercial-registration` |
| POST | `/api/v1/establishments/registration/{id}/submit` |
| DELETE | `/api/v1/establishments/registration/{id}` *(Draft only)* |
| GET | `/api/v1/establishments/my` |
| GET | `/api/v1/establishments/{id}` |
| GET | `/api/v1/establishments/{id}/members` |
| POST | `/api/v1/establishments/{id}/members` |
| PATCH | `/api/v1/establishments/{id}/members/{memberId}` |
| DELETE | `/api/v1/establishments/{id}/members/{memberId}` |
| POST | `/api/v1/establishments/{id}/change-requests` |
| PATCH | `/api/v1/establishments/{id}/change-requests/{crId}/basic-info` |
| POST | `/api/v1/establishments/{id}/change-requests/{crId}/documents/authorization-letter` |
| POST | `/api/v1/establishments/{id}/change-requests/{crId}/documents/commercial-registration` |
| POST | `/api/v1/establishments/{id}/change-requests/{crId}/submit` |
| DELETE | `/api/v1/establishments/{id}/change-requests/{crId}` *(submitter or Owner, Draft only)* |
| GET | `/api/v1/establishments/{id}/change-requests` |
| GET | `/api/v1/establishments/{id}/change-requests/{crId}` |

### Admin side

| Method | Path |
|---|---|
| GET | `/api/v1/admin/establishments/pending-review` |
| GET | `/api/v1/admin/establishments/{id}/review` |
| GET | `/api/v1/admin/establishments/{id}/review-history` |
| POST | `/api/v1/admin/establishments/{id}/approve` |
| POST | `/api/v1/admin/establishments/{id}/reject` |
| POST | `/api/v1/admin/establishments/{id}/suspend` |
| POST | `/api/v1/admin/establishments/{id}/reinstate` |
| GET | `/api/v1/admin/establishments/change-requests/pending` |
| GET | `/api/v1/admin/establishments/change-requests/{id}` |
| POST | `/api/v1/admin/establishments/change-requests/{id}/approve` |
| POST | `/api/v1/admin/establishments/change-requests/{id}/reject` |

### Compat shim

| Method | Path | Disposition |
|---|---|---|
| GET | `/api/users/profile/establishment-list` | **Compat shape, PG-only.** Returns the user's active memberships in Approved/Suspended establishments. |

---

## 15. Angular admin coverage

| Page | Route |
|---|---|
| Pending new registrations | `/admin/establishments/pending-review` |
| Establishment review details | `/admin/establishments/{id}/review` |
| Pending change requests | `/admin/establishments/change-requests/pending` |
| ChangeRequest review (with diff view) | `/admin/establishments/change-requests/{id}` |
| Approved + Suspended establishments | `/admin/establishments` |
| Establishment detail (admin) | `/admin/establishments/{id}` |
| Members management (admin) | `/admin/establishments/{id}/members` |

Shared components: `DocumentViewer`, `DiffViewer`, `StatusBadge`, `ReviewActionsBar`.

---

## 16. Open items still pending team confirmation

Confirmed (closed):
- ~~O-1 Suspension auto-cancel policy~~ → **Freeze, no auto-cancel.** Baked into §8.
- ~~O-2 Drop `SevenHundredNumber` and `EstablishmentStatus`~~ → **Dropped.** Baked into §3.3.
- ~~O-3 `City` as FK or free text~~ → **Free text in v1.** Baked into §3.1.
- ~~O-6 Duplicate `CommercialRegistrationNumber` migration policy~~ → **Fail loudly + generate cleanup report; no silent auto-fix.** Baked into §12.

Still open (carried into the main blueprint's remaining questions):

- **O-4** Production storage location for Assets (local disk vs S3-second-impl). Affects whether the `IFileStorage` second implementation is actively built or just designed for.
- **O-5** Deployment target (Linux container vs IIS), managed Postgres host, IdM client URL for the admin Angular app.

---

## 17. Sign-off

**Phase 8 (EstablishmentOnboarding implementation) is unblocked** once Phase 0 (API matrix) sign-off completes.

H1–H11 confirmed; O-1, O-2, O-3, O-6 confirmed. Only O-4 (storage) and O-5 (deployment) remain — both are infrastructure decisions that do not block design or implementation of the onboarding feature itself.

— end —

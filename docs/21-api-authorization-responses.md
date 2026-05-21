# API authorization responses — establishment module

Short cheat-sheet for the public frontend. Covers the response codes the
Phase 7–8E establishment endpoints actually emit and the rules behind
them. Pair with `docs/20-api-compatibility-matrix.md` for the per-route
inventory.

---

## 1. Status codes the establishment module can return

| Code | Meaning | When |
|---|---|---|
| **200** | OK | Read or successful mutation that returns a body |
| **201** | Created | Mutation that inserts a new row; carries a `Location` header |
| **204** | No Content | Successful mutation with no body (delete, cancel, simple flips) |
| **400** | Bad Request | Validation failure: malformed input, empty required field, oversized file, etc. |
| **401** | Unauthorized | No bearer token, or token is invalid / expired |
| **403** | Forbidden | Authenticated but the caller's role / membership doesn't allow this action |
| **404** | Not Found | Row doesn't exist **OR** caller has no read grant on it — see §3 |
| **409** | Conflict | Lifecycle / uniqueness rule rejects the request — see §4 |
| **413** | Payload Too Large | Asset upload exceeds `Storage:MaxUploadBytes` (default 10 MB) |
| **423** | Locked | Establishment is `Status = Suspended` and the action is a mutation — see §5 |

---

## 2. Anonymous vs authenticated

Every establishment endpoint requires authentication. There are **zero**
`AllowAnonymous` paths. An anonymous caller gets **401** on any route in
`/api/v1/establishments/*`, `/api/v1/admin/establishments/*`, and
`/api/v1/establishments/registration/*`.

The only `AllowAnonymous` endpoints in the API are out of scope of this
doc (`/api/v1/init-data`, `/api/init-data`, asset download for `Public`
visibility assets).

---

## 3. 403 vs 404 — when authorized callers can't see a row

The module deliberately splits the two cases:

| Endpoint family | Unauthorized caller gets |
|---|---|
| `GET /api/v1/establishments/{id}` (user-side details) | **404** |
| `GET /api/v1/establishments/{id}/members` | **403** (after caller learns the id exists from list-mine) |
| `GET /api/v1/admin/establishments/{id}/review` | **404** only if the establishment row actually doesn't exist |
| Any user-side mutation on `/api/v1/establishments/{id}/...` | **403** when authenticated but not authorized |

**Why the asymmetry on details:** the user-side details endpoint is the
only place where a stranger holding a guessed UUID could enumerate ids.
Returning 404 instead of 403 makes "I'm not allowed to see this" and
"this id doesn't exist" indistinguishable from outside, defeating the
enumeration leak. The admin endpoints don't need this because admin role
is a much higher bar.

Frontend implication: don't try to disambiguate 404 on `GET
/api/v1/establishments/{id}`. Treat it as "the user can't see this row
for whatever reason" and route them back to their list.

---

## 4. 409 — conflict codes

Every 409 response carries a machine-readable `code` in the
ProblemDetails extension object. Switch on `code`, not on the human
`detail` text.

```json
{
  "status": 409,
  "title": "Conflict",
  "detail": "...human text...",
  "type": "https://httpstatuses.io/409",
  "code": "cr_number_in_use"
}
```

| `code` | Triggered by | Action for the user |
|---|---|---|
| `cannot_edit_in_status` | Lifecycle gate (e.g. editing Approved establishment without ChangeRequest) | Refresh; show the appropriate next step for the new status |
| `cannot_delete` | Discard tried on a non-Draft establishment | Hide the discard button outside `Status=Draft` |
| `cr_number_in_use` | Commercial registration number is already taken by another active establishment | Edit + resubmit with a different number |
| `change_request_already_exists` | One in-flight ChangeRequest already exists (Draft or PendingReview) | Open the existing one instead of creating a second |
| `change_request_empty` | Submit attempt on a ChangeRequest with no proposed fields | Make at least one edit before submitting |
| `document_missing` | Submit-for-review without one or both required documents | Upload the missing slot(s) |
| `document_slot_already_exists` | Two concurrent re-links raced; DB index settled it | Refresh and try again — the other writer won |
| `member_already_exists` | User is already an active member of the establishment | (Both AddMember pre-flight and DB-level race trigger this) |
| `last_owner_protected` | Attempt to demote / deactivate / remove the only active Owner | Add a second Owner first |
| `asset_purpose_mismatch` | Linked Asset's `Purpose` doesn't match the slot | Re-upload with the correct purpose |
| `asset_not_found` | Asset id is unknown or already soft-deleted | Re-upload |
| `asset_not_owned_by_caller` | Caller didn't upload the Asset (and isn't admin) | Caller must upload the file themselves |
| `establishment_suspended` | Mutation on a Suspended establishment | See §5 |

The set is stable. New codes can be added; existing ones won't be renamed without a deprecation window.

---

## 5. 423 Locked — suspended establishments

When an establishment has `Status = Suspended` (set by admin via `POST
/api/v1/admin/establishments/{id}/suspend`):

**Allowed:**
- All reads: `GET /api/v1/establishments`, `GET /api/v1/establishments/{id}`,
  `GET /api/v1/establishments/{id}/members`, admin queues / review /
  history, asset metadata + download.
- ChangeRequest **cancel** (`DELETE
  /api/v1/establishments/{id}/change-requests/{crId}`) — explicitly
  allowed because cancellation reduces pending work without mutating the
  live establishment row.

**Blocked with 423 + `code: establishment_suspended`:**
- `POST /api/v1/establishments/{id}/members`
- `PATCH /api/v1/establishments/{id}/members/{memberId}`
- `DELETE /api/v1/establishments/{id}/members/{memberId}`
- `POST /api/v1/establishments/{id}/change-requests`
- `PATCH .../change-requests/{crId}/basic-info`
- `POST .../change-requests/{crId}/documents/authorization-letter`
- `POST .../change-requests/{crId}/documents/commercial-registration`
- `POST .../change-requests/{crId}/submit`

In-flight rows (member rows, pending ChangeRequest in Draft, etc.) are
**not auto-cancelled** — they freeze in place. When the admin
reinstates, writes unblock and rows resume in their existing state.

Onboarding-registration endpoints (`/api/v1/establishments/registration/*`)
are not affected because they target Draft / Rejected / PendingReview
establishments — a Suspended row can never be reached from there.

---

## 6. Quick lookup — endpoint × auth outcome

| Caller | Anonymous | Auth, no relation | Auth, member | Auth, Owner | Auth, admin |
|---|---|---|---|---|---|
| `GET /establishments` (list mine) | 401 | 200 (empty) | 200 (rows where active member) | 200 | 200 (member-side rows only — admin queue is separate) |
| `GET /establishments/{id}` (details) | 401 | **404** | 200 | 200 | 200 |
| `GET /establishments/{id}/members` | 401 | 403 | 200 | 200 | 200 |
| `POST /establishments/{id}/members` | 401 | 403 | 403 | 201 | 201 |
| `PATCH /establishments/{id}/members/{id}` | 401 | 403 | 403 | 200 | 200 |
| `DELETE /establishments/{id}/members/{id}` | 401 | 403 | 403 | 204 | 204 |
| `POST /establishments/{id}/change-requests` | 401 | 403 | 403 | 201 | 201 |
| `PATCH /change-requests/{id}/basic-info` | 401 | 403 | 403 | 200 | 200 |
| `POST .../change-requests/{id}/submit` | 401 | 403 | 403 | 200 | 200 |
| `DELETE .../change-requests/{id}` | 401 | 403 (unless submitter) | 403 (unless submitter) | 204 | 204 |

Add `Status=Suspended` to any row in the right-three columns and the
mutation rows above become **423** instead of their normal 2xx — see §5.

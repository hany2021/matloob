# Plan — Establishment Employees: Invite by email, per-role permissions

## Context

Today's stack lets an admin approve a freshly-registered establishment, which auto-inserts the creator as `Owner` in `establishment_members`. That's the only path that puts a person into an establishment, and the only role that has any write power. The 6-value role enum (`Owner / Manager / HR / Accountant / Commissioner / Other`) is in the schema but **unused** — every endpoint that gates writes calls `IsActiveOwnerAsync`, so a `Manager` and a `Commissioner` are functionally indistinguishable from `Other` (read-only). There is no way for an Owner to add anyone else from the public frontend (and the existing `POST /members` is `userId`-based, which the frontend has no way to discover).

This plan adds a real **employees feature**:
1. **Invite by email**, with an accept-flow that works for both already-registered users and brand-new users (deferred materialization on first IdM login).
2. **Per-role permissions**, gated server-side on every write endpoint and surfaced to the Next.js client so the UI hides actions the active role can't perform.
3. A new **`/dashboard/employees`** page (Owner only) for listing members, sending/revoking invites, and changing roles.

Locked decisions (per user): email + invite flow; keep all 6 roles with a practical permission map; full E2E this session; **the entire employees surface is gated on `establishment.status == Approved`** — the existing `AddMemberEndpoint` already enforces this (in-file comment: "Status guard: Establishment must be in EstablishmentStatus.Approved … 409 with code `cannot_edit_in_status`"), so we extend the same pattern to every new endpoint and reflect it in the UI.

---

## Data model

### New entity `EstablishmentInvitation` (aggregate)

`backend/src/Matloob.Domain/Establishments/EstablishmentInvitation.cs` — mirrors the shape of `EstablishmentMember` (sealed, `BaseAuditableEntity<Guid>`, private setters, behavioral methods).

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` PK | `ValueGeneratedNever`. |
| `establishment_id` | `uuid` | soft FK, same style as `EstablishmentMember`. |
| `email` | `varchar(254)` | normalized lower-case + trimmed in ctor. |
| `role` | `varchar(32)` | `EstablishmentMemberRole` via `HasConversion<string>()`. |
| `token_hash` | `varchar(64)` | SHA-256 hex of the raw token. Raw token only exists inside the create request, never persisted. |
| `status` | `varchar(16)` | enum: `Pending / Accepted / Revoked / Expired`. |
| `invited_by_user_id` | `varchar(200)` | sub of the Owner. |
| `invited_at` | `timestamptz` | |
| `expires_at` | `timestamptz` | `invited_at + 7 days`. |
| `accepted_at` | `timestamptz?` | |
| `accepted_by_user_id` | `varchar(200)?` | sub at accept time. |
| `materialized_member_id` | `uuid?` | soft FK to the materialized `establishment_members` row. |

Indexes:
- `UNIQUE (token_hash)` — accept-time lookup.
- `UNIQUE (establishment_id, email) WHERE is_deleted = false AND status = 'Pending'` — name `ux_invitations_one_pending_per_email_per_est`. Duplicate invite to a pending email → 409 `invitation_already_pending`.
- `(email) WHERE is_deleted = false AND status = 'Accepted' AND materialized_member_id IS NULL` — used by the deferred-materialization sweep.
- `(establishment_id) WHERE is_deleted = false` — list endpoint.

EF config at `backend/src/Matloob.Api/Infrastructure/Persistence/Configurations/Establishments/EstablishmentInvitationConfiguration.cs` alongside `EstablishmentMemberConfiguration.cs` (mirror its layout).

`AppDbContext` gains `public DbSet<EstablishmentInvitation> EstablishmentInvitations => Set<EstablishmentInvitation>();`.

Migration: `dotnet ef migrations add EstablishmentInvitationsSchema` (no `--no-build`).

### Reused, unchanged

`establishment_members` schema, the 6-value `EstablishmentMemberRole` enum, the `Establishment.Approve` flow that auto-inserts the first Owner.

---

## Permission map

New file `backend/src/Matloob.Api/Infrastructure/Auth/Permissions.cs` — flat `const string` slugs grouped by static class:

```
profile.edit
events.create        events.manage
opportunities.create opportunities.manage
applications.read
offers.send          offers.respond
evaluations.create
change_requests.submit
members.manage
finance.edit         // reserved for Accountant write surface (deferred)
```

New file `Infrastructure/Auth/RolePermissions.cs` — static map from `EstablishmentMemberRole` → `IReadOnlySet<string>`:

| Role | Permissions |
|---|---|
| **Owner** | **all** (`HasPermissionAsync` short-circuits to `true` for Owner) |
| **Manager** | `events.{create,manage}`, `opportunities.{create,manage}`, `applications.read`, `offers.{send,respond}`, `evaluations.create`, `change_requests.submit` |
| **HR** | `applications.read`, `offers.{send,respond}`, `evaluations.create` |
| **Accountant** | *(none today; `finance.edit` reserved)* |
| **Commissioner** | `offers.{send,respond}` |
| **Other** | *(none — read-only viewer)* |

Read everywhere is gated by `IsActiveMemberAsync`, not a permission slug. Every role above can read the establishment's events, opportunities, offers, applications, evaluations, etc.

---

## Backend endpoints

### New (under `Features/Establishments/Members/`)

All establishment-scoped endpoints below enforce **`establishment.status == Approved`** (409 `cannot_edit_in_status` for Draft / PendingReview / Rejected; 423 for Suspended once that gate ships, 409 in the meantime — same pattern as existing `AddMemberEndpoint`). Extract into a shared helper `MembershipChecks.EnsureApprovedAsync(...)` to avoid duplicating the check.

| Verb + Path | Folder | Input | Output | Gate |
|---|---|---|---|---|
| `POST /api/v1/establishments/{id}/members/invitations` | `Members/InviteMember/` | `{ email, role }` | `201 { id, email, role, status:"Pending", invitedAt, expiresAt }` — no token in body | `members.manage` + Approved |
| `GET /api/v1/establishments/{id}/members/invitations?include=accepted` | `Members/ListInvitations/` | — | array | `members.manage` + Approved |
| `DELETE /api/v1/establishments/{id}/members/invitations/{invitationId}` | `Members/RevokeInvitation/` | — | `204` (sets `status=Revoked`) | `members.manage` + Approved |
| `POST /api/v1/establishments/{id}/members/invitations/{invitationId}/resend` | `Members/ResendInvitation/` | — | `204` (new token, new `expires_at`, re-fires email) | `members.manage` + Approved |
| `GET /api/v1/invitations/preview?token=...` | `Invitations/Preview/` | query token | `200 { establishmentName, role, inviterName, expiresAt }` — 404 on bad/expired/establishment-not-Approved | **AllowAnonymous** |
| `POST /api/v1/invitations/accept` | `Invitations/Accept/` | `{ token }` | `200 { establishmentId, membershipMaterialized: bool }` — 409 if establishment is not Approved at accept time | `MatloobPolicies.User` |

**Establishment-status state machine for invitations** — what happens at each transition:
- **Approved → Suspended after an invite is sent but before accept:** preview returns 404, accept returns 409. Pending invites stay in DB (Owner can revoke them or re-fire after reinstatement).
- **Approved → Suspended after accept but before deferred materialization:** the `CurrentUserSyncService` sweep skips invitations whose establishment is no longer Approved; the accepted-not-materialized row sits until the establishment is reinstated, then materializes on the next sync run.
- **Suspended → Approved (reinstated):** any `Accepted` invitations whose `materialized_member_id IS NULL` materialize on the next sync run; pending invitations resume normal accept behavior. (Manager/HR/etc. existing memberships are unaffected by suspension — their `is_active` doesn't change; the establishment-level writes are blocked by the per-endpoint status gates.)

### Changed

`POST /api/v1/establishments/{id}/members` (`AddMemberEndpoint`) — switch body contract from `{ userId, role }` to `{ email, role }` (same semantics as the new invite endpoint; old path stays alive so the Next.js can keep calling one URL). Drop the `userId` field. The frontend is the contract and only the new Employees page consumes this.

### Gate-swap pass (no behavior change for Owners)

Replace `IsActiveOwnerAsync` with `HasPermissionAsync(<slug>)` at the following call sites. Owner has implicit-all, so existing Owner-driven tests continue to pass byte-for-byte.

| Feature folder | New slug |
|---|---|
| `Establishments/Events/*` (create/update/delete/end/draft) | `events.manage` |
| `Opportunities/Mine/*` writes (Create/Update/Delete/End) | `opportunities.manage` |
| `Offers/Send` + accept/reject/cancellation | `offers.send` (send) / `offers.respond` (accept/reject/cancellation) |
| `Evaluations/*` create | `evaluations.create` |
| `Establishments/ChangeRequests/*` writes | `change_requests.submit` |
| `Establishments/Profile/Update*` + Services + Products writes | `profile.edit` |

**Stays Owner-only**: `Members/UpdateMember`, `Members/RemoveMember`, the four new invitation endpoints. They use `members.manage`, which only Owner holds.

---

## Backend services + middleware

### `IEmailSender` (mirrors `ISmsSender`)

`backend/src/Matloob.Api/Infrastructure/Notifications/IEmailSender.cs` — one method:

```csharp
Task SendInviteAsync(string toEmail, string establishmentName, string inviteUrl,
                     EstablishmentMemberRole role, CancellationToken ct);
```

`NoOpEmailSender` writes `_logger.LogInformation("[INVITE] email={Email} url={Url} role={Role} establishment={Name}", ...)`. DI registration in `Program.cs` mirrors line 82–83 (the `ISmsSender` binding).

Invite URL composition reads `Notifications:InviteBaseUrl` from config (e.g. `http://localhost:3001/ar/invite`); composes `{InviteBaseUrl}/{rawToken}`. Raw token is generated by `RandomNumberGenerator.GetBytes(32)` → URL-safe base64, only exists inside the create request, hashed (SHA-256) before persisting.

### `MembershipChecks` extensions

Add to `backend/src/Matloob.Api/Features/Establishments/Common/MembershipChecks.cs`:

- `Task<bool> HasPermissionAsync(AppDbContext, Guid establishmentId, string userId, string permission, CancellationToken)` — fetches the active member's role; Owner → true; else returns `RolePermissions.PermissionsFor(role).Contains(permission)`.
- `Task<IReadOnlySet<string>> GetPermissionsAsync(AppDbContext, Guid establishmentId, string userId, CancellationToken)` — materialized set (Owner gets the union of every defined slug). Used by `establishment-list` + `me/profile` to surface `permissions[]`.

### Invite materialization in `CurrentUserSyncService`

File exists at `backend/src/Matloob.Api/Infrastructure/Identity/UserSync/CurrentUserSyncService.cs`. Augment `EnsureCurrentUserAsync` so that **after** the local `users` row is upserted for this sub, we query:

```csharp
var pending = await _db.EstablishmentInvitations
    .Where(i => i.Email == normalizedEmail
             && i.Status == EstablishmentInvitationStatus.Accepted
             && i.MaterializedMemberId == null
             && !i.IsDeleted)
    .ToListAsync(ct);
```

For each, create the `EstablishmentMember` row (`AddedByUserId = invited_by_user_id`, `AddedAt = now`), then set `invitation.MaterializedMemberId = member.Id`. This is the **single** "this user just appeared" hook.

### `Accept` endpoint two-path behavior

1. **Caller's sub already maps to a local `users` row with a matching email**: create the `EstablishmentMember` immediately, set `invitation.{Status=Accepted, AcceptedAt, AcceptedByUserId, MaterializedMemberId}`. Return `{ membershipMaterialized: true }`.
2. **No local `users` row yet for this sub**: set `invitation.{Status=Accepted, AcceptedAt, AcceptedByUserId}` only. The materialization sweep in `CurrentUserSyncService` will create the member row on the next authenticated request. Return `{ membershipMaterialized: false }`.

Concurrency: token mutation is a transaction-scoped UPDATE with a WHERE filter on `status='Pending'`. Two parallel accepts → one row succeeds, the other returns 409 `invitation_already_used`.

### `establishment-list` projection

`Features/Profile/EstablishmentList/GetMyEstablishmentListEndpoint.cs` — the response already projects `role` per item (per the existing in-file comment "status and role are returned as new-client extension fields"). Add `permissions: string[]` next to it, populated via `RolePermissions.PermissionsFor(row.Role)` (or the Owner-all set).

### `me/profile`

`Features/Establishments/Profile/GetMeProfileEndpoint.cs` — add top-level `active_role` and `active_permissions: string[]`. Existing `can_manage_events` stays for backward-compat (re-derived from `permissions.Contains("events.manage")`).

---

## Frontend pages and components

### New `/dashboard/employees` page (Owner only)

`app/[locale]/(AuthRoutes)/dashboard/employees/page.tsx` — server component delegating to:

```
dashboard/employees/_components/
  EmployeesPage.tsx           — two tabs: "الموظفون" / "الدعوات المعلقة"
  MembersList.tsx             — table of active members + Change role / Remove
  PendingInvitesList.tsx      — pending invites + Resend / Revoke
  InviteEmployeeDialog.tsx    — modal: email input + role picker (excludes Owner)
```

### Public accept route

`app/[locale]/invite/[token]/page.tsx` — outside `(AuthRoutes)`. On mount calls `GET /api/v1/invitations/preview?token=...`:
- 404 → "Invalid or expired invitation" view.
- 200 → "You've been invited to {establishmentName} as {role}" + Accept button.

Click flow: if not logged in → push to login with `?returnTo=/{locale}/invite/{token}`. After login the page re-renders and POSTs `/api/v1/invitations/accept`, then redirects to `/dashboard`. **Auth-before-accept** — accept endpoint requires `MatloobPolicies.User`.

### Auth context surface

`app/auth/auth.provider.tsx` — derive two fields from the `establishment-list` payload for the currently-selected establishment:
- `activeEstablishmentRole: EstablishmentMemberRole | null`
- `activeEstablishmentPermissions: string[]`

New hook `app/auth/useHasPermission.ts`:
```ts
export function useHasPermission(p: string) {
  const { activeEstablishmentRole, activeEstablishmentPermissions } = useAuthContext();
  return activeEstablishmentRole === 'Owner'
      || activeEstablishmentPermissions.includes(p);
}
```

### Menu gating in `config/Routes.tsx`

Extend the `filter(_, profile, permissions)` predicate (the `can_manage_events` gate is already there — same pattern):

- `events` → `permissions.includes('events.manage')`
- `opportunities` → `permissions.includes('opportunities.manage')`
- `offers` → `permissions.includes('offers.send') || permissions.includes('offers.respond')`
- new `employees` menu item → `permissions.includes('members.manage') && profile.status === 'Approved'` (Owner-only AND establishment must be Approved — hides the menu for Draft / PendingReview / Rejected / Suspended). The active establishment's `status` is already on the auth profile (used elsewhere); just thread it through.

If an Owner navigates by URL to `/dashboard/employees` while their active establishment is not Approved, the page renders a friendly "هذه الميزة متاحة بعد اعتماد المنشأة من قبل إدارة مطلوب" notice and a link back to `/dashboard`. The backend endpoints would also reject (409), so this is defense in depth + better UX.

### Per-button gating rule

Every "create / edit / send / respond" button across `dashboard/**` wraps its render in `useHasPermission('...')`. The rule:

| Button family | Slug |
|---|---|
| إنشاء فعالية (event-type picker cards on `/dashboard/events`, the wizard entry) | `events.create` |
| إنشاء فرصة (opportunity picker on `dashboard/opportunities/_components/OrganizerOpportunities.tsx`, new-opp wizard) | `opportunities.create` |
| Pencil/edit icons in `dashboard/profile/**` (general / contact / bank / experience / logo / services / products) | `profile.edit` |
| إرسال عرض | `offers.send` |
| Accept / Reject / Cancel offer | `offers.respond` |
| إنشاء تقييم | `evaluations.create` |
| طلب تعديل (change request) | `change_requests.submit` |

When a button is hidden, the surrounding read-only view stays visible. A Manager sees the events list, just not the create button.

### Endpoint constants + query hooks

`services/endpoints/establishments.ts` — add under `me`:

```ts
employees: {
  listMembers:        `${BASE_ENDPOINT}/me/members`,
  listInvitations:    `${BASE_ENDPOINT}/me/invitations`,
  inviteMember:       `${BASE_ENDPOINT}/me/invitations`,
  revokeInvitation:   (id: string) => `${BASE_ENDPOINT}/me/invitations/${id}` as const,
  resendInvitation:   (id: string) => `${BASE_ENDPOINT}/me/invitations/${id}/resend` as const,
  changeRole:         (memberId: string) => `${BASE_ENDPOINT}/me/members/${memberId}/role` as const,
  removeMember:       (memberId: string) => `${BASE_ENDPOINT}/me/members/${memberId}` as const,
},
```

and a top-level:

```ts
invitations: {
  preview: (token: string) => `v1/invitations/preview?token=${encodeURIComponent(token)}` as const,
  accept:  `v1/invitations/accept`,
},
```

`queryhooks/organizer/employees/` — `useEmployees`, `useInviteEmployee`, `useChangeRole`, `useRemoveEmployee`, `useRevokeInvitation`, `useResendInvitation`. Each mutation invalidates `['employees']` and (for role/membership changes) `['establishment-list']` so the account-switcher reflects the change immediately.

---

## Tests

Mirror the existing `backend/tests/Matloob.Api.Tests/Establishments/Members/` layout — one folder per endpoint, plus permission-map units.

**`Members/InviteMember/`** — happy path, duplicate-pending → 409, not-Owner → 403, est. not Approved → 409 `cannot_edit_in_status`, est. Suspended → 409/423, invalid email → 400, attempt to invite Owner role → 400.

**`Members/ListInvitations/`, `RevokeInvitation/`, `ResendInvitation/`** — standard CRUD coverage with auth checks + status-gate test per endpoint.

**`Invitations/Accept/`** — the two materialization paths (this is the critical test pair):
- `Accept_UserAlreadyInUsersTable_CreatesMembershipImmediately` — seed `users` row matching the invite email; POST accept; assert invitation `Status=Accepted` AND a new `establishment_members` row exists with the right role AND `materialized_member_id` set.
- `Accept_UserNotYetInUsersTable_DefersMaterialization` — no `users` row; POST accept; assert invitation `Status=Accepted` AND no `establishment_members` row. Then drive a follow-up request through `CurrentUserSyncService` and assert the membership row appears.
- `Accept_EstablishmentSuspendedAtAcceptTime_Returns409` — seed invitation, then suspend establishment, then POST accept → 409.
- `Materialize_SkipsInvitationsForNonApprovedEstablishments` — accepted-not-materialized row for a Suspended establishment; run sync sweep; assert no member row created. Then reinstate, run sync, assert member row appears.

**`Invitations/Preview/`** — 200 happy path; 404 on bad/revoked/expired/establishment-not-Approved.

**`Invitations/AcceptConcurrency`** — two parallel POSTs against same token → one 200, one 409 `invitation_already_used`.

**`Common/MembershipChecksTests.cs`** — extend with `HasPermissionAsync` cases per role (Owner=all, Manager=events.create, HR=offers.send not events.create, Commissioner=offers.respond not profile.edit, Other=nothing, non-member=false).

**`Profile/EstablishmentList/`** — extend the existing list test to assert `permissions[]` is present and matches `RolePermissions.PermissionsFor(role)`.

Existing 530/530 must stay green at every roll-out step.

---

## Roll-out order

Each sub-PR lands on its **own feature branch off `dev`**, PR'd back into `dev`, and leaves the suite green at every step. Suggested branch names below. The API stays behaviorally unchanged from a user's POV through the first three branches — only branch 4 starts to expose new behavior.

**Branch 0 — Plan committed.** Copy this plan file into the backoffice repo as `docs/establishments-employees-plan.md` on a branch `docs/establishments-employees-plan`. Single doc commit, no code. PR → merge to `dev`. (Frontend repo gets nothing in this step.)

**Branch 1 — Permission scaffolding (no behavior change).** Branch `feature/employees-permission-scaffolding`. Adds `Permissions.cs`, `RolePermissions.cs`, `MembershipChecks.HasPermissionAsync` + `GetPermissionsAsync`. New unit tests for the permission map. **Suite stays 530+.**

**Branch 2 — Email sender abstraction (no behavior change).** Branch `feature/employees-email-sender`. Adds `IEmailSender` + `NoOpEmailSender` + DI wire-up mirroring `ISmsSender`. Nothing calls it yet. DI-resolution test. **Suite stays green.**

**Branch 3 — Gate-swap pass.** Branch `feature/employees-gate-swap`. Switches existing endpoint gates from `IsActiveOwnerAsync` to `HasPermissionAsync(<slug>)` across Events / Opportunities / Offers / Evaluations / ChangeRequests / Profile / Services / Products. Owner is implicit-all → existing tests pass byte-for-byte. **Suite stays green.**

**Branch 4 — Invitation schema + endpoints + materialization hook.** Branch `feature/employees-invite-flow`. The new `EstablishmentInvitation` entity, EF config, `EstablishmentInvitationsSchema` migration, all 6 new endpoints, `CurrentUserSyncService` materialization sweep, Approved-status gate threaded through. **`AddMemberEndpoint` body contract switches to email-based here.** New tests land (~15+); suite grows.

**Branch 5 — Surface role + permissions on read endpoints.** Branch `feature/employees-projection`. `establishment-list` gets `permissions[]`; `me/profile` gets `active_role` + `active_permissions`. Existing response tests updated. **Suite stays green.**

**Branch 6 — Frontend Employees page + per-role gating.** Branch `feature/employees-page` (on the **frontend** repo — push target is `github`, not `origin`, per the standing convention in `SESSION-RESUME.md`). All FE work: the new `/dashboard/employees` route, the public `/invite/[token]` route, auth-context surface, `useHasPermission`, menu + button gates, query hooks. No backend changes. **Live-QA happens here.**

After branch 6 lands on `dev`, both repos go back in sync (backoffice `origin/dev`, frontend `github/dev`).

---

## Live-QA script (end-to-end)

1. **Seed**: a second IdM identity for testing (e.g. `manager@test`). Owner (`OwnerProjectManager`) already has an Approved establishment (Rotana).
2. **Owner logs in**, switches to Rotana, opens `/dashboard/employees`. Confirm the menu item appears.
3. **Owner clicks إضافة موظف** → enters `manager@test`, picks role `Manager`, submits. Verify:
   - Browser: 201 response, dialog closes, pending invite appears in the list.
   - API console: `[INVITE] email=manager@test url=http://localhost:3001/ar/invite/<token> role=Manager establishment=Rotana`.
   - Postgres: `SELECT id, email, status, expires_at, token_hash FROM establishment_invitations` → one Pending row, `expires_at` ~7 days out, `token_hash` populated (raw token NOT in DB).
4. **Copy URL from console**, open in a fresh private window.
5. Public preview renders the invite. Click Accept → redirected to IdM login → sign in as `manager@test`.
6. After login the same `/invite/<token>` page POSTs `/api/v1/invitations/accept` → redirects to `/dashboard`. Verify:
   - Postgres: invitation `status=Accepted`. If `manager@test` had no prior `users` row, NO `establishment_members` row yet. On the next authenticated request, `CurrentUserSyncService` runs the sweep → membership row appears with `role=Manager`.
7. **Manager refreshes** the dashboard. Account-switcher shows Rotana with a "Manager" badge.
8. **Permission spot-checks**:
   - `/dashboard/events` — list visible, إنشاء فعالية button visible (Manager has `events.create`).
   - `/dashboard/profile` — pencil icons HIDDEN (Manager lacks `profile.edit`). Direct PATCH via curl returns 403 (defense in depth).
   - `/dashboard/employees` — menu item NOT in sidebar (Manager lacks `members.manage`). Direct nav shows a "Forbidden" view.
9. **Second materialization path**: invite an email whose IdM identity already has a `users` row → membership row appears IMMEDIATELY at accept time.
10. **Negatives**: re-invite an active member → 409 `email_already_member`. Re-invite a pending email → 409 `invitation_already_pending`. Revoke + re-invite → 201. Bump `expires_at` to yesterday → preview 404, accept 410. Open same URL in two tabs, accept in both → one 200, one 409.

11. **Status-gate negative-path**: as Owner, manually transition the establishment to Suspended (via admin Angular), then open `/dashboard/employees` — menu item gone; direct URL shows the "feature requires Approved establishment" notice. Send-invite via curl → 409 `cannot_edit_in_status`. Fire `POST /invitations/accept` against a token issued before suspension → 409. Reinstate via admin Angular, refresh — menu reappears, sweep materializes any deferred accepts.

After QA: restore fixtures — reinstate the establishment if suspended for testing, revoke / delete the test invitations, soft-delete the test members, leave the DB as found.

---

## Out of scope (follow-ups)

- **Real email provider.** `NoOpEmailSender` only logs. A real provider (SES or equivalent) lands later.
- **Accountant write surface.** `finance.edit` is reserved but no endpoint gates on it; bank/finance editing endpoints will land in a later session.
- **HR drafting opportunities.** Locked to "hiring-side only" per the role map.
- **Per-establishment custom roles** (user-defined beyond the 6 enum values).
- **Owner transfer / multi-Owner promotion via invite.** Invite role picker excludes `Owner`; that's added only by the AdminReview approval flow today.
- **Audit log UI.** `EstablishmentReviewHistory` rows for invite/accept/revoke/role-change are written by the endpoints but no UI lists them yet.
- **Localized invite email body.** Mock sender just logs; templates land when a real provider does.

---

## Plan file location

This plan is also persisted in the backoffice repo at:

```
C:\National Events Center - NEC\Matloob\repos\matloob-backoffice (.net + angular)\docs\establishments-employees-plan.md
```

The Branch 0 PR is just the doc landing in `docs/` — gives the team a single source of truth they can reference from later commit messages (e.g. "Branch 4 of docs/establishments-employees-plan.md"). The .claude/plans copy stays as my working scratchpad; the canonical doc lives in the repo.

## Critical files to touch

Backend:
- `backend/src/Matloob.Domain/Establishments/EstablishmentInvitation.cs` (new)
- `backend/src/Matloob.Api/Infrastructure/Persistence/Configurations/Establishments/EstablishmentInvitationConfiguration.cs` (new)
- `backend/src/Matloob.Api/Infrastructure/Persistence/AppDbContext.cs` (add DbSet)
- `backend/src/Matloob.Api/Infrastructure/Persistence/Migrations/<timestamp>_EstablishmentInvitationsSchema.cs` (generated)
- `backend/src/Matloob.Api/Infrastructure/Auth/{Permissions.cs, RolePermissions.cs}` (new)
- `backend/src/Matloob.Api/Infrastructure/Notifications/{IEmailSender.cs, NoOpEmailSender.cs}` (new, mirror `ISmsSender`)
- `backend/src/Matloob.Api/Program.cs` (DI binding for `IEmailSender`, ~lines 82–83)
- `backend/src/Matloob.Api/Features/Establishments/Common/MembershipChecks.cs` (add `HasPermissionAsync`, `GetPermissionsAsync`)
- `backend/src/Matloob.Api/Features/Establishments/Members/{InviteMember,ListInvitations,RevokeInvitation,ResendInvitation}/` (new endpoint folders)
- `backend/src/Matloob.Api/Features/Invitations/{Preview,Accept}/` (new endpoint folders)
- `backend/src/Matloob.Api/Features/Establishments/Members/AddMember/AddMemberEndpoint.cs` (switch body contract to email)
- `backend/src/Matloob.Api/Infrastructure/Identity/UserSync/CurrentUserSyncService.cs` (post-upsert sweep)
- `backend/src/Matloob.Api/Features/Profile/EstablishmentList/GetMyEstablishmentListEndpoint.cs` (add `permissions[]`)
- `backend/src/Matloob.Api/Features/Establishments/Profile/GetMeProfileEndpoint.cs` (add `active_role` + `active_permissions`)
- Endpoint files under `Features/Establishments/Events/*`, `Features/Opportunities/Mine/*`, `Features/Offers/*`, `Features/Evaluations/*`, `Features/Establishments/ChangeRequests/*`, `Features/Establishments/Profile/Update*`, `Features/Establishments/Services/*`, `Features/Establishments/Products/*` (gate-swap pass)

Frontend:
- `app/[locale]/(AuthRoutes)/dashboard/employees/page.tsx` + `_components/` tree (new)
- `app/[locale]/invite/[token]/page.tsx` (new, public)
- `app/auth/auth.provider.tsx` (add `activeEstablishmentRole`, `activeEstablishmentPermissions`)
- `app/auth/useHasPermission.ts` (new)
- `config/Routes.tsx` (menu gating predicates + new `employees` item)
- `services/endpoints/establishments.ts` (new endpoint constants)
- `queryhooks/organizer/employees/` (new hook folder)
- Button-bearing components across `dashboard/events/**`, `dashboard/opportunities/**`, `dashboard/profile/**`, `dashboard/offers/**`, `dashboard/evaluations/**` (wrap action buttons in `useHasPermission`)

Tests:
- `backend/tests/Matloob.Api.Tests/Establishments/Members/{InviteMember,ListInvitations,RevokeInvitation,ResendInvitation}/*`
- `backend/tests/Matloob.Api.Tests/Invitations/{Preview,Accept,Concurrency}/*`
- `backend/tests/Matloob.Api.Tests/Establishments/Common/MembershipChecksTests.cs` (extend)
- `backend/tests/Matloob.Api.Tests/Profile/EstablishmentListTests.cs` (extend to assert `permissions[]`)

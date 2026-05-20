# Data migration plan — MySQL (Laravel) → PostgreSQL (new system)

**Phase 0 deliverable 3.** Source: live MySQL `matloob` database from the current Laravel system. Target: PostgreSQL `matloob` database managed by the new EF Core schema. **Frozen after sign-off; revisions need explicit approval.**

## Strategy overview

| Aspect | Decision |
|---|---|
| Tool | A dedicated **`Matloob.DataMigration`** .NET console project. Reads MySQL via `MySqlConnector`, writes PostgreSQL via Npgsql + EF Core. Idempotent (UPSERT on natural keys). |
| Direction | One-shot ETL from a *frozen* MySQL snapshot. **Not** a live two-way sync. |
| Order | Lookup data → identities → core domain → derived/junction → media → audits/notifications. |
| UUIDs | Preserve existing `uuid` columns from Laravel as the new PG `id` PK. No GUID re-issuance. |
| Timezone | All Laravel `DATETIME` values treated as **`Asia/Riyadh`** local time, converted to UTC `timestamptz` on insert. (Confirmed in Q-DM-1; see open questions if not.) |
| Mode | Dry-run by default. `--apply` flag commits. Dry-run produces the same report files as `--apply` would generate, without writing. |
| Pre-flight gates | Fail loudly on duplicate `cr_number` (per O-6). Fail loudly on referential integrity violations the new schema would reject. |
| Re-runnability | Yes — every insert is `ON CONFLICT (id) DO UPDATE` keyed by UUID. Safe to retry. |
| Rollback | Drop the target PG database. The source MySQL is untouched. |

---

## What's NOT migrated

Per the Ajeer disposition doc and the EstablishmentOnboarding spec, these legacy tables have **no corresponding PG table** and their data is intentionally discarded:

| Legacy table | Reason |
|---|---|
| `contracts` | Contracts entity removed |
| `contract_invoice` | Pivot for removed entities |
| `invoices` | Invoices entity removed |
| `ajeer_notifications` | Ajeer removed |
| `api_logs` | Ajeer HTTP call log; replaced by OTel telemetry |
| `oauth_access_tokens`, `oauth_auth_codes`, `oauth_refresh_tokens`, `oauth_clients`, `oauth_personal_access_clients` | Laravel Passport tables; new auth is IdM-only JWT |
| `failed_jobs`, `jobs`, `job_batches`, `cache`, `sessions`, `password_reset_tokens` | Laravel framework operational tables |
| `imports`, `exports`, `failed_import_rows` | Laravel framework operational tables |
| `filament_otp_login` | Filament removed |
| `migrations` | EF Core has its own `__ef_migrations_history` |

---

## What IS migrated

By feature group, in execution order. Each entry lists: source MySQL table, target PG table, transform notes, and risks.

### Phase A — Lookup / reference data (no dependencies)

| Source (MySQL) | Target (PG) | Transform | Notes |
|---|---|---|---|
| `cities` | `cities` | 1:1 column copy | UUID kept as `id` |
| `regions` | `regions` | 1:1 | |
| `nationalities` | `nationalities` | 1:1 | |
| `languages` | `languages` | 1:1 | |
| `banks` | `banks` | 1:1 | |
| `job_titles` | `job_titles` | 1:1 | |
| `event_types` | `event_types` | 1:1 | |
| `opportunity_categories` | `opportunity_categories` | 1:1 | Self-referential `parent_id` — load roots first, then children |
| `offer_rejection_reasons` | `offer_rejection_reasons` | 1:1 | Drop any rows that referenced Ajeer-specific reasons (filter on a marker column or by name; verify in pre-flight) |
| `offer_cancellation_reasons` | `offer_cancellation_reasons` | 1:1 | Same Ajeer-filter |
| `seasons` | `seasons` | 1:1 | |
| `suggested_locations` | `suggested_locations` | 1:1 | |
| `suggested_attendees` | `suggested_attendees` | 1:1 | |
| `skills` | `skills` | 1:1 | |
| `settings` | `settings` | 1:1 *except* drop keys: `ajeer_enabled`, `ajeer_api_url`, `ajeer_*` (Q-AJ-3) | Pre-flight reports which keys are dropped |
| `translations` | `translations` | 1:1 | Drop rows where the translation key references removed Ajeer concepts |
| `success_management_criteria` | `success_management_criteria` | 1:1 | |
| `permissions` | `permissions` | 1:1 (Spatie) | |
| `roles` | `roles` | 1:1 (Spatie) | Drop any role specifically created for Ajeer flows; rename "Super Admin" → "matloob_admin" if migrating into IdM-claim-driven authorization (Q-AJ-4) |

### Phase B — Identities

| Source | Target | Transform | Notes |
|---|---|---|---|
| `users` | `users` | 1:1; `password` column kept (currently unused — auth is IdM-only); `identity_id` populated where set | Soft-deleted rows migrate with `is_deleted = true` |
| `admins` | `admins` | 1:1; `identity_id` populated; `is_active` preserved | The default `super-admin@matloob.sa` row migrates as-is; **admin must rotate** the placeholder password and re-link `identity_id` to a real IdM admin sub in production |
| `model_has_roles` (Spatie) | `model_has_roles` | 1:1 | Confirm morph map alias `'admin'` is preserved or remapped to FQCN |
| `model_has_permissions` | `model_has_permissions` | 1:1 | |
| `role_has_permissions` | `role_has_permissions` | 1:1 | |
| `personal_access_tokens` (Laravel Sanctum/Passport tokens) | **not migrated** | — | All future auth is JWT-only |

### Phase C — Establishments (the big collapse)

This is the one non-trivial transform.

**Source:** 3 tables joined on `establishment_id`:
```
establishments
  LEFT JOIN establishment_profiles ON establishment_profiles.establishment_id = establishments.id
  LEFT JOIN establishment_contact_infos ON establishment_contact_infos.establishment_id = establishments.id
```

**Target:** 1 table — the new `establishments`.

#### Column mapping

| Legacy column | Source table | New column | Transform |
|---|---|---|---|
| `id` (bigint) | `establishments` | (dropped) | New PK is `uuid` |
| `uuid` | `establishments` | `id` | Preserved verbatim |
| `name` | `establishments` | `Name` | |
| `labor_office_id` | `establishments` | `LaborOfficeId` | |
| `sequence_number` | `establishments` | `SequenceNumber` | |
| `email` | `establishments` | `Email` | |
| `is_sponsor` | `establishments` | `IsSponsor` | bool |
| `can_manage_events` | `establishments` | `CanManageEvents` | bool |
| `profile_completed` | `establishments` | (dropped) | Computed on read in new system |
| `cr_number` | `establishment_profiles` | `CommercialRegistrationNumber` | **Subject to duplicate pre-flight gate** |
| `cr_number_expiry` | `establishment_profiles` | `CommercialRegistrationExpiry` | |
| `economic_activity` | `establishment_profiles` | `EconomicActivity` | |
| `sub_economic_activity` | `establishment_profiles` | `SubEconomicActivity` | |
| `city` | `establishment_profiles` | `City` | Free text per O-3 |
| `neighborhood` | `establishment_profiles` | `District` | Rename |
| `area` | `establishment_profiles` | `Area` | |
| `street_name` | `establishment_profiles` | `Street` | Rename |
| `building_number` | `establishment_profiles` | `BuildingNumber` | |
| `postal_code` | `establishment_profiles` | `PostalCode` | |
| `additional_number` | `establishment_profiles` | `AdditionalNumber` | |
| `description` | `establishment_profiles` | `Description` | |
| `location_title` | `establishment_profiles` | `LocationTitle` | |
| `lat` | `establishment_profiles` | `Latitude` | |
| `lon` | `establishment_profiles` | `Longitude` | |
| `website` | `establishment_profiles` | `Website` | |
| `years_of_experience` | `establishment_profiles` | `YearsOfExperience` | default 0 if null |
| `establishment_size` | `establishment_profiles` | `EstablishmentSize` | |
| `establishment_status` | `establishment_profiles` | **dropped** | Per O-2 |
| `seven_hundred_number` | `establishment_profiles` | **dropped** | Per O-2 |
| `contact_number` | `establishment_contact_infos` | `Phone` | |
| `additional_contact_number` | `establishment_contact_infos` | `AdditionalContactNumber` | |
| (synthesized) | — | `Status` | Always `Approved` for migrated rows |
| (synthesized) | — | `IsLegacyImport` | Always `true` for migrated rows |
| (synthesized) | — | `CreatedByUserId` | See "first commissioner" rule below; nullable if no commissioner |
| (synthesized) | — | `ApprovedAt` | `establishments.created_at` |
| (synthesized) | — | `ApprovedByAdminId` | `NULL` |
| `created_at`, `updated_at` | `establishments` | `CreatedAt`, `UpdatedAt` | KSA local → UTC |

**Bank accounts** (legacy `bank_accounts` polymorphic on `entity_type/entity_id`) migrate to a new `establishment_bank_accounts` table with `establishment_id` FK. User-side bank accounts migrate to `user_bank_accounts` (or remain polymorphic — Q-DM-2).

**Establishment experiences** (legacy `establishment_experiences`) migrate 1:1 to a new table of the same name.

#### Pre-flight gates for Phase C

1. **Duplicate CR number gate (O-6 confirmed):**
   ```sql
   SELECT cr_number, COUNT(*) AS dup_count, GROUP_CONCAT(establishments.id) AS legacy_ids
   FROM establishment_profiles
   JOIN establishments ON establishments.id = establishment_profiles.establishment_id
   WHERE cr_number IS NOT NULL
     AND establishments.deleted_at IS NULL
   GROUP BY cr_number
   HAVING dup_count > 1;
   ```
   - If any rows → write `reports/duplicate-cr-numbers.csv` and **exit non-zero**.
   - Operator must resolve in the source DB *or* supply `--cr-resolution=path/to/duplicate-cr-resolution.csv` with one row per duplicate: `legacy_establishment_id, action (keep|suspend|exclude)`.
   - Re-run.

2. **Orphan establishments report (always written, never blocking):**
   ```sql
   SELECT e.id, e.uuid, e.name
   FROM establishments e
   LEFT JOIN commissioners c ON c.establishment_id = e.id AND c.deleted_at IS NULL
   WHERE c.id IS NULL AND e.deleted_at IS NULL;
   ```
   - Writes `reports/orphan-establishments.csv`. Migration continues; these rows get `CreatedByUserId = NULL` and require post-migration admin assignment.

3. **Missing CR number warning (non-blocking):**
   ```sql
   SELECT e.id, e.uuid, e.name
   FROM establishments e
   LEFT JOIN establishment_profiles p ON p.establishment_id = e.id
   WHERE (p.cr_number IS NULL OR p.cr_number = '') AND e.deleted_at IS NULL;
   ```
   - `reports/establishments-without-cr.csv`. These migrate with `IsLegacyImport=true` and `CommercialRegistrationNumber=NULL`. Admin UI shows them with a warning banner; they cannot create change-requests until they add a CR number.

### Phase D — Establishment members

Per H11 confirmation in the onboarding spec:

**Source:** `commissioners` table.

```sql
SELECT
  establishment_id,
  user_id,
  uuid,
  created_at,
  deleted_at,
  ROW_NUMBER() OVER (PARTITION BY establishment_id ORDER BY created_at ASC) AS rn
FROM commissioners
```

**Mapping:**
- `rn = 1` → `establishment_members(role='Owner', is_active=true)`
- `rn > 1` → `establishment_members(role='Commissioner', is_active=true)`
- `commissioners.deleted_at IS NOT NULL` → `establishment_members(is_deleted=true, is_active=false)`

**Synthesize:**
- `AddedAt = commissioners.created_at`
- `AddedByUserId = NULL`

**Backfill establishment.CreatedByUserId:** Set to the `user_id` of the first commissioner (the one promoted to Owner). For orphan establishments (no commissioners), leave NULL.

#### Pre-flight gate

Verify that every `commissioners.user_id` exists in the migrated `users` table. Any mismatch → `reports/commissioners-with-missing-user.csv` and **exit non-zero**.

### Phase E — Synthetic review history

For each migrated establishment, insert TWO `establishment_review_history` rows:

| Row | Action | OccurredAt | Reason |
|---|---|---|---|
| 1 | `DraftCreated` | `establishments.created_at` | `"Migrated from legacy system"` |
| 2 | `Approved` | `establishments.created_at` | `"Migrated from legacy system (grandfathered)"` |

`ActorAdminId = NULL` for both. `ChangeRequestId = NULL`.

### Phase F — Users' profile data

| Source | Target | Notes |
|---|---|---|
| `user_education` | `user_education` | 1:1 |
| `user_experiences` | `user_experiences` | 1:1 |
| `user_certificates` | `user_certificates` | 1:1; if Spatie media attached, see Phase H |
| `user_profession` | `user_profession` | 1:1 |
| `language_user` (pivot) | `language_user` | 1:1 |
| `supportive_documents` | `supportive_documents` | 1:1 |

### Phase G — Core domain (Opportunities, Applicants, Offers, Events, Evaluations)

| Source | Target | Transform |
|---|---|---|
| `opportunities` | `opportunities` | 1:1; reference establishments by UUID-FK |
| `event_opportunity_category` (pivot) | `event_opportunity_category` | 1:1 |
| `applicants` | `applicants` | 1:1 |
| `offers` | `offers` | **Strip Ajeer-only columns** (e.g. `ajeer_contract_id` if any). If `offers.contract_id` exists, drop it (the linked Contract no longer exists). `status` enum values that were Ajeer-conditional collapse to `Accepted` (Q-DM-3). |
| `offer_cancellation_requests` | `offer_cancellation_requests` | 1:1 |
| `events` | `events` | 1:1 |
| `participations` | `participations` | 1:1 |
| `evaluation` (singular table name in MySQL) | `evaluations` (plural in PG) | 1:1 with table rename. If evaluations reference contracts (`contract_id`), drop that column or remap to `offer_id` (Q-EVAL-1). |

### Phase H — Media (Spatie) → Assets

Legacy `media` table (Spatie laravel-medialibrary). Each row carries:
- `model_type`, `model_id` (polymorphic owner)
- `uuid`, `name`, `file_name`, `mime_type`, `size`, `disk`, `collection_name`
- Files on S3 at `disk:s3, conversions/{path}/{uuid}/{file_name}`.

**For each `media` row:**

1. Download the binary from S3 using existing AWS creds (read-only, one-time).
2. Compute SHA-256.
3. Save to local disk at `{AssetsRoot}/{yyyy}/{MM}/{dd}/{guid}.{ext}` (using the `media.uuid` as the GUID).
4. Insert into new `assets` table with:
   - `Id = media.uuid`
   - `OriginalFileName = media.file_name`
   - `ContentType = media.mime_type`
   - `SizeBytes = media.size`
   - `Sha256 = computed`
   - `RelativePath = "yyyy/MM/dd/{guid}.{ext}"`
   - `StorageDriver = "local"`
   - `Bucket = NULL`
   - `Visibility = "private"` (or `"public"` for logos — Q-DM-4)
   - `Purpose = "legacy_media"` (no semantic purpose available in Spatie data)
   - `Metadata = jsonb_build_object('legacy_collection', collection_name, 'legacy_model_type', model_type, 'legacy_model_id', model_id)`

5. **Update the owning entity:**
   - `media.model_type = User` + `collection_name = 'photo'` → set `users.photo_asset_id = media.uuid`
   - `media.model_type = Establishment` + `collection_name = 'logo'` → set `establishments.logo_asset_id = media.uuid` (new field)
   - Other collections: store the asset id in a polymorphic `entity_assets` join table (Q-DM-5) so we don't have to add columns to every entity.

6. **Documents (legacy)** that look like AuthorizationLetter/CommercialRegistration: legacy schema did NOT have these doc types, so no `media` row will match. Migrated establishments simply have **no** `establishment_documents` rows. The admin UI shows the "legacy import — documents missing" banner.

**Risks (Phase H):**
- **Disk space.** Confirm `{AssetsRoot}` has at least `SUM(media.size)` + 30% headroom before starting.
- **S3 access.** Required during migration window; not after.
- **Throughput.** Sequential downloads will be slow. Migrator should support `--parallel N` (default 4) for the download phase.
- **Missing files in S3.** Some `media` rows may point to files that no longer exist. Report and skip; do not block.

### Phase I — Notifications and audits

| Source | Target | Notes |
|---|---|---|
| `notifications` (Laravel database driver) | `notifications` | 1:1. `notifiable_type` morph stays; in PG we keep the same `(notifiable_type, notifiable_id)` shape with no FK enforcement (matches Laravel behavior). |
| `audits` (`owen-it/laravel-auditing`) | `audit_entries` | **Schema rename + reshape.** Old `audits.old_values` / `new_values` are JSON columns; new `audit_entries` uses jsonb with the same content. Map `auditable_type/id` → `entity_name/entity_id`. PII redaction policy (Q-DM-6) applied during migration. |

---

## Execution plan

```
matloob-data-migration \
  --source mysql://matloob:matloob@localhost:3307/matloob \
  --target postgresql://matloob:matloob@localhost:5432/matloob \
  --assets-root /var/matloob/assets \
  --s3-bucket matloob-prod-media \
  --aws-credentials env \
  --report-dir ./reports \
  --parallel 4 \
  [--apply]            # without this, dry-run only
```

Step ordering (each step gates the next):

1. **Connect + sanity**: source rows count vs expected; target empty.
2. **Pre-flight A**: duplicate CR (blocking). Settings drop list (informational).
3. **Pre-flight B**: orphan establishments report (non-blocking).
4. **Migrate Phase A** (lookups).
5. **Migrate Phase B** (identities).
6. **Pre-flight D**: commissioner-without-user (blocking).
7. **Migrate Phase C** (establishments — collapsed).
8. **Migrate Phase D** (members).
9. **Migrate Phase E** (synthetic review history).
10. **Migrate Phase F** (user profile data).
11. **Migrate Phase G** (core domain — Ajeer columns stripped).
12. **Migrate Phase H** (media → assets, with S3 download).
13. **Migrate Phase I** (notifications + audits).
14. **Post-migration verification**:
    - Row counts per table compared with source
    - Cross-FK integrity (every `applicants.opportunity_id` exists, every `offers.applicant_id` exists, etc.)
    - At least one row in `establishment_review_history` per migrated establishment
    - `assets` row count matches `media` row count minus reported missing-from-S3
    - Report file `reports/post-migration-summary.csv`

If any step fails: the migrator transactionally rolls back the *step*, leaves prior steps committed, and exits non-zero. Operator fixes and resumes with `--resume-from <step>`.

---

## Output reports (under `--report-dir`)

| File | Source phase | Purpose |
|---|---|---|
| `pre-flight-summary.txt` | All pre-flights | Row counts, gate verdicts |
| `duplicate-cr-numbers.csv` | Phase C pre-flight | Blocking gate |
| `orphan-establishments.csv` | Phase C pre-flight | Establishments with no commissioner — need admin assignment |
| `establishments-without-cr.csv` | Phase C | Migrated rows with NULL `CommercialRegistrationNumber` |
| `commissioners-with-missing-user.csv` | Phase D pre-flight | Blocking gate |
| `settings-dropped.csv` | Phase A | Ajeer-related setting keys removed |
| `roles-dropped.csv` | Phase A | Any role dropped |
| `media-missing-from-s3.csv` | Phase H | Media rows where the S3 object 404s |
| `media-too-large-skipped.csv` | Phase H | Files exceeding configured max (probably none, but defensive) |
| `audit-redactions.csv` | Phase I | Audit rows where PII was redacted per Q-DM-6 |
| `post-migration-summary.csv` | Verification | Row counts and integrity verdict |

---

## Open questions before migration begins

| # | Question |
|---|---|
| **Q-DM-1** | Confirm source MySQL stores `DATETIME` in `Asia/Riyadh` local time (no UTC conversion at write). If different, the timezone assumption changes. *(Default: Asia/Riyadh.)* |
| **Q-DM-2** | `bank_accounts` is polymorphic in Laravel (`entity_type/id`). In PG, split into `establishment_bank_accounts` + `user_bank_accounts` (FK-enforced), or keep polymorphic? *(My recommendation: split.)* |
| **Q-DM-3** | `OfferStatus` enum members that were Ajeer-conditional (`WAITING_FOR_AJEER_CONTRACT`, etc., if present): collapse to which terminal status? `Accepted` is the safe default — confirm. |
| **Q-DM-4** | Asset visibility for migrated media: `private` for everything? Or `public` for things like establishment logos? *(My default: `private`; logos served through signed URLs.)* |
| **Q-DM-5** | Non-photo, non-logo legacy media (e.g. UserCertificate attachments): store the asset GUID via a new `entity_assets` polymorphic join table, or add asset_id columns per entity? *(My recommendation: polymorphic join — keeps entities lean.)* |
| **Q-DM-6** | PII redaction in `audit_entries.new_values` — at minimum, never store full national IDs, passport numbers, or bank account numbers in audit JSON. Confirm the redaction policy. *(My default: redact `id_number`, `passport_number`, `iban`, `password`, `remember_token`, anything matching `*_token` or `*_secret`.)* |
| **Q-DM-7** | When does the cutover happen? Live source MySQL must be **frozen** at the moment of migration; we cannot tolerate writes during the run. Operator coordinates a maintenance window. Expected window: 2–6 hours depending on media count. |
| **Q-DM-8** | Production access to S3 for the migration window — who provides credentials? |
| **Q-DM-9** | Is there a staging clone of the production MySQL we can rehearse against? *(Strongly recommended — at least one full dry-run before the real cutover.)* |
| **Q-AJ-1** (carried over) | `ContractType` enum: drop entirely, or port as `OfferContractKind`? Affects Phase G. |
| **Q-AJ-4** | Roles: do we rename "Super Admin" → "matloob_admin" in the new system, or keep both? Affects Phase A. |

---

## Sign-off

When Q-DM-1, Q-DM-6, Q-DM-7, and Q-DM-9 are answered, Phase 14 (Data migration MySQL → PostgreSQL) is unblocked. The other Qs can be answered later but **before** the cutover window.

— end —

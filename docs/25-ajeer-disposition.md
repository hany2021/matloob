# Ajeer disposition list

**Phase 0 deliverable 2.** Every Laravel source file that references Ajeer, plus the new-system disposition. Source of truth: the grep of `ajeer | AjeerApi | AjeerInvoice | AjeerContract` across `app/`, `routes/`, `config/`, `database/migrations/` in the read-only matloob-backoffice repo on commit `4b25549` (branch `main`).

## Master decision

**Ajeer is removed from the new system in its entirety.** No adapters, no fake mode, no compatibility shims. Routes that touched Ajeer either:
- become **compatible** (the route survives but the Ajeer side-effect is gone — offer just changes status, no external call), or
- become **removed** (the route's only purpose was to talk to Ajeer).

Internal Contracts and Invoices entities were Ajeer-only concepts. They are deleted (per team decision, update #3, blueprint).

---

## Disposition by file

### Configuration

| File | Content | Disposition |
|---|---|---|
| `config/ajeer.php` | Ajeer base URL + credentials | **Removed.** No replacement. |
| `config/services.php` "ajeer" block (if present) | — | **Removed.** |
| `app/Http/Middleware/CheckAjeerEnabled.php` | Global API middleware that short-circuits when Ajeer is disabled | **Removed.** No conditional behavior remains. |
| `bootstrap/app.php` scheduler entries: `app:ajeer-notification` | Quartz-equivalent worker job | **Removed.** Command class was already missing from the repo. |

### Enums

| File | Content | Disposition |
|---|---|---|
| `app/Enums/AjeerContractStatus.php` | Ajeer contract status values | **Removed.** Contracts entity gone. |
| `app/Enums/AjeerInvoiceStatus.php` | Ajeer invoice status values | **Removed.** Invoices entity gone. |
| `app/Enums/ContractType.php` | Contract type enum (possibly Ajeer-influenced) | **Removed.** Verify the enum is not referenced by surviving Offer logic — if Offers carry a `ContractType` field, port the enum but rename to `OfferContractKind` and confirm it's not Ajeer-shaped. |
| `app/Enums/OfferStatus.php` | Offer status values | **Kept.** Audit values for Ajeer-only transitions (e.g. `WAITING_FOR_AJEER_CONTRACT`) — drop those enum members if present, keep the rest. |

### Services

| File | Content | Disposition |
|---|---|---|
| `app/Services/Invoices/IssueInvoiceService.php` | Calls Ajeer to issue an invoice; persists local Invoice | **Removed.** Invoices entity gone. |
| `app/Services/Offers/AcceptOfferService.php` | On accept: marks offer accepted, calls Ajeer to issue contract, links Contract to Offer | **Re-implemented internal-only.** New version: validate transition, set `OfferStatus = Accepted`, raise `OfferAccepted` event. Drop the Ajeer call + Contract creation entirely. |
| `app/Services/Offers/RejectOfferService.php` | Likely just status change | **Re-implemented internal-only.** Should already be pure-internal; verify and port. |
| `app/Services/Offers/CancelOfferService.php` | Status change + possibly Ajeer call to cancel contract | **Re-implemented internal-only.** Strip any Ajeer code. |
| `app/Services/Offers/ApproveOfferCancellationRequestService.php` | Approve a cancellation request; may notify Ajeer | **Re-implemented internal-only.** |
| `app/Services/Offers/RejectOfferCancellationRequestService.php` | Reject cancellation | **Re-implemented internal-only.** |
| `app/Services/Offers/SendOfferService.php` | Send offer; may pre-call Ajeer eligibility check | **Re-implemented internal-only.** Pre-flight eligibility check removed entirely. |
| `app/Services/Establishments/Offers/PendingApprovalOfferService.php` | Sponsor-approval flow service | **Re-implemented internal-only.** Sponsor flow is a pure-internal status machine; strip any Ajeer-touching code. |

### Controllers

| File | Route | Disposition |
|---|---|---|
| `app/Http/Controllers/Establishments/Invoices/IssueInvoiceController.php` | `POST /api/establishments/invoices/issue` | **Removed.** |
| `app/Http/Controllers/Establishments/Invoices/IndexInvoiceController.php` | `GET /api/establishments/invoices` | **Removed.** |
| `app/Http/Controllers/Establishments/Invoices/ShowInvoiceController.php` | `GET /api/establishments/invoices/{invoice}` | **Removed.** |
| `app/Http/Controllers/Establishments/Offers/Ajeer/CheckContractingEligibility.php` | `GET /api/establishments/offers/ajeer/check-eligibility` | **Removed.** |
| `app/Http/Controllers/Establishments/Offers/Invoices/IndexPendingInvoiceOfferController.php` | `GET /api/establishments/offers/pending-invoice` | **Removed.** |
| `app/Http/Controllers/Establishments/Offers/CancellationRequests/AcceptOfferCancellationRequestController.php` | `POST /api/establishments/offers/{offer}/approve-cancellation` | **Kept — re-implemented internal-only.** The Ajeer call inside the service is stripped; the route stays. |
| `app/Http/Controllers/Establishments/Offers/CancellationRequests/RejectOfferCancellationRequestController.php` | `POST /api/establishments/offers/{offer}/reject-cancellation` | **Kept — re-implemented internal-only.** |
| `app/Http/Controllers/Establishments/Offers/CancelOfferController.php` | `POST /api/establishments/offers/cancel` | **Kept — re-implemented internal-only.** |
| `app/Http/Controllers/Establishments/Offers/SendOfferController.php` | `POST /api/establishments/offers/send` | **Kept — re-implemented internal-only.** |
| `app/Http/Controllers/Establishments/Offers/ShowOfferRegulationController.php` | `GET /api/establishments/contracts-regulations` | **Kept.** Returns regulatory text; not Ajeer-coupled despite the grep hit (probably mentions Ajeer in static text). Rename path to `/offer-regulations` (Q-PF-3) or keep for compat. |
| `app/Http/Controllers/Establishments/Offers/Sponsors/ApprovePendingApprovalOfferController.php` | `POST /api/establishments/offers/{offer}/sponsor/accept` | **Kept — re-implemented internal-only.** Sponsor flow is internal status logic. |
| `app/Http/Controllers/Establishments/Offers/Sponsors/RejectPendingApprovalOfferController.php` | `POST /api/establishments/offers/{offer}/sponsor/reject` | **Kept — re-implemented internal-only.** |
| `app/Http/Controllers/Users/Offers/AcceptOfferController.php` | `POST /api/users/offers/{offer}/accept` | **Kept — re-implemented internal-only.** |
| `app/Http/Controllers/Users/Offers/RejectOfferController.php` | `POST /api/users/offers/{offer}/reject` | **Kept — re-implemented internal-only.** |
| `app/Http/Controllers/Users/Offers/CancelOfferController.php` | `POST /api/users/offers/cancel` | **Kept — re-implemented internal-only.** |
| `app/Http/Controllers/Users/Offers/AcceptOfferCancellationRequestController.php` | `POST /api/users/offers/{offer}/approve-cancellation` | **Kept — re-implemented internal-only.** |
| `app/Http/Controllers/Users/Offers/RejectOfferCancellationRequestController.php` | `POST /api/users/offers/{offer}/reject-cancellation` | **Kept — re-implemented internal-only.** |
| **(missing in repo)** `HandleAjeerInvoiceUpdateCallbackController` | Webhook from Ajeer | **Not ported.** Webhook is removed. |
| `app/Http/Controllers/PrintContractController.php` | `GET /print-contract/{contract}` (signed URL) | **Removed.** Contract entity gone. |
| `app/Http/Controllers/Admin/Auth/SsoCallbackController.php` | `GET /admin/auth/callback` | **Removed** (Filament admin gone — Angular owns its own callback). The grep flagged this because the Ajeer enum and contracts entity are imported transitively; the controller itself is OIDC-callback logic and has no Ajeer-specific code worth porting. |

### Requests

| File | Disposition |
|---|---|
| `app/Http/Requests/Establishments/Invoices/IssueInvoiceRequest.php` | **Removed.** |
| `app/Http/Requests/Base/RejectionRequest.php` | **Kept.** Generic rejection-reason form request. The grep hit because OfferRejectionReason and AjeerRejectionReason share a base. Port the base; drop any Ajeer-specific subclass. |
| `app/Http/Requests/Offers/CancelOfferRequest.php` | **Kept — re-implemented internal-only.** Validation rules port; any Ajeer-conditional rule (e.g. "reason required if contract exists in Ajeer") becomes unconditional or is dropped. |

### Resources

| File | Disposition |
|---|---|
| `app/Http/Resources/Contracts/ContractResource.php` | **Removed.** Contract entity gone. |
| `app/Http/Resources/Establishments/Offers/OfferResource.php` | **Kept — fields trimmed.** Drop any `ajeer_*`, `contract`, or `invoice` nested fields. Confirm Q-PF-4/Q-PF-6 first. |
| `app/Http/Resources/OfferRejectionReasonResource.php` | **Kept.** Reference data; no Ajeer logic. |

### Models

| File | Disposition |
|---|---|
| `app/Models/Contract.php` | **Removed.** |
| `app/Models/Invoice.php` | **Removed.** |
| `app/Models/OfferCancellationReason.php` | **Kept.** Reference data. |
| `app/Models/OfferRejectionReason.php` | **Kept.** Reference data. |

### Query builders & support

| File | Disposition |
|---|---|
| `app/QueryBuilders/OfferQueryBuilder.php` | **Kept — re-implemented internal-only.** Strip any joins or filters on `contracts` / `invoices` / Ajeer status. |
| `app/Support/ContractSupport.php` | **Removed.** Contract entity gone. |

### Database migrations

| Migration | Disposition |
|---|---|
| `create_ajeer_notifications_table` | **Not ported.** The table itself doesn't exist in the new schema. |
| `create_api_logs_table` | **Not ported.** Was a log of Ajeer HTTP calls. Replaced by structured OTel HTTP-client telemetry. |
| `create_contracts_table` | **Not ported.** |
| `create_contract_invoice_table` | **Not ported.** |
| `create_invoices_table` | **Not ported.** |

(All other migrations are kept and re-expressed as EF Core migrations. See data migration plan.)

### Filament admin

| File | Disposition |
|---|---|
| `app/Filament/Resources/SettingResource.php` | **Kept logic — re-implemented in Angular admin.** The grep hit because settings table includes an `ajeer_enabled` toggle. The setting key is **dropped** from the seed data; the Angular admin shows the rest. |
| Other Filament resources that reference contracts/invoices | **Removed.** No replacement in Angular admin. |

### Notifications

The grep didn't hit the `app/Notifications/` directory directly, but the Ajeer flow raised these:

| Notification | Disposition |
|---|---|
| `OfferAcceptedNotification` | **Kept.** Raised internally on offer accept. |
| `OfferRejectedNotification` | **Kept.** |
| Any Ajeer-status notification (e.g. "Ajeer contract issued") | **Removed.** |

---

## Old side-effects that disappear

Each Ajeer side-effect of a currently-public endpoint, and what the new system does instead:

| Old side-effect | New behavior |
|---|---|
| On `POST /offers/{offer}/accept`, Laravel called Ajeer to issue a contract, wrote `contracts` row, linked it to the offer, set `offers.contract_id` | New: only sets `offers.status = Accepted`. No contract created. `OfferAccepted` domain event raised; handlers can hook for notifications/audit. |
| On `POST /offers/{offer}/accept`, returned OfferResource included `contract: { ajeer_contract_number, ajeer_contract_status, … }` | New: nested `contract` field drops out of the response. Confirm Q-PF-6 that public frontend tolerates this. |
| On `POST /offers/cancel`, Laravel may have called Ajeer to terminate the existing contract | New: pure status transition. No external call. |
| On `POST /offers/{offer}/sponsor/accept`, may have triggered Ajeer post-actions | New: pure internal sponsor-approval state machine. |
| On `POST /invoices/issue`, Laravel called Ajeer to issue an invoice, wrote `invoices` row | New: endpoint removed. Invoice concept gone. |
| Background job `app:sync-offers-statuses` polled Ajeer for contract status changes and reflected them in `offers.status` / `contracts.ajeer_status` | New: scheduled job is removed. Offer status only changes from explicit API calls. |
| Background job `app:ajeer-notification` (command class was missing) | New: removed. |
| Inbound `POST /…/ajeer/invoice-status-callback` webhook (controller missing from repo, but referenced) | New: endpoint not created. If Ajeer is still sending it, traffic returns 404. |
| `CheckAjeerEnabled` middleware short-circuited Ajeer-dependent endpoints when the integration was disabled | New: middleware removed. There is no Ajeer to disable. |

---

## What this means for the new build

- **Phase 4** (Infrastructure) does *not* ship any integration adapters or Fake-mode services. The `Matloob.Domain/Integrations/` and `Matloob.Api/Infrastructure/Integrations/` folders are not created.
- **Phase 10** (formerly "Offers + Contracts + Invoices") collapses to **"Offers (lifecycle only)"** — Send, Accept, Reject, Cancel, cancellation-requests, sponsor approval. No contract or invoice slices.
- No Ajeer credentials are needed for any environment (dev, staging, production).
- No background sync jobs run for Ajeer.
- No webhook surface area accepts Ajeer callbacks.

---

## What still needs team confirmation

| # | Question |
|---|---|
| Q-AJ-1 | The `ContractType` enum has values like `Direct` / `Sponsored` / `Ajeer`. Is `ContractType` (rename: `OfferContractKind`) still used by **Offers** to categorize the kind of arrangement, or did it only exist to drive Ajeer-vs-non-Ajeer branching? *(My default: drop the enum entirely; offers don't need it.)* |
| Q-AJ-2 | Are there any third parties (Qiwa? a reporting BI tool?) that **read** the Laravel `contracts` or `invoices` tables directly? If yes, the migration must hold them open for a deprecation window. *(My default: no third parties; just delete.)* |
| Q-AJ-3 | Settings table contains `ajeer_enabled`, `ajeer_api_url`, etc. keys. **Delete these settings rows during migration?** *(My default: yes, drop on import.)* |

— end —

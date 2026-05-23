import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { OfferService } from '../../core/services/offer.service';
import { Offer } from '../../core/models/offer';
import { ProfileService } from '../../core/services/profile.service';
import { LoadingComponent } from '../../shared/components/loading.component';
import { ConfirmDialogService } from '../../shared/components/confirm-dialog.service';
import { ToastService } from '../../shared/components/toast.service';
import { ApiErrorService } from '../../core/http/api-error.service';

/**
 * Offer detail page. Loads via the sent-offers endpoint first; if that
 * 404s (caller isn't the sender), retries against received-offers.
 * Accept / reject / cancel / sponsor actions gate on the offer status
 * and the caller's role with respect to the offer.
 *
 * Ajeer / contract / invoice surfaces are deliberately absent — those
 * concepts no longer exist (docs/25-ajeer-disposition.md). `accepted_at`
 * is the post-Ajeer replacement for the legacy `contract.created_at`.
 */
@Component({
  selector: 'app-offer-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, LoadingComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <a class="muted" [routerLink]="['/offers']">← Offers</a>
        <h1>Offer</h1>
        <p class="muted" *ngIf="offer() as o">
          <span class="badge">{{ o.status }}</span>
          <span *ngIf="o.expired"> · expired</span>
          <span *ngIf="o.is_pending_sponsor_approval"> · pending sponsor</span>
        </p>
      </header>

      <app-loading *ngIf="loading()" />

      <ng-container *ngIf="!loading() && offer() as o">
        <section class="card actions">
          <button
            class="btn"
            *ngIf="canAct() && o.status !== 'accepted'"
            (click)="onAccept()"
            [disabled]="busy()"
          >Accept</button>
          <button
            class="btn btn-danger"
            *ngIf="canAct() && o.status !== 'rejected' && o.status !== 'cancelled'"
            (click)="onReject()"
            [disabled]="busy()"
          >Reject</button>
          <button
            class="btn btn-secondary"
            *ngIf="o.status === 'accepted' && !o.cancellation_request"
            (click)="onCancelRequest()"
            [disabled]="busy()"
          >Request cancellation</button>
          <button
            class="btn"
            *ngIf="o.cancellation_request && !o.cancellation_request.is_approved && !o.cancellation_request.is_rejected"
            (click)="onApproveCancellation()"
            [disabled]="busy()"
          >Approve cancellation</button>
          <button
            class="btn btn-danger"
            *ngIf="o.cancellation_request && !o.cancellation_request.is_approved && !o.cancellation_request.is_rejected"
            (click)="onRejectCancellation()"
            [disabled]="busy()"
          >Reject cancellation</button>
          <button
            class="btn"
            *ngIf="o.is_pending_sponsor_approval"
            (click)="onSponsorAccept()"
            [disabled]="busy()"
          >Sponsor accept</button>
          <button
            class="btn btn-danger"
            *ngIf="o.is_pending_sponsor_approval"
            (click)="onSponsorReject()"
            [disabled]="busy()"
          >Sponsor reject</button>
        </section>

        <section class="card">
          <h2>Parties</h2>
          <dl class="kv">
            <div>
              <dt>Sender</dt>
              <dd>{{ o.sender?.name || o.sender?.email || '—' }}</dd>
            </div>
            <div>
              <dt>Applicant</dt>
              <dd>{{ o.applicant?.applier?.name || o.applicant?.applier?.email || '—' }}</dd>
            </div>
            <div *ngIf="o.opportunity">
              <dt>Opportunity</dt>
              <dd>
                <a [routerLink]="['/opportunities', o.opportunity.id]">{{ o.opportunity.name }}</a>
              </dd>
            </div>
          </dl>
        </section>

        <section class="card">
          <h2>Terms</h2>
          <dl class="kv">
            <div><dt>Job title</dt><dd>{{ o.job_title?.name ?? '—' }}</dd></div>
            <div><dt>Monthly salary</dt><dd>{{ o.monthly_salary ?? '—' }} {{ o.currency }}</dd></div>
            <div><dt>Daily wage</dt><dd>{{ o.daily_wage ?? '—' }}</dd></div>
            <div><dt>Working days</dt><dd>{{ o.number_of_working_days ?? '—' }}</dd></div>
            <div><dt>Validity</dt><dd>{{ o.offer_validity_from ?? '—' }} → {{ o.offer_validity_to ?? '—' }}</dd></div>
            <div><dt>Start / end</dt><dd>{{ o.start_date ?? '—' }} → {{ o.end_date ?? '—' }}</dd></div>
            <div><dt>Created</dt><dd>{{ o.created_at }}</dd></div>
            <div *ngIf="o.accepted_at"><dt>Accepted</dt><dd>{{ o.accepted_at | date: 'medium' }}</dd></div>
          </dl>
          <p *ngIf="o.laborer_commitments" class="muted">
            <strong>Laborer commitments:</strong> {{ o.laborer_commitments }}
          </p>
        </section>

        <section class="card" *ngIf="o.cancellation_request as cr">
          <h2>Cancellation request</h2>
          <dl class="kv">
            <div><dt>Requested by</dt><dd>{{ cr.requested_by_type }}</dd></div>
            <div><dt>Requested at</dt><dd>{{ cr.requested_at | date: 'medium' }}</dd></div>
            <div *ngIf="cr.other_reason"><dt>Reason</dt><dd>{{ cr.other_reason }}</dd></div>
            <div *ngIf="cr.is_approved"><dt>Status</dt><dd>Approved</dd></div>
            <div *ngIf="cr.is_rejected"><dt>Status</dt><dd>Rejected</dd></div>
          </dl>
        </section>
      </ng-container>
    </div>
  `,
  styles: [
    `
      .actions { display: flex; gap: 8px; flex-wrap: wrap; }
      .kv { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px 24px; margin: 0; }
      .kv div { display: flex; flex-direction: column; }
      .kv dt { color: #6b7280; font-size: 12px; text-transform: uppercase; letter-spacing: 0.04em; }
      .kv dd { margin: 0; font-weight: 500; }
    `,
  ],
})
export class OfferDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(OfferService);
  private readonly profile = inject(ProfileService);
  private readonly dialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly apiError = inject(ApiErrorService);

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly offer = signal<Offer | null>(null);
  protected readonly direction = signal<'sent' | 'received' | null>(null);
  protected readonly id = computed(() => this.route.snapshot.paramMap.get('id') ?? '');

  /** Establishment is the receiver — can accept/reject. */
  protected readonly canAct = computed(() => this.direction() === 'received');

  ngOnInit(): void { this.reload(); }

  private reload(): void {
    const eid = this.profile.activeEstablishmentId();
    const id = this.id();
    if (!eid || !id) {
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.service.getSent(eid, id).subscribe({
      next: (o) => {
        this.offer.set(o);
        this.direction.set('sent');
        this.loading.set(false);
      },
      error: () => {
        this.service.getReceived(eid, id).subscribe({
          next: (o) => {
            this.offer.set(o);
            this.direction.set('received');
            this.loading.set(false);
          },
          error: () => this.loading.set(false),
        });
      },
    });
  }

  private run(label: string, op: () => ReturnType<typeof this.service.accept>): void {
    this.busy.set(true);
    op().subscribe({
      next: (o) => {
        this.offer.set(o);
        this.toast.success(label);
        this.busy.set(false);
      },
      error: (err) => {
        this.apiError.notify(err, `${label.replace(/\.$/, '')} failed`);
        this.busy.set(false);
      },
    });
  }

  async onAccept(): Promise<void> {
    const eid = this.profile.activeEstablishmentId();
    if (!eid) return;
    const ok = await this.dialog.confirm({ title: 'Accept offer?', confirmLabel: 'Accept' });
    if (!ok) return;
    this.run('Offer accepted.', () => this.service.accept(eid, this.id()));
  }

  async onReject(): Promise<void> {
    const eid = this.profile.activeEstablishmentId();
    if (!eid) return;
    const ok = await this.dialog.confirm({
      title: 'Reject offer?', kind: 'danger', confirmLabel: 'Reject',
    });
    if (!ok) return;
    this.run('Offer rejected.', () => this.service.reject(eid, this.id()));
  }

  async onCancelRequest(): Promise<void> {
    const eid = this.profile.activeEstablishmentId();
    if (!eid) return;
    const ok = await this.dialog.confirm({
      title: 'Request cancellation?',
      kind: 'danger',
      confirmLabel: 'Continue',
    });
    if (!ok) return;
    const reason = window.prompt('Reason (optional, ≤500 chars):') ?? '';
    this.run('Cancellation requested.', () =>
      this.service.requestCancel(eid, {
        offer_id: this.id(),
        other_reason: reason.trim() || undefined,
      }),
    );
  }

  async onApproveCancellation(): Promise<void> {
    const eid = this.profile.activeEstablishmentId();
    if (!eid) return;
    const ok = await this.dialog.confirm({
      title: 'Approve cancellation?', confirmLabel: 'Approve',
    });
    if (!ok) return;
    this.run('Cancellation approved.', () =>
      this.service.approveCancellation(eid, this.id()),
    );
  }

  async onRejectCancellation(): Promise<void> {
    const eid = this.profile.activeEstablishmentId();
    if (!eid) return;
    const ok = await this.dialog.confirm({
      title: 'Reject cancellation?', kind: 'danger', confirmLabel: 'Reject',
    });
    if (!ok) return;
    this.run('Cancellation rejected.', () =>
      this.service.rejectCancellation(eid, this.id()),
    );
  }

  async onSponsorAccept(): Promise<void> {
    const eid = this.profile.activeEstablishmentId();
    if (!eid) return;
    const ok = await this.dialog.confirm({ title: 'Sponsor: accept offer?' });
    if (!ok) return;
    this.run('Sponsor accepted.', () => this.service.sponsorAccept(eid, this.id()));
  }

  async onSponsorReject(): Promise<void> {
    const eid = this.profile.activeEstablishmentId();
    if (!eid) return;
    const ok = await this.dialog.confirm({
      title: 'Sponsor: reject offer?', kind: 'danger', confirmLabel: 'Reject',
    });
    if (!ok) return;
    this.run('Sponsor rejected.', () => this.service.sponsorReject(eid, this.id()));
  }
}

import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { EstablishmentService } from '../../core/services/establishment.service';
import {
  ReviewDetail,
  ReviewHistoryItem,
} from '../../core/models/admin-establishment';
import { LoadingComponent } from '../../shared/components/loading.component';
import { ConfirmDialogService } from '../../shared/components/confirm-dialog.service';
import { ToastService } from '../../shared/components/toast.service';
import { ApiErrorService } from '../../core/http/api-error.service';

/**
 * Admin review page. Approve / reject / suspend / reinstate buttons
 * toggle based on the establishment's current status. Reject + suspend
 * collect a mandatory reason; the backend validates length too (≤2000).
 */
@Component({
  selector: 'app-review-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, LoadingComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <a class="muted" [routerLink]="['/admin/review-queue']">← Review queue</a>
        <h1>{{ detail()?.name ?? 'Review' }}</h1>
        <p class="muted" *ngIf="detail() as d">
          <span class="badge">{{ d.status }}</span>
          <span> · CR {{ d.commercialRegistrationNumber }}</span>
        </p>
      </header>

      <app-loading *ngIf="loading()" />

      <ng-container *ngIf="!loading() && detail() as d">
        <section class="card actions">
          <button
            class="btn"
            *ngIf="d.status === 'PendingReview'"
            (click)="onApprove()"
            [disabled]="busy()"
          >Approve</button>
          <button
            class="btn btn-danger"
            *ngIf="d.status === 'PendingReview'"
            (click)="onReject()"
            [disabled]="busy()"
          >Reject</button>
          <button
            class="btn btn-danger"
            *ngIf="d.status === 'Approved'"
            (click)="onSuspend()"
            [disabled]="busy()"
          >Suspend</button>
          <button
            class="btn"
            *ngIf="d.status === 'Suspended'"
            (click)="onReinstate()"
            [disabled]="busy()"
          >Reinstate</button>
        </section>

        <section class="card">
          <h2>Profile</h2>
          <dl class="kv">
            <div><dt>Email</dt><dd>{{ d.email }}</dd></div>
            <div><dt>Phone</dt><dd>{{ d.phone }}</dd></div>
            <div><dt>Labor office</dt><dd>{{ d.laborOfficeId }}</dd></div>
            <div><dt>Sequence</dt><dd>{{ d.sequenceNumber }}</dd></div>
            <div><dt>City</dt><dd>{{ d.city }}</dd></div>
            <div><dt>District</dt><dd>{{ d.district ?? '—' }}</dd></div>
            <div><dt>Economic activity</dt><dd>{{ d.economicActivity ?? '—' }}</dd></div>
            <div><dt>Sub activity</dt><dd>{{ d.subEconomicActivity ?? '—' }}</dd></div>
            <div><dt>Years of experience</dt><dd>{{ d.yearsOfExperience ?? '—' }}</dd></div>
            <div><dt>CR expiry</dt><dd>{{ d.commercialRegistrationExpiry ?? '—' }}</dd></div>
            <div><dt>Submitted</dt><dd>{{ d.submittedAt | date: 'medium' }}</dd></div>
            <div *ngIf="d.rejectedAt"><dt>Rejected</dt><dd>{{ d.rejectedAt | date: 'medium' }}</dd></div>
            <div *ngIf="d.rejectionReason" class="full"><dt>Reject reason</dt><dd>{{ d.rejectionReason }}</dd></div>
            <div *ngIf="d.suspendedAt"><dt>Suspended</dt><dd>{{ d.suspendedAt | date: 'medium' }}</dd></div>
            <div *ngIf="d.suspensionReason" class="full"><dt>Suspension reason</dt><dd>{{ d.suspensionReason }}</dd></div>
          </dl>
        </section>

        <section class="card" *ngIf="d.documents.length">
          <h2>Documents</h2>
          <table class="table">
            <thead><tr><th>Type</th><th>Asset</th><th>Uploaded</th></tr></thead>
            <tbody>
              <tr *ngFor="let doc of d.documents">
                <td>{{ doc.documentType }}</td>
                <td class="mono">{{ doc.assetId }}</td>
                <td>{{ doc.uploadedAt | date: 'medium' }}</td>
              </tr>
            </tbody>
          </table>
        </section>

        <section class="card" *ngIf="history().length">
          <h2>Review history</h2>
          <ul class="timeline">
            <li *ngFor="let h of history()">
              <strong>{{ h.action }}</strong>
              <span class="muted"> · {{ h.occurredAt | date: 'medium' }}</span>
              <div *ngIf="h.reason" class="muted">Reason: {{ h.reason }}</div>
            </li>
          </ul>
        </section>
      </ng-container>
    </div>
  `,
  styles: [
    `
      .actions { display: flex; gap: 8px; }
      .kv { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px 24px; margin: 0; }
      .kv div { display: flex; flex-direction: column; }
      .kv .full { grid-column: 1 / -1; }
      .kv dt { color: #6b7280; font-size: 12px; text-transform: uppercase; letter-spacing: 0.04em; }
      .kv dd { margin: 0; font-weight: 500; }
      .mono { font-family: ui-monospace, SFMono-Regular, monospace; }
      .timeline { list-style: none; padding: 0; margin: 0; }
      .timeline li { padding: 8px 0; border-bottom: 1px solid #e5e7eb; }
      .timeline li:last-child { border-bottom: 0; }
    `,
  ],
})
export class ReviewDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(EstablishmentService);
  private readonly dialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly apiError = inject(ApiErrorService);

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly detail = signal<ReviewDetail | null>(null);
  protected readonly history = signal<ReviewHistoryItem[]>([]);

  protected readonly id = computed(() => this.route.snapshot.paramMap.get('id') ?? '');

  ngOnInit(): void {
    this.reload();
  }

  private reload(): void {
    const id = this.id();
    if (!id) {
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.service.getReviewDetail(id).subscribe({
      next: (d) => {
        this.detail.set(d);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
    this.service.getReviewHistory(id).subscribe({
      next: (r) => this.history.set(r.items),
      error: () => undefined,
    });
  }

  async onApprove(): Promise<void> {
    const ok = await this.dialog.confirm({
      title: 'Approve this establishment?',
      message: 'They will gain full access immediately.',
      confirmLabel: 'Approve',
    });
    if (!ok) return;
    this.busy.set(true);
    this.service.approve(this.id()).subscribe({
      next: () => {
        this.toast.success('Approved.');
        this.busy.set(false);
        this.reload();
      },
      error: (err) => {
        this.apiError.notify(err, 'Approve failed');
        this.busy.set(false);
      },
    });
  }

  async onReject(): Promise<void> {
    const reason = await this.promptReason('Reject this establishment?');
    if (!reason) return;
    this.busy.set(true);
    this.service.reject(this.id(), reason).subscribe({
      next: () => {
        this.toast.success('Rejected.');
        this.busy.set(false);
        this.reload();
      },
      error: (err) => {
        this.apiError.notify(err, 'Reject failed');
        this.busy.set(false);
      },
    });
  }

  async onSuspend(): Promise<void> {
    const reason = await this.promptReason('Suspend this establishment?');
    if (!reason) return;
    this.busy.set(true);
    this.service.suspend(this.id(), reason).subscribe({
      next: () => {
        this.toast.success('Suspended.');
        this.busy.set(false);
        this.reload();
      },
      error: (err) => {
        this.apiError.notify(err, 'Suspend failed');
        this.busy.set(false);
      },
    });
  }

  async onReinstate(): Promise<void> {
    const ok = await this.dialog.confirm({
      title: 'Reinstate this establishment?',
      confirmLabel: 'Reinstate',
    });
    if (!ok) return;
    this.busy.set(true);
    this.service.reinstate(this.id()).subscribe({
      next: () => {
        this.toast.success('Reinstated.');
        this.busy.set(false);
        this.reload();
      },
      error: (err) => {
        this.apiError.notify(err, 'Reinstate failed');
        this.busy.set(false);
      },
    });
  }

  private async promptReason(title: string): Promise<string | null> {
    const proceed = await this.dialog.confirm({ title, kind: 'danger', confirmLabel: 'Continue' });
    if (!proceed) return null;
    const reason = window.prompt('Reason (required, ≤2000 chars):');
    if (!reason || !reason.trim()) {
      this.toast.warning('Reason is required.');
      return null;
    }
    return reason.trim();
  }
}

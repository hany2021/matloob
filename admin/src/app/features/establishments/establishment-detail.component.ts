import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';

import { EstablishmentService } from '../../core/services/establishment.service';
import { AssetService } from '../../core/services/asset.service';
import { AuthService } from '../../core/auth/auth.service';
import { EstablishmentDetail } from '../../core/models/establishment';
import { MemberSummary } from '../../core/models/admin-establishment';
import { LoadingComponent } from '../../shared/components/loading.component';
import { ConfirmDialogService } from '../../shared/components/confirm-dialog.service';
import { ToastService } from '../../shared/components/toast.service';
import { ApiErrorService } from '../../core/http/api-error.service';

/**
 * Self-read establishment detail page. Composite snake_case shape from
 * `GET /api/v1/establishments/{id}`. Loads the member list in parallel
 * (admin-only data — `403` is silently swallowed since the page still
 * works for the establishment owner).
 */
@Component({
  selector: 'app-establishment-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, LoadingComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <a class="muted" [routerLink]="['/establishments']">← Establishments</a>
        <h1>{{ detail()?.name ?? 'Establishment' }}</h1>
        <p class="muted" *ngIf="detail() as d">
          <span class="badge">{{ d.status }}</span>
          <span> · CR {{ d.commercialRegistrationNumber ?? '—' }}</span>
        </p>
      </header>

      <app-loading *ngIf="loading()" />

      <ng-container *ngIf="!loading() && detail() as d">
        <section class="card actions" *ngIf="isAdmin()">
          <button class="btn" *ngIf="d.status === 'PendingReview'" (click)="onApprove()" [disabled]="busy()">Approve</button>
          <button class="btn btn-danger" *ngIf="d.status === 'PendingReview'" (click)="onReject()" [disabled]="busy()">Reject</button>
          <button class="btn btn-danger" *ngIf="d.status === 'Approved'" (click)="onSuspend()" [disabled]="busy()">Suspend</button>
          <button class="btn" *ngIf="d.status === 'Suspended'" (click)="onReinstate()" [disabled]="busy()">Reinstate</button>
          <span class="muted" *ngIf="d.status === 'Draft' || d.status === 'Rejected'">
            No review actions in status {{ d.status }}.
          </span>
        </section>

        <section class="card">
          <h2>General</h2>
          <dl class="kv">
            <div><dt>Email</dt><dd>{{ d.email }}</dd></div>
            <div><dt>Phone</dt><dd>{{ d.phone ?? '—' }}</dd></div>
            <div><dt>City</dt><dd>{{ d.city ?? '—' }}</dd></div>
            <div><dt>Economic activity</dt><dd>{{ d.economicActivity ?? '—' }}</dd></div>
            <div><dt>Sub activity</dt><dd>{{ d.subEconomicActivity ?? '—' }}</dd></div>
            <div><dt>Website</dt><dd>{{ d.website ?? '—' }}</dd></div>
            <div><dt>Establishment size</dt><dd>{{ d.establishmentSize ?? '—' }}</dd></div>
            <div><dt>Years of experience</dt><dd>{{ d.yearsOfExperience ?? '—' }}</dd></div>
            <div><dt>Can manage events</dt><dd>{{ d.canManageEvents ? 'Yes' : 'No' }}</dd></div>
            <div><dt>Is sponsor</dt><dd>{{ d.isSponsor ? 'Yes' : 'No' }}</dd></div>
          </dl>
        </section>

        <section class="card" *ngIf="d.documents.length">
          <h2>Documents</h2>
          <table class="table">
            <thead><tr><th>Type</th><th>Uploaded</th><th></th></tr></thead>
            <tbody>
              <tr *ngFor="let doc of d.documents">
                <td>{{ doc.documentType }}</td>
                <td>{{ doc.uploadedAt | date: 'medium' }}</td>
                <td class="doc-actions">
                  <button class="btn btn-ghost" (click)="assets.openInNewTab(doc.assetId)">Preview</button>
                  <button class="btn btn-ghost" (click)="assets.saveAs(doc.assetId, doc.documentType)">Download</button>
                </td>
              </tr>
            </tbody>
          </table>
        </section>

        <section class="card">
          <h2>Members</h2>
          <table class="table" *ngIf="members().length; else noMembers">
            <thead>
              <tr>
                <th>User</th>
                <th>Role</th>
                <th>Active</th>
                <th>Added</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let m of members()">
                <td class="mono">{{ m.userId }}</td>
                <td>{{ m.role }}</td>
                <td>{{ m.isActive ? 'Yes' : 'No' }}</td>
                <td>{{ m.addedAt | date: 'mediumDate' }}</td>
                <td>
                  <button class="btn btn-ghost btn-danger" (click)="onRemove(m)">Remove</button>
                </td>
              </tr>
            </tbody>
          </table>
          <ng-template #noMembers>
            <p class="muted">No members visible to you.</p>
          </ng-template>
        </section>

        <section class="card" *ngIf="d.pendingChangeRequest as pcr">
          <h2>Pending change request</h2>
          <p>
            Submitted
            {{ pcr.submittedAt | date: 'medium' }}
            by {{ pcr.createdByUserId }}.
            Status: {{ pcr.status }}.
          </p>
        </section>
      </ng-container>
    </div>
  `,
  styles: [
    `
      .kv { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px 24px; margin: 0; }
      .kv div { display: flex; flex-direction: column; }
      .kv dt { color: #6b7280; font-size: 12px; text-transform: uppercase; letter-spacing: 0.04em; }
      .kv dd { margin: 0; font-weight: 500; }
      .mono { font-family: ui-monospace, SFMono-Regular, monospace; }
      .actions { display: flex; gap: 8px; align-items: center; }
      .doc-actions { display: flex; gap: 8px; }
    `,
  ],
})
export class EstablishmentDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(EstablishmentService);
  protected readonly assets = inject(AssetService);
  private readonly dialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly apiError = inject(ApiErrorService);
  private readonly auth = inject(AuthService);

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly detail = signal<EstablishmentDetail | null>(null);
  protected readonly members = signal<MemberSummary[]>([]);
  protected readonly isAdmin = this.auth.isAdmin;

  protected readonly id = computed(() => this.route.snapshot.paramMap.get('id') ?? '');

  ngOnInit(): void {
    if (!this.id()) {
      this.loading.set(false);
      return;
    }
    this.reload();
    this.service.listMembers(this.id()).subscribe({
      next: (r) => this.members.set(r.members),
      // 403/404 here just means the caller isn't permitted to enumerate
      // members — leave the section empty rather than failing the page.
      error: () => undefined,
    });
  }

  private reload(): void {
    this.loading.set(true);
    this.service.getDetail(this.id()).subscribe({
      next: (d) => {
        this.detail.set(d);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  // -- Admin review actions (status-aware; backend enforces admin-only) ----

  async onApprove(): Promise<void> {
    const ok = await this.dialog.confirm({
      title: 'Approve this establishment?',
      message: 'They will gain full access immediately.',
      confirmLabel: 'Approve',
    });
    if (!ok) return;
    this.runAction(this.service.approve(this.id()), 'Approved.', 'Approve failed');
  }

  async onReject(): Promise<void> {
    const reason = await this.promptReason('Reject this establishment?');
    if (!reason) return;
    this.runAction(this.service.reject(this.id(), reason), 'Rejected.', 'Reject failed');
  }

  async onSuspend(): Promise<void> {
    const reason = await this.promptReason('Suspend this establishment?');
    if (!reason) return;
    this.runAction(this.service.suspend(this.id(), reason), 'Suspended.', 'Suspend failed');
  }

  async onReinstate(): Promise<void> {
    const ok = await this.dialog.confirm({ title: 'Reinstate this establishment?', confirmLabel: 'Reinstate' });
    if (!ok) return;
    this.runAction(this.service.reinstate(this.id()), 'Reinstated.', 'Reinstate failed');
  }

  private runAction(obs: Observable<unknown>, success: string, failure: string): void {
    this.busy.set(true);
    obs.subscribe({
      next: () => {
        this.toast.success(success);
        this.busy.set(false);
        this.reload();
      },
      error: (err) => {
        this.apiError.notify(err, failure);
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

  async onRemove(member: MemberSummary): Promise<void> {
    const confirmed = await this.dialog.confirm({
      title: 'Remove member?',
      message: `User ${member.userId} will lose access immediately.`,
      kind: 'danger',
      confirmLabel: 'Remove',
    });
    if (!confirmed) return;
    this.service.removeMember(this.id(), member.id).subscribe({
      next: () => {
        this.members.update((list) => list.filter((m) => m.id !== member.id));
        this.toast.success('Member removed.');
      },
      error: (err) => this.apiError.notify(err, 'Remove member failed'),
    });
  }
}

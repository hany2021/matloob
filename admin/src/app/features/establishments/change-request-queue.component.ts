import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';

import { EstablishmentService } from '../../core/services/establishment.service';
import { PendingChangeRequestItem } from '../../core/models/admin-establishment';
import { LoadingComponent } from '../../shared/components/loading.component';
import { EmptyStateComponent } from '../../shared/components/empty-state.component';
import { ConfirmDialogService } from '../../shared/components/confirm-dialog.service';
import { ToastService } from '../../shared/components/toast.service';
import { ApiErrorService } from '../../core/http/api-error.service';

/**
 * Admin queue of submitted establishment change requests awaiting a
 * decision. Approve / reject inline so the admin can churn through the
 * queue without a separate detail page.
 */
@Component({
  selector: 'app-change-request-queue',
  standalone: true,
  imports: [CommonModule, LoadingComponent, EmptyStateComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <h1>Pending change requests</h1>
        <p class="muted">Edits submitted by approved establishments.</p>
      </header>

      <app-loading *ngIf="loading()" />

      <ng-container *ngIf="!loading()">
        <app-empty-state
          *ngIf="!rows().length"
          heading="No change requests"
          message="Submitted change requests will appear here, oldest first."
        />
        <table class="table" *ngIf="rows().length">
          <thead>
            <tr>
              <th>Establishment</th>
              <th>CR number</th>
              <th>Submitted</th>
              <th>Submitted by</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let r of rows()">
              <td>{{ r.establishmentName }}</td>
              <td>{{ r.commercialRegistrationNumber }}</td>
              <td>{{ r.submittedAt | date: 'medium' }}</td>
              <td class="mono">{{ r.createdByUserId }}</td>
              <td class="actions">
                <button class="btn" (click)="onApprove(r)" [disabled]="busy() === r.id">Approve</button>
                <button class="btn btn-danger" (click)="onReject(r)" [disabled]="busy() === r.id">Reject</button>
              </td>
            </tr>
          </tbody>
        </table>
      </ng-container>
    </div>
  `,
  styles: [
    `
      .actions { display: flex; gap: 8px; }
      .mono { font-family: ui-monospace, SFMono-Regular, monospace; }
    `,
  ],
})
export class ChangeRequestQueueComponent implements OnInit {
  private readonly service = inject(EstablishmentService);
  private readonly dialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly apiError = inject(ApiErrorService);

  protected readonly loading = signal(true);
  protected readonly rows = signal<PendingChangeRequestItem[]>([]);
  protected readonly busy = signal<string | null>(null);

  ngOnInit(): void { this.reload(); }

  private reload(): void {
    this.loading.set(true);
    this.service.listPendingChangeRequests().subscribe({
      next: (resp) => {
        this.rows.set(resp.items);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  async onApprove(item: PendingChangeRequestItem): Promise<void> {
    const ok = await this.dialog.confirm({
      title: `Approve change request for ${item.establishmentName}?`,
      message: 'The proposed values will be applied to the establishment record.',
      confirmLabel: 'Approve',
    });
    if (!ok) return;
    this.busy.set(item.id);
    this.service.approveChangeRequest(item.id).subscribe({
      next: () => {
        this.toast.success('Change request approved.');
        this.busy.set(null);
        this.rows.update((list) => list.filter((r) => r.id !== item.id));
      },
      error: (err) => {
        this.apiError.notify(err, 'Approve failed');
        this.busy.set(null);
      },
    });
  }

  async onReject(item: PendingChangeRequestItem): Promise<void> {
    const proceed = await this.dialog.confirm({
      title: `Reject change request for ${item.establishmentName}?`,
      kind: 'danger',
      confirmLabel: 'Continue',
    });
    if (!proceed) return;
    const reason = window.prompt('Reason (required, ≤2000 chars):');
    if (!reason || !reason.trim()) {
      this.toast.warning('Reason is required.');
      return;
    }
    this.busy.set(item.id);
    this.service.rejectChangeRequest(item.id, reason.trim()).subscribe({
      next: () => {
        this.toast.success('Change request rejected.');
        this.busy.set(null);
        this.rows.update((list) => list.filter((r) => r.id !== item.id));
      },
      error: (err) => {
        this.apiError.notify(err, 'Reject failed');
        this.busy.set(null);
      },
    });
  }
}

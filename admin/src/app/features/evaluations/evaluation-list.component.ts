import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { EvaluationService } from '../../core/services/evaluation.service';
import { Evaluation } from '../../core/models/evaluation';
import { Offer } from '../../core/models/offer';
import { ProfileService } from '../../core/services/profile.service';
import { LoadingComponent } from '../../shared/components/loading.component';
import { EmptyStateComponent } from '../../shared/components/empty-state.component';

/**
 * Lists evaluations the active establishment has authored, plus a
 * "left to evaluate" panel of accepted offers that don't yet have an
 * evaluation row. The unevaluated endpoint returns full Offer records
 * (same shape as sent/received reads); we project the bits the table
 * needs and fall back gracefully when a side of the offer isn't loaded.
 */
@Component({
  selector: 'app-evaluation-list',
  standalone: true,
  imports: [CommonModule, RouterLink, LoadingComponent, EmptyStateComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <h1>Evaluations</h1>
        <p class="muted">{{ activeName() ?? '—' }}</p>
      </header>

      <app-empty-state
        *ngIf="!activeEstablishmentId()"
        heading="No active establishment"
        message="You need to be a member of an Approved establishment to view evaluations."
      />

      <app-loading *ngIf="activeEstablishmentId() && loading()" />

      <ng-container *ngIf="activeEstablishmentId() && !loading()">
        <section class="card" *ngIf="pending().length">
          <h2>Left to evaluate</h2>
          <table class="table">
            <thead>
              <tr>
                <th>Opportunity</th>
                <th>Counterparty</th>
                <th>Accepted</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let o of pending()">
                <td>{{ opportunityName(o) }}</td>
                <td>{{ counterpartyName(o) }}</td>
                <td>{{ o.accepted_at | date: 'mediumDate' }}</td>
                <td>
                  <a
                    class="btn btn-ghost"
                    [routerLink]="['/evaluations/new']"
                    [queryParams]="{ offer_id: o.id }"
                  >Evaluate</a>
                </td>
              </tr>
            </tbody>
          </table>
        </section>

        <section class="card">
          <h2>Submitted</h2>
          <app-empty-state
            *ngIf="!rows().length"
            heading="No evaluations yet"
            message="Once you evaluate an accepted offer it'll appear here."
          />
          <table class="table" *ngIf="rows().length">
            <thead>
              <tr>
                <th>Opportunity</th>
                <th>Evaluable</th>
                <th>Rating</th>
                <th>Recommend</th>
                <th>Created</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let e of rows()">
                <td>{{ e.opportunity?.name ?? '—' }}</td>
                <td>{{ e.evaluable?.name || e.evaluable?.email || '—' }}</td>
                <td>{{ e.rating }} / 5</td>
                <td>{{ e.recommend_for_future_opportunities ? 'Yes' : 'No' }}</td>
                <td>{{ e.created_at }}</td>
              </tr>
            </tbody>
          </table>
        </section>
      </ng-container>
    </div>
  `,
})
export class EvaluationListComponent implements OnInit {
  private readonly service = inject(EvaluationService);
  private readonly profile = inject(ProfileService);

  protected readonly loading = signal(true);
  protected readonly rows = signal<Evaluation[]>([]);
  protected readonly pending = signal<Offer[]>([]);

  protected readonly activeEstablishmentId = this.profile.activeEstablishmentId;
  protected readonly activeName = computed(
    () => this.profile.establishments().find((e) => e.id === this.activeEstablishmentId())?.name ?? null,
  );

  constructor() {
    effect(() => {
      const eid = this.activeEstablishmentId();
      if (eid) this.reload(eid);
    });
  }

  ngOnInit(): void {}

  protected opportunityName(o: Offer): string {
    return o.opportunity?.name ?? '—';
  }

  /**
   * Pick the "other party" on the offer. If the active establishment
   * was the sender, the applicant is the counterparty; otherwise the
   * sender is. We don't know which side we are from the row alone, so
   * we prefer whichever name is populated.
   */
  protected counterpartyName(o: Offer): string {
    const applicantParty =
      o.applicant?.applier?.name || o.applicant?.applier?.email;
    const senderParty = o.sender?.name || o.sender?.email;
    return applicantParty || senderParty || '—';
  }

  private reload(eid: string): void {
    this.loading.set(true);
    let listDone = false;
    let pendingDone = false;
    const tryFinish = () => { if (listDone && pendingDone) this.loading.set(false); };

    this.service.list(eid).subscribe({
      next: (rows) => { this.rows.set(rows); listDone = true; tryFinish(); },
      error: () => { listDone = true; tryFinish(); },
    });
    this.service.listUnevaluatedOffers(eid).subscribe({
      next: (rows) => { this.pending.set(rows); pendingDone = true; tryFinish(); },
      error: () => { pendingDone = true; tryFinish(); },
    });
  }
}

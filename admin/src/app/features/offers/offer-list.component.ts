import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { OfferService } from '../../core/services/offer.service';
import { Offer } from '../../core/models/offer';
import { ProfileService } from '../../core/services/profile.service';
import { LoadingComponent } from '../../shared/components/loading.component';
import { EmptyStateComponent } from '../../shared/components/empty-state.component';

type Tab = 'sent' | 'received' | 'pending';

/**
 * Lists offers for the active establishment, with a tab strip for
 * sent / received / pending-action. Sent is the most common view for
 * issuing establishments; received covers cases where the
 * establishment itself is the applicant.
 */
@Component({
  selector: 'app-offer-list',
  standalone: true,
  imports: [CommonModule, RouterLink, LoadingComponent, EmptyStateComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <h1>Offers</h1>
        <p class="muted">{{ activeName() ?? '—' }}</p>
      </header>

      <nav class="tabs" *ngIf="activeEstablishmentId()">
        <button class="tab" [class.active]="tab() === 'sent'" (click)="onTab('sent')">Sent</button>
        <button class="tab" [class.active]="tab() === 'received'" (click)="onTab('received')">Received</button>
        <button class="tab" [class.active]="tab() === 'pending'" (click)="onTab('pending')">Pending action</button>
      </nav>

      <app-empty-state
        *ngIf="!activeEstablishmentId()"
        heading="No active establishment"
        message="You need to be a member of an Approved establishment to view offers."
      />

      <app-loading *ngIf="activeEstablishmentId() && loading()" />

      <ng-container *ngIf="activeEstablishmentId() && !loading()">
        <app-empty-state
          *ngIf="!rows().length"
          heading="No offers"
        />
        <table class="table" *ngIf="rows().length">
          <thead>
            <tr>
              <th>{{ tab() === 'sent' ? 'Applicant' : 'Sender' }}</th>
              <th>Opportunity</th>
              <th>Status</th>
              <th>Created</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let o of rows()">
              <td>
                {{
                  tab() === 'sent'
                    ? (o.applicant?.applier?.name || o.applicant?.applier?.email || '—')
                    : (o.sender?.name || o.sender?.email || '—')
                }}
              </td>
              <td>{{ o.opportunity?.name ?? '—' }}</td>
              <td><span class="badge">{{ o.status }}</span></td>
              <td>{{ o.created_at }}</td>
              <td>
                <a class="btn btn-ghost" [routerLink]="['/offers', o.id]">Open</a>
              </td>
            </tr>
          </tbody>
        </table>
      </ng-container>
    </div>
  `,
  styles: [
    `
      .tabs { display: flex; gap: 4px; margin-bottom: 16px; }
      .tab {
        padding: 8px 16px;
        border: 0;
        background: transparent;
        border-bottom: 2px solid transparent;
        cursor: pointer;
        font-size: 14px;
        color: #4b5563;
      }
      .tab.active { border-bottom-color: #1f5eff; color: #1f5eff; font-weight: 500; }
    `,
  ],
})
export class OfferListComponent implements OnInit {
  private readonly service = inject(OfferService);
  private readonly profile = inject(ProfileService);

  protected readonly tab = signal<Tab>('sent');
  protected readonly loading = signal(true);
  protected readonly rows = signal<Offer[]>([]);
  protected readonly activeEstablishmentId = this.profile.activeEstablishmentId;
  protected readonly activeName = computed(
    () => this.profile.establishments().find((e) => e.id === this.activeEstablishmentId())?.name ?? null,
  );

  constructor() {
    effect(() => {
      const eid = this.activeEstablishmentId();
      const t = this.tab();
      if (eid) this.reload(eid, t);
    });
  }

  ngOnInit(): void {}

  onTab(t: Tab): void {
    this.tab.set(t);
  }

  private reload(eid: string, t: Tab): void {
    this.loading.set(true);
    const obs =
      t === 'sent' ? this.service.listSent(eid)
      : t === 'received' ? this.service.listReceived(eid)
      : this.service.listPendingAction(eid);
    obs.subscribe({
      next: (rows) => {
        this.rows.set(rows);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}

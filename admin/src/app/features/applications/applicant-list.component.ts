import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { OpportunityService } from '../../core/services/opportunity.service';
import { ProfileService } from '../../core/services/profile.service';
import { OpportunityApplication } from '../../core/models/application';
import { LoadingComponent } from '../../shared/components/loading.component';
import { EmptyStateComponent } from '../../shared/components/empty-state.component';

/**
 * Lists applicants on a single opportunity. Backend returns applicants
 * that don't have an offer yet (Laravel's `whereDoesntHave('offer')`),
 * so this view is "still in the funnel" — applicants who already
 * accepted an offer surface in the Offers section instead.
 */
@Component({
  selector: 'app-applicant-list',
  standalone: true,
  imports: [CommonModule, RouterLink, LoadingComponent, EmptyStateComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <a class="muted" [routerLink]="['/opportunities', opportunityId()]">← Opportunity</a>
        <h1>Applicants</h1>
        <p class="muted">{{ rows().length }} pending</p>
      </header>

      <app-loading *ngIf="loading()" />

      <ng-container *ngIf="!loading()">
        <app-empty-state
          *ngIf="!rows().length"
          heading="No pending applicants"
          message="Applicants who have already received an offer appear under Offers."
        />
        <table class="table" *ngIf="rows().length">
          <thead>
            <tr>
              <th>Applier</th>
              <th>Type</th>
              <th>Applied</th>
              <th>Status</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let a of rows()">
              <td>{{ a.applier?.name || a.applier?.email || '—' }}</td>
              <td>{{ a.applier_type }}</td>
              <td>{{ a.created_at }}</td>
              <td><span class="badge">{{ a.status }}</span></td>
              <td>
                <a class="btn btn-ghost" [routerLink]="['/applicants', a.id]">Open</a>
              </td>
            </tr>
          </tbody>
        </table>
      </ng-container>
    </div>
  `,
})
export class ApplicantListComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(OpportunityService);
  private readonly profile = inject(ProfileService);

  protected readonly loading = signal(true);
  protected readonly rows = signal<OpportunityApplication[]>([]);
  protected readonly opportunityId = computed(() => this.route.snapshot.paramMap.get('id') ?? '');

  ngOnInit(): void {
    const eid = this.profile.activeEstablishmentId();
    const oppId = this.opportunityId();
    if (!eid || !oppId) {
      this.loading.set(false);
      return;
    }
    this.service.listApplicants<OpportunityApplication[]>(eid, oppId).subscribe({
      next: (rows) => {
        this.rows.set(rows);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}

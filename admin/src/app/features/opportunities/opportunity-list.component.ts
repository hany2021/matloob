import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { OpportunityService } from '../../core/services/opportunity.service';
import { Opportunity } from '../../core/models/opportunity';
import { ProfileService } from '../../core/services/profile.service';
import { LoadingComponent } from '../../shared/components/loading.component';
import { EmptyStateComponent } from '../../shared/components/empty-state.component';

/**
 * Lists every opportunity issued by the active establishment. Active
 * establishment is sourced from the profile service — if the caller
 * has none, the page surfaces an empty state nudging them to create
 * one first.
 */
@Component({
  selector: 'app-opportunity-list',
  standalone: true,
  imports: [CommonModule, RouterLink, LoadingComponent, EmptyStateComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <h1>Opportunities</h1>
        <p class="muted">Issued by {{ activeName() ?? '—' }}.</p>
        <a class="btn" [routerLink]="['/opportunities/new']">New opportunity</a>
      </header>

      <app-empty-state
        *ngIf="!activeEstablishmentId()"
        heading="No active establishment"
        message="You need to be a member of an Approved establishment to issue opportunities."
      />

      <app-loading *ngIf="activeEstablishmentId() && loading()" />

      <ng-container *ngIf="activeEstablishmentId() && !loading()">
        <app-empty-state
          *ngIf="!rows().length"
          heading="No opportunities"
          message="Click ‘New opportunity’ to create one."
        />
        <table class="table" *ngIf="rows().length">
          <thead>
            <tr>
              <th>Name</th>
              <th>Status</th>
              <th>Start</th>
              <th>End</th>
              <th>Applicants</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let o of rows()">
              <td>{{ o.name }}</td>
              <td><span class="badge">{{ o.status }}</span></td>
              <td>{{ o.start_date }}</td>
              <td>{{ o.end_date }}</td>
              <td>{{ o.applicants_count }}</td>
              <td>
                <a class="btn btn-ghost" [routerLink]="['/opportunities', o.id]">Open</a>
              </td>
            </tr>
          </tbody>
        </table>
      </ng-container>
    </div>
  `,
})
export class OpportunityListComponent implements OnInit {
  private readonly service = inject(OpportunityService);
  private readonly profile = inject(ProfileService);

  protected readonly activeEstablishmentId = this.profile.activeEstablishmentId;
  protected readonly activeName = computed(
    () => this.profile.establishments().find((e) => e.id === this.activeEstablishmentId())?.name ?? null,
  );

  protected readonly loading = signal(true);
  protected readonly rows = signal<Opportunity[]>([]);

  constructor() {
    // Reload when the active establishment changes.
    effect(() => {
      const id = this.activeEstablishmentId();
      if (id) this.reload(id);
    });
  }

  ngOnInit(): void {
    // Effect above handles the initial load once profile resolves.
  }

  private reload(establishmentId: string): void {
    this.loading.set(true);
    this.service.list(establishmentId).subscribe({
      next: (rows) => {
        this.rows.set(rows);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}

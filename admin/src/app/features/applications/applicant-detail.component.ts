import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { OpportunityService } from '../../core/services/opportunity.service';
import { ProfileService } from '../../core/services/profile.service';
import { OpportunityApplication } from '../../core/models/application';
import { LoadingComponent } from '../../shared/components/loading.component';

/**
 * Single applicant page. Surfaces the applicant + opportunity snapshot
 * the backend returns, plus a "Send offer" shortcut that pre-fills the
 * application id into the offer form.
 */
@Component({
  selector: 'app-applicant-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, LoadingComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <a class="muted" [routerLink]="['/opportunities']">← Opportunities</a>
        <h1>Applicant</h1>
      </header>

      <app-loading *ngIf="loading()" />

      <ng-container *ngIf="!loading() && application() as a">
        <section class="card">
          <h2>{{ a.applier?.name || a.applier?.email || a.applier?.id }}</h2>
          <dl class="kv">
            <div><dt>Type</dt><dd>{{ a.applier_type }}</dd></div>
            <div><dt>Email</dt><dd>{{ a.applier?.email ?? '—' }}</dd></div>
            <div><dt>Status</dt><dd><span class="badge">{{ a.status }}</span></dd></div>
            <div><dt>Applied</dt><dd>{{ a.created_at }}</dd></div>
            <div *ngIf="a.applied_by"><dt>Applied by</dt><dd>{{ a.applied_by?.name ?? a.applied_by?.email }}</dd></div>
          </dl>
        </section>

        <section class="card" *ngIf="a.opportunity as o">
          <h2>Opportunity</h2>
          <p><strong>{{ o.name }}</strong></p>
          <p>{{ o.start_date }} → {{ o.end_date }}</p>
          <p class="muted">{{ o.location_title }}</p>
          <a class="btn btn-ghost" [routerLink]="['/opportunities', o.id]">Open opportunity</a>
        </section>

        <section class="card actions">
          <a
            class="btn"
            [routerLink]="['/offers/new']"
            [queryParams]="{ application_id: a.id }"
          >Send offer</a>
        </section>
      </ng-container>
    </div>
  `,
  styles: [
    `
      .actions { display: flex; gap: 8px; }
      .kv { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px 24px; margin: 0; }
      .kv div { display: flex; flex-direction: column; }
      .kv dt { color: #6b7280; font-size: 12px; text-transform: uppercase; letter-spacing: 0.04em; }
      .kv dd { margin: 0; font-weight: 500; }
    `,
  ],
})
export class ApplicantDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(OpportunityService);
  private readonly profile = inject(ProfileService);

  protected readonly loading = signal(true);
  protected readonly application = signal<OpportunityApplication | null>(null);
  protected readonly applicantId = computed(
    () => this.route.snapshot.paramMap.get('applicantId') ?? '',
  );

  ngOnInit(): void {
    const eid = this.profile.activeEstablishmentId();
    const aid = this.applicantId();
    if (!eid || !aid) {
      this.loading.set(false);
      return;
    }
    this.service.getApplicant<OpportunityApplication>(eid, aid).subscribe({
      next: (a) => {
        this.application.set(a);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}

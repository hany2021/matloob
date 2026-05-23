import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { OpportunityService } from '../../core/services/opportunity.service';
import { Opportunity } from '../../core/models/opportunity';
import { ProfileService } from '../../core/services/profile.service';
import { AssetUploadComponent } from '../../shared/components/asset-upload.component';
import { LoadingComponent } from '../../shared/components/loading.component';
import { ConfirmDialogService } from '../../shared/components/confirm-dialog.service';
import { ToastService } from '../../shared/components/toast.service';
import { ApiErrorService } from '../../core/http/api-error.service';
import { UploadAssetResponse } from '../../core/models/asset';

/**
 * Detail page for a single opportunity. Edit / end / delete actions
 * gate on the `can_end` flag and the opportunity status. Asset uploads
 * flow through the reusable AssetUploadComponent → the resulting
 * asset id is linked back to the opportunity via
 * `POST /opportunities/{id}/assets`.
 */
@Component({
  selector: 'app-opportunity-detail',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    AssetUploadComponent,
    LoadingComponent,
  ],
  template: `
    <div class="page">
      <header class="page-header">
        <a class="muted" [routerLink]="['/opportunities']">← Opportunities</a>
        <h1>{{ opportunity()?.name ?? 'Opportunity' }}</h1>
        <p class="muted" *ngIf="opportunity() as o">
          <span class="badge">{{ o.status }}</span>
          <span> · {{ o.start_date }} → {{ o.end_date }}</span>
        </p>
      </header>

      <app-loading *ngIf="loading()" />

      <ng-container *ngIf="!loading() && opportunity() as o">
        <section class="card actions">
          <a class="btn btn-secondary" [routerLink]="['/opportunities', o.id, 'edit']">Edit</a>
          <a class="btn btn-secondary" [routerLink]="['/opportunities', o.id, 'applicants']">
            Applicants ({{ o.applicants_count }})
          </a>
          <button class="btn" *ngIf="o.can_end" (click)="onEnd()" [disabled]="busy()">End</button>
          <button class="btn btn-danger" (click)="onDelete()" [disabled]="busy()">Delete</button>
        </section>

        <section class="card">
          <h2>Description</h2>
          <p>{{ o.description }}</p>
        </section>

        <section class="card">
          <h2>Details</h2>
          <dl class="kv">
            <div><dt>Location</dt><dd>{{ o.location_title }}</dd></div>
            <div><dt>Required personnel</dt><dd>{{ o.required_personnel }}</dd></div>
            <div><dt>Monthly salary</dt><dd>{{ o.monthly_salary ?? '—' }}</dd></div>
            <div><dt>Years of experience</dt><dd>{{ o.years_of_experience_required ?? '—' }}</dd></div>
            <div><dt>Working hours</dt><dd>{{ o.working_hours_from ?? '—' }} → {{ o.working_hours_to ?? '—' }}</dd></div>
            <div><dt>Gender</dt><dd>{{ o.gender_label || (o.gender || []).join(', ') || '—' }}</dd></div>
            <div><dt>Category</dt><dd>{{ o.opportunity_category?.title ?? '—' }}</dd></div>
            <div><dt>Fees</dt><dd>{{ o.fees ?? '—' }}</dd></div>
            <div><dt>Phone</dt><dd>{{ o.phone_contact_information ?? '—' }}</dd></div>
            <div><dt>Email</dt><dd>{{ o.email_contact_information ?? '—' }}</dd></div>
          </dl>
        </section>

        <section class="card">
          <h2>Uploads</h2>
          <table class="table" *ngIf="o.uploads.length; else noUploads">
            <thead><tr><th>File</th><th>Type</th><th>Size</th></tr></thead>
            <tbody>
              <tr *ngFor="let u of o.uploads">
                <td>{{ u.file_name }}</td>
                <td>{{ u.content_type }}</td>
                <td>{{ formatSize(u.size_bytes) }}</td>
              </tr>
            </tbody>
          </table>
          <ng-template #noUploads>
            <p class="muted">No files uploaded.</p>
          </ng-template>
          <app-asset-upload
            purpose="Generic"
            pickerLabel="Add file"
            (uploadedAsset)="onAssetUploaded($event)"
          />
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
export class OpportunityDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(OpportunityService);
  private readonly profile = inject(ProfileService);
  private readonly dialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly apiError = inject(ApiErrorService);

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly opportunity = signal<Opportunity | null>(null);
  protected readonly id = computed(() => this.route.snapshot.paramMap.get('id') ?? '');

  ngOnInit(): void {
    this.reload();
  }

  private reload(): void {
    const eid = this.profile.activeEstablishmentId();
    const id = this.id();
    if (!eid || !id) {
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.service.get(eid, id).subscribe({
      next: (o) => {
        this.opportunity.set(o);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  async onEnd(): Promise<void> {
    const eid = this.profile.activeEstablishmentId();
    if (!eid) return;
    const ok = await this.dialog.confirm({
      title: 'End this opportunity?',
      message: 'Applicants will no longer be able to apply.',
      confirmLabel: 'End',
    });
    if (!ok) return;
    this.busy.set(true);
    this.service.end(eid, this.id()).subscribe({
      next: (o) => {
        this.opportunity.set(o);
        this.toast.success('Opportunity ended.');
        this.busy.set(false);
      },
      error: (err) => {
        this.apiError.notify(err, 'End opportunity failed');
        this.busy.set(false);
      },
    });
  }

  async onDelete(): Promise<void> {
    const eid = this.profile.activeEstablishmentId();
    if (!eid) return;
    const ok = await this.dialog.confirm({
      title: 'Delete this opportunity?',
      message: 'This action cannot be undone.',
      kind: 'danger',
      confirmLabel: 'Delete',
    });
    if (!ok) return;
    this.busy.set(true);
    this.service.delete(eid, this.id()).subscribe({
      next: () => {
        this.toast.success('Opportunity deleted.');
        this.router.navigate(['/opportunities']);
      },
      error: (err) => {
        this.apiError.notify(err, 'Delete failed');
        this.busy.set(false);
      },
    });
  }

  onAssetUploaded(asset: UploadAssetResponse): void {
    const eid = this.profile.activeEstablishmentId();
    if (!eid) return;
    this.service.linkAsset(eid, this.id(), { asset_id: asset.id }).subscribe({
      next: () => {
        this.toast.success('File attached.');
        this.reload();
      },
      error: (err) => this.apiError.notify(err, 'Attach file failed'),
    });
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }
}

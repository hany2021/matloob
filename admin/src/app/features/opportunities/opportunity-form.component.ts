import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { OpportunityService } from '../../core/services/opportunity.service';
import { ProfileService } from '../../core/services/profile.service';
import { CreateOpportunityRequest, UpdateOpportunityRequest } from '../../core/models/opportunity';
import { ToastService } from '../../shared/components/toast.service';
import { LoadingComponent } from '../../shared/components/loading.component';
import { ApiErrorService } from '../../core/http/api-error.service';

/**
 * Create / edit opportunity form. Hosted at both `/opportunities/new`
 * (create) and `/opportunities/:id/edit` (update). The form covers the
 * single-create path only — the bulk shape (event-scoped array) is
 * backend-only and not exposed here.
 *
 * Event linkage isn't migrated to the new system yet (no Events slice
 * surfaced in the admin), so creates without an event_id fall back to
 * whatever the backend defaults to. Reference data (categories,
 * nationalities) likewise will be a typeahead later; for now the form
 * accepts raw GUIDs.
 */
@Component({
  selector: 'app-opportunity-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink, LoadingComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <a class="muted" [routerLink]="['/opportunities']">← Opportunities</a>
        <h1>{{ id() ? 'Edit opportunity' : 'New opportunity' }}</h1>
      </header>

      <app-loading *ngIf="loading()" />

      <form
        *ngIf="!loading()"
        [formGroup]="form"
        (ngSubmit)="onSubmit()"
        class="card form"
      >
        <div class="form-field">
          <label>Name</label>
          <input formControlName="name" maxlength="120" />
        </div>
        <div class="form-field">
          <label>Description</label>
          <textarea formControlName="description" rows="6" minlength="10" maxlength="3000"></textarea>
        </div>
        <div class="grid">
          <div class="form-field">
            <label>Start date</label>
            <input formControlName="start_date" type="date" />
          </div>
          <div class="form-field">
            <label>End date</label>
            <input formControlName="end_date" type="date" />
          </div>
          <div class="form-field">
            <label>Location title</label>
            <input formControlName="location_title" maxlength="200" />
          </div>
          <div class="form-field">
            <label>Required personnel</label>
            <input formControlName="required_personnel" type="number" min="1" />
          </div>
          <div class="form-field">
            <label>Latitude</label>
            <input formControlName="lat" type="number" step="0.000001" />
          </div>
          <div class="form-field">
            <label>Longitude</label>
            <input formControlName="lon" type="number" step="0.000001" />
          </div>
          <div class="form-field">
            <label>Monthly salary (optional)</label>
            <input formControlName="monthly_salary" type="number" min="0" />
          </div>
          <div class="form-field">
            <label>Years of experience (optional)</label>
            <input formControlName="years_of_experience_required" type="number" min="0" max="100" />
          </div>
          <div class="form-field">
            <label>Category ID</label>
            <input formControlName="opportunity_category_id" />
          </div>
          <div class="form-field" *ngIf="!id()">
            <label>Event ID (optional)</label>
            <input formControlName="event_id" />
          </div>
          <div class="form-field">
            <label>Phone contact (optional)</label>
            <input formControlName="phone_contact_information" />
          </div>
          <div class="form-field">
            <label>Email contact (optional)</label>
            <input formControlName="email_contact_information" type="email" />
          </div>
        </div>

        <div class="actions">
          <button class="btn" type="submit" [disabled]="busy() || !form.valid">
            {{ id() ? 'Save changes' : 'Create' }}
          </button>
          <a class="btn btn-ghost" [routerLink]="['/opportunities']">Cancel</a>
        </div>
      </form>
    </div>
  `,
  styles: [
    `
      .form { display: flex; flex-direction: column; gap: 16px; }
      .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 16px; }
      .actions { display: flex; gap: 8px; }
    `,
  ],
})
export class OpportunityFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(OpportunityService);
  private readonly profile = inject(ProfileService);
  private readonly toast = inject(ToastService);
  private readonly apiError = inject(ApiErrorService);

  protected readonly id = computed(() => this.route.snapshot.paramMap.get('id'));
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);

  protected readonly form: FormGroup = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
    description: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(3000)]],
    start_date: ['', Validators.required],
    end_date: ['', Validators.required],
    location_title: ['', [Validators.required, Validators.maxLength(200)]],
    required_personnel: [1, [Validators.required, Validators.min(1)]],
    lat: [0, [Validators.required, Validators.min(-90), Validators.max(90)]],
    lon: [0, [Validators.required, Validators.min(-180), Validators.max(180)]],
    monthly_salary: [null as number | null],
    years_of_experience_required: [null as number | null],
    opportunity_category_id: ['', Validators.required],
    event_id: [''],
    phone_contact_information: [''],
    email_contact_information: [''],
  });

  ngOnInit(): void {
    const id = this.id();
    if (id) this.loadExisting(id);
  }

  private loadExisting(id: string): void {
    const eid = this.profile.activeEstablishmentId();
    if (!eid) return;
    this.loading.set(true);
    this.service.get(eid, id).subscribe({
      next: (o) => {
        this.form.patchValue({
          name: o.name,
          description: o.description,
          start_date: o.start_date,
          end_date: o.end_date,
          location_title: o.location_title,
          required_personnel: o.required_personnel,
          lat: o.lat,
          lon: o.lon,
          monthly_salary: o.monthly_salary,
          years_of_experience_required: o.years_of_experience_required,
          opportunity_category_id: o.opportunity_category?.id ?? '',
          phone_contact_information: o.phone_contact_information ?? '',
          email_contact_information: o.email_contact_information ?? '',
        });
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  onSubmit(): void {
    const eid = this.profile.activeEstablishmentId();
    if (!eid) {
      this.toast.warning('No active establishment.');
      return;
    }
    const v = this.form.value;
    this.busy.set(true);
    if (this.id()) {
      const update: UpdateOpportunityRequest = {
        name: v.name,
        description: v.description,
        start_date: v.start_date,
        end_date: v.end_date,
        location_title: v.location_title,
        required_personnel: v.required_personnel,
        lat: v.lat,
        lon: v.lon,
        monthly_salary: v.monthly_salary,
        years_of_experience_required: v.years_of_experience_required,
        opportunity_category_id: v.opportunity_category_id,
        phone_contact_information: v.phone_contact_information || null,
        email_contact_information: v.email_contact_information || null,
      };
      this.service.update(eid, this.id()!, update).subscribe({
        next: (o) => {
          this.toast.success('Saved.');
          this.busy.set(false);
          this.router.navigate(['/opportunities', o.id]);
        },
        error: (err) => {
          this.apiError.notify(err, 'Save failed');
          this.busy.set(false);
        },
      });
    } else {
      const create: CreateOpportunityRequest = {
        event_id: v.event_id || undefined,
        opportunity_category_id: v.opportunity_category_id,
        name: v.name,
        description: v.description,
        start_date: v.start_date,
        end_date: v.end_date,
        location_title: v.location_title,
        lat: v.lat,
        lon: v.lon,
        required_personnel: v.required_personnel,
        monthly_salary: v.monthly_salary,
        years_of_experience_required: v.years_of_experience_required,
        phone_contact_information: v.phone_contact_information || null,
        email_contact_information: v.email_contact_information || null,
      };
      this.service.create(eid, create).subscribe({
        next: (o) => {
          this.toast.success('Opportunity created.');
          this.busy.set(false);
          this.router.navigate(['/opportunities', o.id]);
        },
        error: (err) => {
          this.apiError.notify(err, 'Create failed');
          this.busy.set(false);
        },
      });
    }
  }
}

import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { OfferService } from '../../core/services/offer.service';
import { ProfileService } from '../../core/services/profile.service';
import { ToastService } from '../../shared/components/toast.service';
import { SendOfferRequest } from '../../core/models/offer';
import { ApiErrorService } from '../../core/http/api-error.service';

/**
 * Send-offer form. The applicant id is typically pre-filled by the
 * applicant-detail page; otherwise the user pastes it in. Reference
 * data (job titles, sponsors) isn't surfaced as a typeahead yet — the
 * fields accept raw GUIDs.
 */
@Component({
  selector: 'app-send-offer',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <div class="page">
      <header class="page-header">
        <a class="muted" [routerLink]="['/offers']">← Offers</a>
        <h1>Send offer</h1>
      </header>

      <form [formGroup]="form" (ngSubmit)="onSubmit()" class="card form">
        <div class="form-field">
          <label>Applicant ID</label>
          <input formControlName="applicant_id" />
        </div>
        <div class="form-field">
          <label>Job title ID (optional)</label>
          <input formControlName="job_title_id" />
        </div>
        <div class="form-field">
          <label>Job title category ID (optional)</label>
          <input formControlName="job_title_category_id" />
        </div>
        <div class="form-field">
          <label>Sponsor ID (optional)</label>
          <input formControlName="sponsor_id" />
        </div>
        <div class="grid">
          <div class="form-field">
            <label>Monthly salary</label>
            <input formControlName="monthly_salary" type="number" min="0" />
          </div>
          <div class="form-field">
            <label>Daily wage</label>
            <input formControlName="daily_wage" type="number" min="0" />
          </div>
          <div class="form-field">
            <label>Working days</label>
            <input formControlName="number_of_working_days" type="number" min="0" />
          </div>
          <div class="form-field">
            <label>Currency</label>
            <input formControlName="currency" />
          </div>
          <div class="form-field">
            <label>Validity from</label>
            <input formControlName="offer_validity_from" type="date" />
          </div>
          <div class="form-field">
            <label>Validity to</label>
            <input formControlName="offer_validity_to" type="date" />
          </div>
          <div class="form-field">
            <label>Start date</label>
            <input formControlName="start_date" type="date" />
          </div>
          <div class="form-field">
            <label>End date</label>
            <input formControlName="end_date" type="date" />
          </div>
        </div>
        <div class="form-field">
          <label>Laborer commitments</label>
          <textarea formControlName="laborer_commitments" rows="3"></textarea>
        </div>

        <div class="actions">
          <button class="btn" type="submit" [disabled]="busy() || !form.valid">Send offer</button>
          <a class="btn btn-ghost" [routerLink]="['/offers']">Cancel</a>
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
export class SendOfferComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(OfferService);
  private readonly profile = inject(ProfileService);
  private readonly toast = inject(ToastService);
  private readonly apiError = inject(ApiErrorService);

  protected readonly busy = signal(false);

  protected readonly form: FormGroup = this.fb.group({
    applicant_id: ['', Validators.required],
    job_title_id: [''],
    job_title_category_id: [''],
    sponsor_id: [''],
    monthly_salary: [null as number | null],
    daily_wage: [null as number | null],
    number_of_working_days: [null as number | null],
    currency: ['SAR'],
    offer_validity_from: [''],
    offer_validity_to: [''],
    start_date: [''],
    end_date: [''],
    laborer_commitments: [''],
  });

  ngOnInit(): void {
    const presetApplicant = this.route.snapshot.queryParamMap.get('application_id');
    if (presetApplicant) this.form.patchValue({ applicant_id: presetApplicant });
  }

  onSubmit(): void {
    const eid = this.profile.activeEstablishmentId();
    if (!eid) {
      this.toast.warning('No active establishment.');
      return;
    }
    const v = this.form.value;
    const body: SendOfferRequest = {
      applicant_id: v.applicant_id,
      job_title_id: v.job_title_id || undefined,
      job_title_category_id: v.job_title_category_id || undefined,
      sponsor_id: v.sponsor_id || undefined,
      monthly_salary: v.monthly_salary ?? undefined,
      daily_wage: v.daily_wage ?? undefined,
      number_of_working_days: v.number_of_working_days ?? undefined,
      currency: v.currency || undefined,
      offer_validity_from: v.offer_validity_from || undefined,
      offer_validity_to: v.offer_validity_to || undefined,
      start_date: v.start_date || undefined,
      end_date: v.end_date || undefined,
      laborer_commitments: v.laborer_commitments || undefined,
    };
    this.busy.set(true);
    this.service.send(eid, body).subscribe({
      next: (o) => {
        this.toast.success('Offer sent.');
        this.busy.set(false);
        this.router.navigate(['/offers', o.id]);
      },
      error: (err) => {
        this.apiError.notify(err, 'Send offer failed');
        this.busy.set(false);
      },
    });
  }
}

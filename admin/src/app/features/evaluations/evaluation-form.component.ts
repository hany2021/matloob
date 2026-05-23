import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { EvaluationService } from '../../core/services/evaluation.service';
import { ProfileService } from '../../core/services/profile.service';
import { ToastService } from '../../shared/components/toast.service';
import { CreateEvaluationRequest } from '../../core/models/evaluation';
import { ApiErrorService } from '../../core/http/api-error.service';

/**
 * Create-evaluation form. Hooked from the "Left to evaluate" panel on
 * the evaluation list — that page pre-fills `offer_id` via a query
 * param. Evaluations attach to `offer_id` only (Q-EVAL-1; no
 * contract_id).
 */
@Component({
  selector: 'app-evaluation-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <div class="page">
      <header class="page-header">
        <a class="muted" [routerLink]="['/evaluations']">← Evaluations</a>
        <h1>New evaluation</h1>
      </header>

      <form [formGroup]="form" (ngSubmit)="onSubmit()" class="card form">
        <div class="form-field">
          <label>Offer ID</label>
          <input formControlName="offer_id" />
        </div>
        <div class="form-field">
          <label>Rating (1-5)</label>
          <input formControlName="rating" type="number" min="1" max="5" />
        </div>
        <div class="form-field">
          <label>
            <input formControlName="recommend_for_future_opportunities" type="checkbox" />
            Recommend for future opportunities
          </label>
        </div>
        <div class="form-field">
          <label>Comment</label>
          <textarea formControlName="comment" rows="4" maxlength="500"></textarea>
        </div>
        <div class="form-field">
          <label>Matching percentage (0-100)</label>
          <input formControlName="matching_percentage" type="number" min="0" max="100" />
        </div>
        <div class="form-field">
          <label>Success management criteria comment</label>
          <textarea formControlName="success_management_criteria_comment" rows="3" maxlength="500"></textarea>
        </div>

        <div class="actions">
          <button class="btn" type="submit" [disabled]="busy() || !form.valid">Submit</button>
          <a class="btn btn-ghost" [routerLink]="['/evaluations']">Cancel</a>
        </div>
      </form>
    </div>
  `,
  styles: [`.form { display: flex; flex-direction: column; gap: 16px; } .actions { display: flex; gap: 8px; }`],
})
export class EvaluationFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(EvaluationService);
  private readonly profile = inject(ProfileService);
  private readonly toast = inject(ToastService);
  private readonly apiError = inject(ApiErrorService);

  protected readonly busy = signal(false);

  protected readonly form: FormGroup = this.fb.group({
    offer_id: ['', Validators.required],
    rating: [5, [Validators.required, Validators.min(1), Validators.max(5)]],
    recommend_for_future_opportunities: [true],
    comment: [''],
    matching_percentage: [null as number | null],
    success_management_criteria_comment: [''],
  });

  ngOnInit(): void {
    const preset = this.route.snapshot.queryParamMap.get('offer_id');
    if (preset) this.form.patchValue({ offer_id: preset });
  }

  onSubmit(): void {
    const eid = this.profile.activeEstablishmentId();
    if (!eid) {
      this.toast.warning('No active establishment.');
      return;
    }
    const v = this.form.value;
    const body: CreateEvaluationRequest = {
      offer_id: v.offer_id,
      rating: v.rating,
      recommend_for_future_opportunities: v.recommend_for_future_opportunities,
      comment: v.comment || null,
      matching_percentage: v.matching_percentage ?? null,
      success_management_criteria_comment: v.success_management_criteria_comment || null,
    };
    this.busy.set(true);
    this.service.create(eid, body).subscribe({
      next: () => {
        this.toast.success('Evaluation submitted.');
        this.busy.set(false);
        this.router.navigate(['/evaluations']);
      },
      error: (err) => {
        this.apiError.notify(err, 'Submit failed');
        this.busy.set(false);
      },
    });
  }
}

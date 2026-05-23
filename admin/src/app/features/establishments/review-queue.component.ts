import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { EstablishmentService } from '../../core/services/establishment.service';
import { PendingReviewItem } from '../../core/models/admin-establishment';
import { LoadingComponent } from '../../shared/components/loading.component';
import { EmptyStateComponent } from '../../shared/components/empty-state.component';

/**
 * Admin queue of establishments in PendingReview status. Oldest first
 * so the longest-waiting submission floats to the top.
 */
@Component({
  selector: 'app-review-queue',
  standalone: true,
  imports: [CommonModule, RouterLink, LoadingComponent, EmptyStateComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <h1>Review queue</h1>
        <p class="muted">Establishments waiting on admin approval.</p>
      </header>

      <app-loading *ngIf="loading()" />

      <ng-container *ngIf="!loading()">
        <app-empty-state
          *ngIf="!rows().length"
          heading="No establishments awaiting review"
          message="New submissions will appear here, oldest first."
        />
        <table class="table" *ngIf="rows().length">
          <thead>
            <tr>
              <th>Name</th>
              <th>CR number</th>
              <th>City</th>
              <th>Submitted</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let r of rows()">
              <td>{{ r.name }}</td>
              <td>{{ r.commercialRegistrationNumber }}</td>
              <td>{{ r.city }}</td>
              <td>{{ r.submittedAt | date: 'medium' }}</td>
              <td>
                <a class="btn btn-ghost" [routerLink]="['/admin/review-queue', r.id]">Review</a>
              </td>
            </tr>
          </tbody>
        </table>
        <div class="muted" *ngIf="total() > pageSize()">
          {{ rows().length }} of {{ total() }} shown.
        </div>
      </ng-container>
    </div>
  `,
})
export class ReviewQueueComponent implements OnInit {
  private readonly service = inject(EstablishmentService);

  protected readonly loading = signal(true);
  protected readonly rows = signal<PendingReviewItem[]>([]);
  protected readonly total = signal(0);
  protected readonly pageSize = signal(20);

  ngOnInit(): void {
    this.service.listPendingReview(1, this.pageSize()).subscribe({
      next: (resp) => {
        this.rows.set(resp.items);
        this.total.set(resp.total);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}

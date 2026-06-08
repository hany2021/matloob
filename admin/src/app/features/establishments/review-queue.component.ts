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
        <h1>طلبات المراجعة</h1>
        <p class="muted">المنشآت بانتظار موافقة المشرف.</p>
      </header>

      <app-loading *ngIf="loading()" />

      <ng-container *ngIf="!loading()">
        <app-empty-state
          *ngIf="!rows().length"
          heading="لا توجد منشآت بانتظار المراجعة"
          message="ستظهر الطلبات الجديدة هنا، الأقدم أولاً."
        />
        <table class="table" *ngIf="rows().length">
          <thead>
            <tr>
              <th>الاسم</th>
              <th>رقم السجل التجاري</th>
              <th>المدينة</th>
              <th>تاريخ التقديم</th>
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
                <a class="btn btn-ghost" [routerLink]="['/admin/review-queue', r.id]">مراجعة</a>
              </td>
            </tr>
          </tbody>
        </table>
        <div class="muted" *ngIf="total() > pageSize()">
          عرض {{ rows().length }} من {{ total() }}.
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

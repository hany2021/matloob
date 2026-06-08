import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';

import { AdminEstablishmentListService } from '../../core/services/admin-establishment-list.service';
import {
  AdminEstablishmentListItem,
  EstablishmentRoleFilter,
} from '../../core/models/admin-establishment';
import { LoadingComponent } from '../../shared/components/loading.component';
import { EmptyStateComponent } from '../../shared/components/empty-state.component';

/**
 * Establishments list — the .NET-backed replacement for the legacy Filament
 * "EstablishmentResource" list. One component drives both the organizers and
 * operators screens; the `role` filter (and the screen title) come from the
 * route `data`. Reads `/api/v1/admin/establishments?role=...`. View-only.
 */
@Component({
  selector: 'app-establishments-list',
  standalone: true,
  imports: [CommonModule, FormsModule, LoadingComponent, EmptyStateComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ title }}</h1>
          <p class="muted">{{ subtitle }}</p>
        </div>
      </header>

      <div class="toolbar">
        <input
          type="search"
          [(ngModel)]="search"
          (keyup.enter)="reload()"
          placeholder="بحث بالاسم أو السجل التجاري أو المدينة"
        />
        <button class="btn btn-secondary" (click)="reload()">بحث</button>
      </div>

      <app-loading *ngIf="loading()" />

      <ng-container *ngIf="!loading()">
        <app-empty-state *ngIf="!rows().length" [heading]="emptyHeading" />
        <div class="table-wrap" *ngIf="rows().length">
          <table class="table">
            <thead>
              <tr>
                <th>الاسم</th>
                <th>المدينة</th>
                <th>النشاط الاقتصادي</th>
                <th>السجل التجاري</th>
                <th>انتهاء السجل</th>
                <th>الفرص</th>
                <th>العقود</th>
                <th>الحالة</th>
                <th>تاريخ التسجيل</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let r of rows()">
                <td>{{ r.name }}</td>
                <td>{{ r.city || 'غير محدد' }}</td>
                <td>{{ r.economicActivity || 'غير محدد' }}</td>
                <td>{{ r.crNumber || '—' }}</td>
                <td>{{ r.crExpiry || '—' }}</td>
                <td>{{ r.opportunitiesCount }}</td>
                <td>{{ r.contractsCount }}</td>
                <td>
                  <span class="badge" [class.badge-success]="r.status === 'Approved'">
                    {{ statusLabel(r.status) }}
                  </span>
                </td>
                <td>{{ r.createdAt }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div class="muted" *ngIf="total() > rows().length">
          عرض {{ rows().length }} من {{ total() }}.
        </div>
      </ng-container>
    </div>
  `,
  styles: [
    `
      .toolbar { display: flex; gap: 8px; margin-bottom: 16px; max-width: 480px; }
      .toolbar input { flex: 1; padding: 8px 12px; border: 1px solid var(--color-border); border-radius: 4px; font-family: inherit; }
      .table-wrap { overflow-x: auto; }
    `,
  ],
})
export class EstablishmentsListComponent implements OnInit {
  private readonly service = inject(AdminEstablishmentListService);
  private readonly route = inject(ActivatedRoute);

  protected role: EstablishmentRoleFilter = 'organizer';
  protected title = 'المنشآت';
  protected subtitle = '';
  protected emptyHeading = 'لا توجد منشآت';

  protected search = '';
  protected readonly loading = signal(true);
  protected readonly rows = signal<AdminEstablishmentListItem[]>([]);
  protected readonly total = signal(0);

  ngOnInit(): void {
    const data = this.route.snapshot.data;
    this.role = (data['role'] as EstablishmentRoleFilter) ?? 'organizer';
    this.title = (data['title'] as string) ?? this.title;
    this.subtitle = (data['subtitle'] as string) ?? '';
    this.emptyHeading = (data['emptyHeading'] as string) ?? this.emptyHeading;
    this.reload();
  }

  protected statusLabel(status: string): string {
    switch (status) {
      case 'Draft':
        return 'مسودة';
      case 'PendingReview':
        return 'بانتظار المراجعة';
      case 'Approved':
        return 'معتمدة';
      case 'Rejected':
        return 'مرفوضة';
      case 'Suspended':
        return 'موقوفة';
      default:
        return status;
    }
  }

  protected reload(): void {
    this.loading.set(true);
    this.service.list(this.role, this.search.trim() || undefined).subscribe({
      next: (resp) => {
        this.rows.set(resp.items);
        this.total.set(resp.total);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}

import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { IndividualService } from '../../core/services/individual.service';
import { IndividualListItem } from '../../core/models/individual';
import { LoadingComponent } from '../../shared/components/loading.component';
import { EmptyStateComponent } from '../../shared/components/empty-state.component';

/**
 * Individual user accounts list — the .NET-backed replacement for the legacy
 * Filament "UserResource" list (view-only). Reads
 * `/api/v1/admin/individuals`.
 */
@Component({
  selector: 'app-individuals-list',
  standalone: true,
  imports: [CommonModule, FormsModule, LoadingComponent, EmptyStateComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>الأفراد</h1>
          <p class="muted">حسابات الأفراد المسجّلين في منصة مطلوب.</p>
        </div>
      </header>

      <div class="toolbar">
        <input
          type="search"
          [(ngModel)]="search"
          (keyup.enter)="reload()"
          placeholder="بحث بالاسم أو البريد أو رقم الهوية"
        />
        <button class="btn btn-secondary" (click)="reload()">بحث</button>
      </div>

      <app-loading *ngIf="loading()" />

      <ng-container *ngIf="!loading()">
        <app-empty-state *ngIf="!rows().length" heading="لا يوجد أفراد" />
        <div class="table-wrap" *ngIf="rows().length">
          <table class="table">
            <thead>
              <tr>
                <th>الاسم</th>
                <th>البريد الإلكتروني</th>
                <th>الجوال</th>
                <th>رقم الهوية</th>
                <th>الجنس</th>
                <th>العمر</th>
                <th>المدينة</th>
                <th>الجنسية</th>
                <th>سنوات الخبرة</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let r of rows()">
                <td>{{ r.name || '—' }}</td>
                <td>{{ r.email || '—' }}</td>
                <td>{{ r.phone || '—' }}</td>
                <td>{{ r.idNumber || '—' }}</td>
                <td>{{ r.gender || 'غير محدد' }}</td>
                <td>{{ r.age ?? 'غير محدد' }}</td>
                <td>{{ r.city || 'غير محدد' }}</td>
                <td>{{ r.nationality || 'غير محدد' }}</td>
                <td>{{ r.yearsOfExperience }}</td>
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
export class IndividualsListComponent implements OnInit {
  private readonly service = inject(IndividualService);

  protected search = '';
  protected readonly loading = signal(true);
  protected readonly rows = signal<IndividualListItem[]>([]);
  protected readonly total = signal(0);

  ngOnInit(): void {
    this.reload();
  }

  protected reload(): void {
    this.loading.set(true);
    this.service.list(this.search.trim() || undefined).subscribe({
      next: (resp) => {
        this.rows.set(resp.items);
        this.total.set(resp.total);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}

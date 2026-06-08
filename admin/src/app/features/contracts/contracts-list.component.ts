import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AdminContractService } from '../../core/services/admin-contract.service';
import { AdminContractListItem } from '../../core/models/admin-contract';
import { LoadingComponent } from '../../shared/components/loading.component';
import { EmptyStateComponent } from '../../shared/components/empty-state.component';

/**
 * Contracts list — in the Ajeer-stripped model an accepted offer IS the
 * contract, so this lists offers in a binding status. The .NET-backed
 * replacement for the legacy Filament contract resources (minus the dropped
 * document viewers). Reads `/api/v1/admin/contracts`. View-only.
 */
@Component({
  selector: 'app-contracts-list',
  standalone: true,
  imports: [CommonModule, FormsModule, LoadingComponent, EmptyStateComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>العقود</h1>
          <p class="muted">العروض المقبولة بين المنشآت والأفراد (العرض المقبول هو العقد).</p>
        </div>
      </header>

      <div class="toolbar">
        <input
          type="search"
          [(ngModel)]="search"
          (keyup.enter)="reload()"
          placeholder="بحث باسم المنشأة أو الفرصة"
        />
        <button class="btn btn-secondary" (click)="reload()">بحث</button>
      </div>

      <app-loading *ngIf="loading()" />

      <ng-container *ngIf="!loading()">
        <app-empty-state *ngIf="!rows().length" heading="لا توجد عقود" />
        <div class="table-wrap" *ngIf="rows().length">
          <table class="table">
            <thead>
              <tr>
                <th>المتقدم</th>
                <th>المنشأة</th>
                <th>الفرصة</th>
                <th>الحالة</th>
                <th>تاريخ القبول</th>
                <th>المدة</th>
                <th>الراتب الشهري</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let r of rows()">
                <td>
                  {{ r.applicantName || 'غير محدد' }}
                  <span class="sub" *ngIf="r.applicantType">
                    ({{ r.applicantType === 'establishment' ? 'منشأة' : 'فرد' }})
                  </span>
                </td>
                <td>{{ r.providerName || 'غير محدد' }}</td>
                <td>{{ r.opportunityName || 'غير محدد' }}</td>
                <td>
                  <span class="dot" [style.background]="r.statusColor"></span>
                  {{ r.statusLabel }}
                </td>
                <td>{{ r.acceptedAt || '—' }}</td>
                <td>{{ r.startDate || '—' }} → {{ r.endDate || '—' }}</td>
                <td>{{ r.monthlySalary != null ? (r.monthlySalary | number) + ' ر.س' : '—' }}</td>
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
      .sub { color: var(--color-text-muted, #888); font-size: 0.85em; }
      .dot { display: inline-block; width: 8px; height: 8px; border-radius: 50%; margin-inline-end: 6px; vertical-align: middle; }
    `,
  ],
})
export class ContractsListComponent implements OnInit {
  private readonly service = inject(AdminContractService);

  protected search = '';
  protected readonly loading = signal(true);
  protected readonly rows = signal<AdminContractListItem[]>([]);
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

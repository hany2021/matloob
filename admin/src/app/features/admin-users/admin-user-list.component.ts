import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { AdminUserService } from '../../core/services/admin-user.service';
import { AdminListItem } from '../../core/models/admin-user';
import { LoadingComponent } from '../../shared/components/loading.component';
import { EmptyStateComponent } from '../../shared/components/empty-state.component';
import { ConfirmDialogService } from '../../shared/components/confirm-dialog.service';
import { ToastService } from '../../shared/components/toast.service';
import { ApiErrorService } from '../../core/http/api-error.service';

/**
 * Admin-users list/search screen — the .NET-backed replacement for the legacy
 * Filament "المستخدمون" resource list.
 */
@Component({
  selector: 'app-admin-user-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, LoadingComponent, EmptyStateComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>المستخدمون</h1>
          <p class="muted">مستخدمو لوحة الإدارة المرتبطون بهوية IdM.</p>
        </div>
        <a class="btn" routerLink="/admin/admins/new">+ إضافة مستخدم</a>
      </header>

      <div class="toolbar">
        <input
          type="search"
          [(ngModel)]="search"
          (keyup.enter)="reload()"
          placeholder="بحث بالاسم أو البريد الإلكتروني"
        />
        <button class="btn btn-secondary" (click)="reload()">بحث</button>
      </div>

      <app-loading *ngIf="loading()" />

      <ng-container *ngIf="!loading()">
        <app-empty-state
          *ngIf="!rows().length"
          heading="لا يوجد مستخدمون"
          message="أضف أول مستخدم للوحة الإدارة عبر زر «إضافة مستخدم»."
        />
        <table class="table" *ngIf="rows().length">
          <thead>
            <tr>
              <th>الاسم</th>
              <th>البريد الإلكتروني</th>
              <th>الحالة</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let r of rows()">
              <td>{{ r.name }}</td>
              <td>{{ r.email }}</td>
              <td>
                <span class="badge" [class.badge-success]="r.isActive" [class.badge-danger]="!r.isActive">
                  {{ r.isActive ? 'نشط' : 'غير نشط' }}
                </span>
              </td>
              <td class="actions">
                <a class="btn btn-ghost" [routerLink]="['/admin/admins', r.id]">عرض / تعديل</a>
                <button class="btn btn-ghost" (click)="onDelete(r)" [disabled]="busy() === r.id">حذف</button>
              </td>
            </tr>
          </tbody>
        </table>
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
      .actions { display: flex; gap: 4px; justify-content: flex-end; }
    `,
  ],
})
export class AdminUserListComponent implements OnInit {
  private readonly service = inject(AdminUserService);
  private readonly dialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly apiError = inject(ApiErrorService);

  protected search = '';
  protected readonly loading = signal(true);
  protected readonly rows = signal<AdminListItem[]>([]);
  protected readonly total = signal(0);
  protected readonly busy = signal<string | null>(null);

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

  async onDelete(item: AdminListItem): Promise<void> {
    const ok = await this.dialog.confirm({
      title: `حذف المستخدم ${item.name}؟`,
      message: 'سيتم إلغاء تفعيل هويته في IdM ثم حذفه من القائمة.',
      kind: 'danger',
      confirmLabel: 'حذف',
    });
    if (!ok) return;
    this.busy.set(item.id);
    this.service.remove(item.id).subscribe({
      next: () => {
        this.toast.success('تم حذف المستخدم.');
        this.busy.set(null);
        this.rows.update((list) => list.filter((r) => r.id !== item.id));
        this.total.update((t) => Math.max(0, t - 1));
      },
      error: (err) => {
        this.apiError.notify(err, 'فشل الحذف');
        this.busy.set(null);
      },
    });
  }
}

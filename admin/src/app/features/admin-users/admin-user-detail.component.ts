import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { AdminUserService } from '../../core/services/admin-user.service';
import { AdminDetail } from '../../core/models/admin-user';
import { LoadingComponent } from '../../shared/components/loading.component';
import { ConfirmDialogService } from '../../shared/components/confirm-dialog.service';
import { ToastService } from '../../shared/components/toast.service';
import { ApiErrorService } from '../../core/http/api-error.service';

/**
 * View + edit a single admin user. Email is fixed (it keys the IdM identity);
 * only name + active are editable. Delete deactivates the IdM identity first.
 */
@Component({
  selector: 'app-admin-user-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, LoadingComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <a class="muted" routerLink="/admin/admins">→ المستخدمون</a>
        <h1>{{ detail()?.name ?? 'مستخدم' }}</h1>
      </header>

      <app-loading *ngIf="loading()" />

      <ng-container *ngIf="!loading() && detail() as d">
        <section class="card form">
          <label class="field">
            <span>البريد الإلكتروني</span>
            <input type="email" [value]="d.email" disabled />
            <small class="muted">لا يمكن تغيير البريد الإلكتروني (مرتبط بهوية IdM).</small>
          </label>

          <label class="field">
            <span>الاسم</span>
            <input type="text" [(ngModel)]="name" />
          </label>

          <label class="check">
            <input type="checkbox" [(ngModel)]="isActive" />
            <span>نشط</span>
          </label>

          <dl class="kv">
            <div><dt>معرّف IdM</dt><dd class="mono">{{ d.identityId ?? '—' }}</dd></div>
          </dl>

          <div class="row">
            <button class="btn" (click)="onSave()" [disabled]="busy() || !name.trim()">
              {{ busy() ? 'جارٍ الحفظ…' : 'حفظ' }}
            </button>
            <button class="btn btn-danger" (click)="onDelete()" [disabled]="busy()">حذف</button>
          </div>
        </section>
      </ng-container>
    </div>
  `,
  styles: [
    `
      .form { max-width: 560px; display: flex; flex-direction: column; gap: 16px; }
      .field { display: flex; flex-direction: column; gap: 6px; }
      .field > span { font-weight: 500; }
      .field input { padding: 8px 12px; border: 1px solid var(--color-border); border-radius: 4px; font-family: inherit; }
      .check { display: flex; align-items: center; gap: 8px; }
      .kv { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px 24px; margin: 0; }
      .kv div { display: flex; flex-direction: column; }
      .kv dt { color: #6b7280; font-size: 12px; }
      .kv dd { margin: 0; font-weight: 500; }
      .mono { font-family: ui-monospace, SFMono-Regular, monospace; word-break: break-all; }
      .row { display: flex; gap: 8px; }
    `,
  ],
})
export class AdminUserDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(AdminUserService);
  private readonly dialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly apiError = inject(ApiErrorService);

  protected name = '';
  protected isActive = true;
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly detail = signal<AdminDetail | null>(null);

  protected readonly id = computed(() => this.route.snapshot.paramMap.get('id') ?? '');

  ngOnInit(): void {
    const id = this.id();
    if (!id) {
      this.loading.set(false);
      return;
    }
    this.service.get(id).subscribe({
      next: (d) => {
        this.detail.set(d);
        this.name = d.name;
        this.isActive = d.isActive;
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  onSave(): void {
    if (!this.name.trim()) return;
    this.busy.set(true);
    this.service.update(this.id(), { name: this.name.trim(), isActive: this.isActive }).subscribe({
      next: (d) => {
        this.detail.set(d);
        this.toast.success('تم حفظ التعديلات.');
        this.busy.set(false);
      },
      error: (err) => {
        this.apiError.notify(err, 'تعذّر حفظ التعديلات');
        this.busy.set(false);
      },
    });
  }

  async onDelete(): Promise<void> {
    const ok = await this.dialog.confirm({
      title: `حذف المستخدم ${this.detail()?.name}؟`,
      message: 'سيتم إلغاء تفعيل هويته في IdM ثم حذفه من القائمة.',
      kind: 'danger',
      confirmLabel: 'حذف',
    });
    if (!ok) return;
    this.busy.set(true);
    this.service.remove(this.id()).subscribe({
      next: () => {
        this.toast.success('تم حذف المستخدم.');
        this.router.navigateByUrl('/admin/admins');
      },
      error: (err) => {
        this.apiError.notify(err, 'فشل الحذف');
        this.busy.set(false);
      },
    });
  }
}

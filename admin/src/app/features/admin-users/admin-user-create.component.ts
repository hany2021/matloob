import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import { AdminUserService } from '../../core/services/admin-user.service';
import { CheckIdmResult } from '../../core/models/admin-user';
import { ToastService } from '../../shared/components/toast.service';
import { ApiErrorService } from '../../core/http/api-error.service';

/**
 * Create-admin form — the email-first flow from the legacy AdminResource:
 * type an email, "تحقق من IdM" looks it up, then the form either links the
 * existing identity or creates a new one on save (the API runs the saga).
 */
@Component({
  selector: 'app-admin-user-create',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="page">
      <header class="page-header">
        <a class="muted" routerLink="/admin/admins">→ المستخدمون</a>
        <h1>إضافة مستخدم</h1>
      </header>

      <section class="card form">
        <label class="field">
          <span>البريد الإلكتروني</span>
          <div class="with-action">
            <input
              type="email"
              [(ngModel)]="email"
              [disabled]="!!checkResult()?.found"
              placeholder="name@nec.gov.sa"
            />
            <button class="btn btn-secondary" (click)="onCheck()" [disabled]="checking() || !email.trim()">
              {{ checking() ? '...' : 'تحقق من IdM' }}
            </button>
          </div>
          <small class="muted">النطاق المسموح به: @nec.gov.sa</small>
        </label>

        @if (checkResult(); as r) {
          <div class="notice" [class.ok]="r.found" [class.info]="!r.found">
            @if (r.found) {
              تم العثور على هوية في IdM — سيتم ربطها ومنحها صلاحية المشرف.
              @if (r.alreadyAdmin) { <strong> (هذا المستخدم مشرف بالفعل)</strong> }
            } @else {
              لا توجد هوية لهذا البريد — سيتم إنشاء هوية جديدة عند الحفظ.
            }
          </div>
        }

        <label class="field">
          <span>الاسم</span>
          <input
            type="text"
            [(ngModel)]="name"
            [disabled]="!!checkResult()?.found && !!checkResult()?.name"
          />
        </label>

        <div class="row">
          <button class="btn" (click)="onSave()" [disabled]="saving() || !canSave()">
            {{ saving() ? 'جارٍ الحفظ…' : 'حفظ' }}
          </button>
          <a class="btn btn-secondary" routerLink="/admin/admins">إلغاء</a>
        </div>
      </section>
    </div>
  `,
  styles: [
    `
      .form { max-width: 560px; display: flex; flex-direction: column; gap: 16px; }
      .field { display: flex; flex-direction: column; gap: 6px; }
      .field > span { font-weight: 500; }
      .field input { padding: 8px 12px; border: 1px solid var(--color-border); border-radius: 4px; font-family: inherit; }
      .with-action { display: flex; gap: 8px; }
      .with-action input { flex: 1; }
      .notice { padding: 10px 12px; border-radius: 6px; font-size: 14px; }
      .notice.ok { background: #e6f4ea; color: #1f7a3a; }
      .notice.info { background: #e0eafc; color: #1c3d8e; }
      .row { display: flex; gap: 8px; }
    `,
  ],
})
export class AdminUserCreateComponent {
  private readonly service = inject(AdminUserService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly apiError = inject(ApiErrorService);

  protected email = '';
  protected name = '';
  protected readonly checking = signal(false);
  protected readonly saving = signal(false);
  protected readonly checkResult = signal<CheckIdmResult | null>(null);

  protected canSave(): boolean {
    return !!this.email.trim() && !!this.name.trim();
  }

  onCheck(): void {
    const email = this.email.trim();
    if (!email) return;
    this.checking.set(true);
    this.service.checkIdm(email).subscribe({
      next: (r) => {
        this.checkResult.set(r);
        if (r.found && r.name) this.name = r.name;
        this.checking.set(false);
      },
      error: (err) => {
        this.apiError.notify(err, 'تعذّر الوصول إلى IdM');
        this.checking.set(false);
      },
    });
  }

  onSave(): void {
    if (!this.canSave()) return;
    this.saving.set(true);
    this.service
      .create({
        email: this.email.trim(),
        name: this.name.trim(),
        idmExistingUserId: this.checkResult()?.found ? this.checkResult()!.id : null,
      })
      .subscribe({
        next: () => {
          this.toast.success('تم إنشاء المستخدم.');
          this.router.navigateByUrl('/admin/admins');
        },
        error: (err) => {
          this.apiError.notify(err, 'تعذّر إنشاء المستخدم');
          this.saving.set(false);
        },
      });
  }
}

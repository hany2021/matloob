import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { EstablishmentService } from '../../core/services/establishment.service';
import { AssetService } from '../../core/services/asset.service';
import {
  ReviewDetail,
  ReviewHistoryItem,
} from '../../core/models/admin-establishment';
import { LoadingComponent } from '../../shared/components/loading.component';
import { ConfirmDialogService } from '../../shared/components/confirm-dialog.service';
import { ToastService } from '../../shared/components/toast.service';
import { ApiErrorService } from '../../core/http/api-error.service';

/**
 * Admin review page. Approve / reject / suspend / reinstate buttons
 * toggle based on the establishment's current status. Reject + suspend
 * collect a mandatory reason; the backend validates length too (≤2000).
 */
@Component({
  selector: 'app-review-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, LoadingComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <a class="muted" [routerLink]="['/admin/review-queue']">→ طلبات المراجعة</a>
        <h1>{{ detail()?.name ?? 'مراجعة' }}</h1>
        <p class="muted" *ngIf="detail() as d">
          <span class="badge">{{ d.status }}</span>
          <span> · السجل التجاري {{ d.commercialRegistrationNumber }}</span>
        </p>
      </header>

      <app-loading *ngIf="loading()" />

      <ng-container *ngIf="!loading() && detail() as d">
        <section class="card actions">
          <button
            class="btn"
            *ngIf="d.status === 'PendingReview'"
            (click)="onApprove()"
            [disabled]="busy()"
          >موافقة</button>
          <button
            class="btn btn-danger"
            *ngIf="d.status === 'PendingReview'"
            (click)="onReject()"
            [disabled]="busy()"
          >رفض</button>
          <button
            class="btn btn-danger"
            *ngIf="d.status === 'Approved'"
            (click)="onSuspend()"
            [disabled]="busy()"
          >إيقاف</button>
          <button
            class="btn"
            *ngIf="d.status === 'Suspended'"
            (click)="onReinstate()"
            [disabled]="busy()"
          >إعادة التفعيل</button>
        </section>

        <section class="card">
          <h2>البيانات</h2>
          <dl class="kv">
            <div><dt>البريد الإلكتروني</dt><dd>{{ d.email }}</dd></div>
            <div><dt>رقم الجوال</dt><dd>{{ d.phone }}</dd></div>
            <div><dt>مكتب العمل</dt><dd>{{ d.laborOfficeId }}</dd></div>
            <div><dt>الرقم التسلسلي</dt><dd>{{ d.sequenceNumber }}</dd></div>
            <div><dt>المدينة</dt><dd>{{ d.city }}</dd></div>
            <div><dt>الحي</dt><dd>{{ d.district ?? '—' }}</dd></div>
            <div><dt>النشاط الاقتصادي</dt><dd>{{ d.economicActivity ?? '—' }}</dd></div>
            <div><dt>النشاط الفرعي</dt><dd>{{ d.subEconomicActivity ?? '—' }}</dd></div>
            <div><dt>سنوات الخبرة</dt><dd>{{ d.yearsOfExperience ?? '—' }}</dd></div>
            <div><dt>انتهاء السجل التجاري</dt><dd>{{ d.commercialRegistrationExpiry ?? '—' }}</dd></div>
            <div><dt>تاريخ التقديم</dt><dd>{{ d.submittedAt | date: 'medium' }}</dd></div>
            <div *ngIf="d.rejectedAt"><dt>تاريخ الرفض</dt><dd>{{ d.rejectedAt | date: 'medium' }}</dd></div>
            <div *ngIf="d.rejectionReason" class="full"><dt>سبب الرفض</dt><dd>{{ d.rejectionReason }}</dd></div>
            <div *ngIf="d.suspendedAt"><dt>تاريخ الإيقاف</dt><dd>{{ d.suspendedAt | date: 'medium' }}</dd></div>
            <div *ngIf="d.suspensionReason" class="full"><dt>سبب الإيقاف</dt><dd>{{ d.suspensionReason }}</dd></div>
          </dl>
        </section>

        <section class="card" *ngIf="d.documents.length">
          <h2>المستندات</h2>
          <table class="table">
            <thead><tr><th>النوع</th><th>تاريخ الرفع</th><th></th></tr></thead>
            <tbody>
              <tr *ngFor="let doc of d.documents">
                <td>{{ doc.documentType }}</td>
                <td>{{ doc.uploadedAt | date: 'medium' }}</td>
                <td class="doc-actions">
                  <button class="btn btn-ghost" (click)="assets.openInNewTab(doc.assetId)">معاينة</button>
                  <button class="btn btn-ghost" (click)="assets.saveAs(doc.assetId, doc.documentType)">تنزيل</button>
                </td>
              </tr>
            </tbody>
          </table>
        </section>

        <section class="card" *ngIf="history().length">
          <h2>سجل المراجعة</h2>
          <ul class="timeline">
            <li *ngFor="let h of history()">
              <strong>{{ h.action }}</strong>
              <span class="muted"> · {{ h.occurredAt | date: 'medium' }}</span>
              <div *ngIf="h.reason" class="muted">السبب: {{ h.reason }}</div>
            </li>
          </ul>
        </section>
      </ng-container>
    </div>
  `,
  styles: [
    `
      .actions { display: flex; gap: 8px; }
      .kv { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px 24px; margin: 0; }
      .kv div { display: flex; flex-direction: column; }
      .kv .full { grid-column: 1 / -1; }
      .kv dt { color: #6b7280; font-size: 12px; text-transform: uppercase; letter-spacing: 0.04em; }
      .kv dd { margin: 0; font-weight: 500; }
      .mono { font-family: ui-monospace, SFMono-Regular, monospace; }
      .timeline { list-style: none; padding: 0; margin: 0; }
      .timeline li { padding: 8px 0; border-bottom: 1px solid #e5e7eb; }
      .timeline li:last-child { border-bottom: 0; }
    `,
  ],
})
export class ReviewDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(EstablishmentService);
  protected readonly assets = inject(AssetService);
  private readonly dialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly apiError = inject(ApiErrorService);

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly detail = signal<ReviewDetail | null>(null);
  protected readonly history = signal<ReviewHistoryItem[]>([]);

  protected readonly id = computed(() => this.route.snapshot.paramMap.get('id') ?? '');

  ngOnInit(): void {
    this.reload();
  }

  private reload(): void {
    const id = this.id();
    if (!id) {
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.service.getReviewDetail(id).subscribe({
      next: (d) => {
        this.detail.set(d);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
    this.service.getReviewHistory(id).subscribe({
      next: (r) => this.history.set(r.items),
      error: () => undefined,
    });
  }

  async onApprove(): Promise<void> {
    const ok = await this.dialog.confirm({
      title: 'هل تريد الموافقة على هذه المنشأة؟',
      message: 'ستحصل على صلاحية الوصول الكامل فوراً.',
      confirmLabel: 'موافقة',
    });
    if (!ok) return;
    this.busy.set(true);
    this.service.approve(this.id()).subscribe({
      next: () => {
        this.toast.success('تمت الموافقة.');
        this.busy.set(false);
        this.reload();
      },
      error: (err) => {
        this.apiError.notify(err, 'فشلت الموافقة');
        this.busy.set(false);
      },
    });
  }

  async onReject(): Promise<void> {
    const reason = await this.promptReason('هل تريد رفض هذه المنشأة؟');
    if (!reason) return;
    this.busy.set(true);
    this.service.reject(this.id(), reason).subscribe({
      next: () => {
        this.toast.success('تم الرفض.');
        this.busy.set(false);
        this.reload();
      },
      error: (err) => {
        this.apiError.notify(err, 'فشل الرفض');
        this.busy.set(false);
      },
    });
  }

  async onSuspend(): Promise<void> {
    const reason = await this.promptReason('هل تريد إيقاف هذه المنشأة؟');
    if (!reason) return;
    this.busy.set(true);
    this.service.suspend(this.id(), reason).subscribe({
      next: () => {
        this.toast.success('تم الإيقاف.');
        this.busy.set(false);
        this.reload();
      },
      error: (err) => {
        this.apiError.notify(err, 'فشل الإيقاف');
        this.busy.set(false);
      },
    });
  }

  async onReinstate(): Promise<void> {
    const ok = await this.dialog.confirm({
      title: 'هل تريد إعادة تفعيل هذه المنشأة؟',
      confirmLabel: 'إعادة التفعيل',
    });
    if (!ok) return;
    this.busy.set(true);
    this.service.reinstate(this.id()).subscribe({
      next: () => {
        this.toast.success('تمت إعادة التفعيل.');
        this.busy.set(false);
        this.reload();
      },
      error: (err) => {
        this.apiError.notify(err, 'فشلت إعادة التفعيل');
        this.busy.set(false);
      },
    });
  }

  private async promptReason(title: string): Promise<string | null> {
    const proceed = await this.dialog.confirm({ title, kind: 'danger', confirmLabel: 'متابعة' });
    if (!proceed) return null;
    const reason = window.prompt('السبب (مطلوب، 2000 حرف كحد أقصى):');
    if (!reason || !reason.trim()) {
      this.toast.warning('السبب مطلوب.');
      return null;
    }
    return reason.trim();
  }
}

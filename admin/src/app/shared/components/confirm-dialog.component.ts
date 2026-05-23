import { CommonModule } from '@angular/common';
import { Component, computed, inject } from '@angular/core';

import { ConfirmDialogService } from './confirm-dialog.service';

/**
 * Singleton dialog host. Mount once at the app root; opens whenever any
 * caller invokes `ConfirmDialogService.confirm({ ... })`. Native
 * `<dialog>` is intentionally avoided so we get explicit control over
 * backdrop + escape behavior.
 */
@Component({
  selector: 'app-confirm-dialog-host',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="backdrop" *ngIf="visible()" (click)="onBackdrop($event)">
      <div class="dialog" role="dialog" aria-modal="true">
        <h2>{{ request()?.title ?? 'Are you sure?' }}</h2>
        <p *ngIf="request()?.message">{{ request()?.message }}</p>
        <div class="actions">
          <button type="button" class="btn-ghost" (click)="cancel()">
            {{ request()?.cancelLabel ?? 'Cancel' }}
          </button>
          <button
            type="button"
            [class]="confirmButtonClass()"
            (click)="confirm()"
          >
            {{ request()?.confirmLabel ?? 'Confirm' }}
          </button>
        </div>
      </div>
    </div>
  `,
  styles: [
    `
      .backdrop {
        position: fixed; inset: 0; background: rgba(15, 23, 39, 0.55);
        display: flex; align-items: center; justify-content: center; z-index: 1000;
      }
      .dialog {
        background: #fff; padding: 24px; border-radius: 12px;
        min-width: 320px; max-width: 480px;
        box-shadow: 0 10px 30px rgba(0, 0, 0, 0.2);
      }
      .dialog h2 { margin: 0 0 8px; font-size: 18px; }
      .dialog p { margin: 0 0 16px; color: #4b5563; }
      .actions { display: flex; gap: 8px; justify-content: flex-end; }
    `,
  ],
})
export class ConfirmDialogHostComponent {
  private readonly dialog = inject(ConfirmDialogService);

  readonly request = this.dialog.request;
  readonly visible = computed(() => this.request() !== null);

  readonly confirmButtonClass = computed(() => {
    const kind = this.request()?.kind ?? 'default';
    return kind === 'danger' ? 'btn btn-danger' : 'btn';
  });

  confirm(): void { this.dialog.resolve(true); }
  cancel(): void { this.dialog.resolve(false); }
  onBackdrop(event: MouseEvent): void {
    if (event.target === event.currentTarget) this.cancel();
  }
}

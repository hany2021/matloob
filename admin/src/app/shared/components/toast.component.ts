import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ToastService } from './toast.service';

@Component({
  selector: 'app-toast-host',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="toast-host">
      @for (t of toast.toasts(); track t.id) {
        <div class="toast" [class]="'toast-' + t.kind">
          <div class="toast-body">
            <strong>{{ t.title }}</strong>
            @if (t.body) {
              <span>{{ t.body }}</span>
            }
          </div>
          <button
            type="button"
            class="dismiss"
            (click)="toast.dismiss(t.id)"
            aria-label="إغلاق"
          >
            ×
          </button>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .toast-host {
        position: fixed;
        inset-inline-end: var(--space-5);
        bottom: var(--space-5);
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
        z-index: 1000;
      }
      .toast {
        display: flex;
        align-items: flex-start;
        gap: var(--space-3);
        min-width: 280px;
        max-width: 420px;
        padding: var(--space-3) var(--space-4);
        background: var(--color-surface);
        border: 1px solid var(--color-border);
        border-radius: var(--radius);
        box-shadow: var(--shadow-md);
      }
      .toast-body {
        display: flex;
        flex-direction: column;
        gap: 2px;
        flex: 1;
      }
      .dismiss {
        background: none;
        border: none;
        font-size: 18px;
        color: var(--color-text-muted);
        cursor: pointer;
      }
      .toast-info {
        border-inline-start: 4px solid var(--color-info);
      }
      .toast-success {
        border-inline-start: 4px solid var(--color-success);
      }
      .toast-warning {
        border-inline-start: 4px solid var(--color-warning);
      }
      .toast-error {
        border-inline-start: 4px solid var(--color-danger);
      }
    `,
  ],
})
export class ToastHostComponent {
  protected readonly toast = inject(ToastService);
}

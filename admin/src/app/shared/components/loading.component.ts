import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-loading',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="loading" role="status" aria-live="polite">
      <span class="spinner" aria-hidden="true"></span>
      <span>{{ label }}</span>
    </div>
  `,
  styles: [
    `
      .loading { display: inline-flex; align-items: center; gap: 8px; color: #4b5563; }
      .spinner {
        width: 16px; height: 16px; border-radius: 50%;
        border: 2px solid #cbd5e1; border-top-color: #1f5eff;
        animation: spin 0.8s linear infinite;
      }
      @keyframes spin { to { transform: rotate(360deg); } }
    `,
  ],
})
export class LoadingComponent {
  @Input() label = 'Loading…';
}

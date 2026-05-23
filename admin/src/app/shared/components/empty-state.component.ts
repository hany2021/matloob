import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-empty-state',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="empty">
      <h3>{{ heading }}</h3>
      <p *ngIf="message">{{ message }}</p>
      <ng-content />
    </div>
  `,
  styles: [
    `
      .empty {
        border: 1px dashed #cbd5e1;
        border-radius: 12px;
        padding: 32px;
        text-align: center;
        color: #4b5563;
      }
      .empty h3 { margin: 0 0 8px; }
      .empty p { margin: 0 0 16px; }
    `,
  ],
})
export class EmptyStateComponent {
  @Input() heading = 'Nothing to show yet';
  @Input() message?: string;
}

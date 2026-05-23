import { Component, Input } from '@angular/core';

/**
 * Minimal placeholder for routes that have a stable URL but no UI yet.
 * Used during phased implementation to keep the navigation skeleton
 * coherent without shipping half-finished pages.
 */
@Component({
  selector: 'app-coming-soon',
  standalone: true,
  template: `
    <div class="page">
      <header class="page-header">
        <h1>{{ heading }}</h1>
      </header>
      <div class="card">
        <p class="muted">{{ message }}</p>
      </div>
    </div>
  `,
})
export class ComingSoonComponent {
  @Input() heading = 'Coming soon';
  @Input() message =
    'This page is part of a later phase of the admin migration. Check back after the next deploy.';
}

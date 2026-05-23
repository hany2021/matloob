import { Component } from '@angular/core';
import { ComingSoonComponent } from '../../shared/components/coming-soon.component';

@Component({
  selector: 'app-review-queue',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `<app-coming-soon heading="Review queue" />`,
})
export class ReviewQueueComponent {}

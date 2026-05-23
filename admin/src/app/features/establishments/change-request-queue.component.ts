import { Component } from '@angular/core';
import { ComingSoonComponent } from '../../shared/components/coming-soon.component';

@Component({
  selector: 'app-change-request-queue',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `<app-coming-soon heading="Pending change requests" />`,
})
export class ChangeRequestQueueComponent {}

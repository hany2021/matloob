import { Component } from '@angular/core';
import { ComingSoonComponent } from '../../shared/components/coming-soon.component';

@Component({
  selector: 'app-opportunity-list',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `<app-coming-soon heading="Opportunities" />`,
})
export class OpportunityListComponent {}

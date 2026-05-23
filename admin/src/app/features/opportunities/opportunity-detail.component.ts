import { Component } from '@angular/core';
import { ComingSoonComponent } from '../../shared/components/coming-soon.component';

@Component({
  selector: 'app-opportunity-detail',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `<app-coming-soon heading="Opportunity detail" />`,
})
export class OpportunityDetailComponent {}

import { Component } from '@angular/core';
import { ComingSoonComponent } from '../../shared/components/coming-soon.component';

@Component({
  selector: 'app-evaluation-list',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `<app-coming-soon heading="Evaluations" />`,
})
export class EvaluationListComponent {}

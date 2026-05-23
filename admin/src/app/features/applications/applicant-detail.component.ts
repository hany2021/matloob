import { Component } from '@angular/core';
import { ComingSoonComponent } from '../../shared/components/coming-soon.component';

@Component({
  selector: 'app-applicant-detail',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `<app-coming-soon heading="Applicant detail" />`,
})
export class ApplicantDetailComponent {}

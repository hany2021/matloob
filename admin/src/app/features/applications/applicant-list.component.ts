import { Component } from '@angular/core';
import { ComingSoonComponent } from '../../shared/components/coming-soon.component';

@Component({
  selector: 'app-applicant-list',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `<app-coming-soon heading="Applicants" />`,
})
export class ApplicantListComponent {}

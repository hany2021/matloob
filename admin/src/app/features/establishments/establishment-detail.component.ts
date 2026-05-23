import { Component } from '@angular/core';
import { ComingSoonComponent } from '../../shared/components/coming-soon.component';

@Component({
  selector: 'app-establishment-detail',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `<app-coming-soon heading="Establishment detail" />`,
})
export class EstablishmentDetailComponent {}

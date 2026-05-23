import { Component } from '@angular/core';
import { ComingSoonComponent } from '../../shared/components/coming-soon.component';

@Component({
  selector: 'app-establishment-list',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `<app-coming-soon heading="Establishments" />`,
})
export class EstablishmentListComponent {}

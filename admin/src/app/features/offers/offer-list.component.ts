import { Component } from '@angular/core';
import { ComingSoonComponent } from '../../shared/components/coming-soon.component';

@Component({
  selector: 'app-offer-list',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `<app-coming-soon heading="Offers" />`,
})
export class OfferListComponent {}

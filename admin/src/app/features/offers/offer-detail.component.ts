import { Component } from '@angular/core';
import { ComingSoonComponent } from '../../shared/components/coming-soon.component';

@Component({
  selector: 'app-offer-detail',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `<app-coming-soon heading="Offer detail" />`,
})
export class OfferDetailComponent {}

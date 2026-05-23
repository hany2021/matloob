import { Component } from '@angular/core';
import { ComingSoonComponent } from '../../shared/components/coming-soon.component';

@Component({
  selector: 'app-send-offer',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `<app-coming-soon heading="Send offer" />`,
})
export class SendOfferComponent {}

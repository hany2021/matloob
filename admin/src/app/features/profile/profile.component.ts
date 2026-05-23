import { Component } from '@angular/core';
import { ComingSoonComponent } from '../../shared/components/coming-soon.component';

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `<app-coming-soon heading="My profile" />`,
})
export class ProfileComponent {}

import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastHostComponent } from './shared/components/toast.component';
import { ConfirmDialogHostComponent } from './shared/components/confirm-dialog.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ToastHostComponent, ConfirmDialogHostComponent],
  template: `
    <router-outlet />
    <app-toast-host />
    <app-confirm-dialog-host />
  `,
})
export class App {}

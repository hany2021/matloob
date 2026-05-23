import { Injectable, signal } from '@angular/core';

export interface ConfirmDialogRequest {
  title: string;
  message?: string;
  confirmLabel?: string;
  cancelLabel?: string;
  kind?: 'default' | 'danger';
}

@Injectable({ providedIn: 'root' })
export class ConfirmDialogService {
  readonly request = signal<ConfirmDialogRequest | null>(null);
  private resolver: ((confirmed: boolean) => void) | null = null;

  confirm(request: ConfirmDialogRequest): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      this.resolver = resolve;
      this.request.set(request);
    });
  }

  resolve(confirmed: boolean): void {
    this.request.set(null);
    const resolver = this.resolver;
    this.resolver = null;
    resolver?.(confirmed);
  }
}

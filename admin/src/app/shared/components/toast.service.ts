import { Injectable, signal } from '@angular/core';

export type ToastKind = 'info' | 'success' | 'warning' | 'error';

export interface Toast {
  id: string;
  kind: ToastKind;
  title: string;
  body?: string;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<readonly Toast[]>([]);

  info(title: string, body?: string): void {
    this.push({ kind: 'info', title, body });
  }
  success(title: string, body?: string): void {
    this.push({ kind: 'success', title, body });
  }
  warning(title: string, body?: string): void {
    this.push({ kind: 'warning', title, body });
  }
  error(title: string, body?: string): void {
    this.push({ kind: 'error', title, body });
  }

  dismiss(id: string): void {
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }

  private push(input: Omit<Toast, 'id'>): void {
    const id = `${Date.now()}-${Math.random().toString(16).slice(2, 8)}`;
    this.toasts.update((list) => [...list, { id, ...input }]);
    // Auto-dismiss after 5s.
    setTimeout(() => this.dismiss(id), 5_000);
  }
}

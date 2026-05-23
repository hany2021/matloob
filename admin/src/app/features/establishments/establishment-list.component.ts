import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { EstablishmentService } from '../../core/services/establishment.service';
import { EstablishmentSummary } from '../../core/models/establishment';
import { LoadingComponent } from '../../shared/components/loading.component';
import { EmptyStateComponent } from '../../shared/components/empty-state.component';

/**
 * Lists every establishment the caller is an active member of. Backed
 * by `GET /api/v1/establishments` which returns the snake_case
 * `EstablishmentSummary` shape.
 */
@Component({
  selector: 'app-establishment-list',
  standalone: true,
  imports: [CommonModule, RouterLink, LoadingComponent, EmptyStateComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <h1>Establishments</h1>
        <p class="muted">Establishments you can act on.</p>
      </header>

      <app-loading *ngIf="loading()" />

      <ng-container *ngIf="!loading()">
        <app-empty-state
          *ngIf="!rows().length"
          heading="No establishments"
          message="You're not a member of any establishment yet."
        />
        <table class="table" *ngIf="rows().length">
          <thead>
            <tr>
              <th>Name</th>
              <th>Status</th>
              <th>CR number</th>
              <th>City</th>
              <th>My role</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let e of rows()">
              <td>{{ e.name }}</td>
              <td><span class="badge">{{ e.status }}</span></td>
              <td>{{ e.commercialRegistrationNumber }}</td>
              <td>{{ e.city }}</td>
              <td>{{ e.myRole ?? '—' }}</td>
              <td>
                <a class="btn btn-ghost" [routerLink]="['/establishments', e.id]">Open</a>
              </td>
            </tr>
          </tbody>
        </table>
      </ng-container>
    </div>
  `,
})
export class EstablishmentListComponent implements OnInit {
  private readonly service = inject(EstablishmentService);
  protected readonly loading = signal(true);
  protected readonly rows = signal<EstablishmentSummary[]>([]);

  ngOnInit(): void {
    this.service.listMine().subscribe({
      next: (rows) => {
        this.rows.set(rows);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}

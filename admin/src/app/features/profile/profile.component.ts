import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject } from '@angular/core';

import { LoadingComponent } from '../../shared/components/loading.component';
import { EmptyStateComponent } from '../../shared/components/empty-state.component';
import { ProfileService } from '../../core/services/profile.service';
import { AuthService } from '../../core/auth/auth.service';

/**
 * My-profile page. Reads from the cached ProfileService snapshot loaded
 * by the admin shell — does its own initial load only if the user
 * landed here via deep link before the shell finished. PATCH endpoints
 * for editing the user record aren't migrated yet (docs/40-api-migration-
 * readiness.md §6/§7), so the page is read-only for now.
 */
@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [CommonModule, LoadingComponent, EmptyStateComponent],
  template: `
    <div class="page">
      <header class="page-header">
        <h1>My profile</h1>
        <p class="muted">Read-only snapshot of your account.</p>
      </header>

      <app-loading *ngIf="!profileService.loaded()" />

      <ng-container *ngIf="profileService.loaded()">
        <section class="card" *ngIf="profile() as p">
          <h2>Account</h2>
          <dl class="kv">
            <div><dt>Name</dt><dd>{{ p.name ?? '—' }}</dd></div>
            <div><dt>Email</dt><dd>{{ p.email ?? '—' }}</dd></div>
            <div><dt>Phone</dt><dd>{{ p.phone_number ?? '—' }}</dd></div>
            <div><dt>Identity ID</dt><dd class="mono">{{ p.identity_id }}</dd></div>
            <div><dt>Onboarded</dt><dd>{{ p.onboarded ? 'Yes' : 'No' }}</dd></div>
            <div>
              <dt>Profile completeness</dt>
              <dd>{{ p.profile_complete_percentage }}%</dd>
            </div>
            <div *ngIf="auth.isAdmin()"><dt>Role</dt><dd>matloob_admin</dd></div>
          </dl>
        </section>

        <section class="card">
          <h2>Establishments</h2>
          <app-empty-state
            *ngIf="!establishments().length"
            heading="No establishments"
            message="You're not an active member of any establishment yet."
          />
          <table class="table" *ngIf="establishments().length">
            <thead>
              <tr>
                <th>Name</th>
                <th>Status</th>
                <th>Role</th>
                <th>Labor office</th>
                <th>Sequence</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let e of establishments()">
                <td>{{ e.name }}</td>
                <td><span class="badge">{{ e.status }}</span></td>
                <td>{{ e.role }}</td>
                <td>{{ e.labor_office_id }}</td>
                <td>{{ e.sequence_number }}</td>
              </tr>
            </tbody>
          </table>
        </section>

        <section class="card" *ngIf="uncompletedSections().length">
          <h2>Sections to complete</h2>
          <ul>
            <li *ngFor="let s of uncompletedSections()">{{ s }}</li>
          </ul>
          <p class="muted">Editing endpoints aren't migrated yet — this list is read-only.</p>
        </section>
      </ng-container>
    </div>
  `,
  styles: [
    `
      .kv { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px 24px; margin: 0; }
      .kv div { display: flex; flex-direction: column; }
      .kv dt { color: #6b7280; font-size: 12px; text-transform: uppercase; letter-spacing: 0.04em; }
      .kv dd { margin: 0; font-weight: 500; }
      .mono { font-family: ui-monospace, SFMono-Regular, monospace; word-break: break-all; }
    `,
  ],
})
export class ProfileComponent implements OnInit {
  protected readonly profileService = inject(ProfileService);
  protected readonly auth = inject(AuthService);

  protected readonly profile = this.profileService.profile;
  protected readonly establishments = this.profileService.establishments;
  protected readonly uncompletedSections = computed(
    () => this.profile()?.uncompleted_profile_sections ?? [],
  );

  ngOnInit(): void {
    if (!this.profileService.loaded()) {
      this.profileService.loadInitData().subscribe({ error: () => undefined });
    }
  }
}

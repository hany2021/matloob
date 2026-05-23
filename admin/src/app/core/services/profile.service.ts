import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, forkJoin, tap } from 'rxjs';

import { ApiClient } from '../http/api-client';
import { InitData, MyEstablishmentListItem, UserProfile } from '../models/profile';

/**
 * Reads the current user's profile + establishment memberships. Caches
 * the last successful load as signals so the shell can surface the
 * caller's name and active establishments without a refetch on every
 * route change. Also exposes an `activeEstablishmentId` signal —
 * opportunity/offer/evaluation pages all need to know which
 * establishment is acting, and we seed it to the first membership.
 */
@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly api = inject(ApiClient);

  readonly profile = signal<UserProfile | null>(null);
  readonly establishments = signal<MyEstablishmentListItem[]>([]);
  readonly loaded = signal(false);

  private readonly activeIdInternal = signal<string | null>(null);
  /** First active membership, seeded when the establishment list loads. */
  readonly activeEstablishmentId = computed(() => {
    const explicit = this.activeIdInternal();
    if (explicit) return explicit;
    return this.establishments()[0]?.id ?? null;
  });

  loadProfile(): Observable<UserProfile> {
    return this.api.get<UserProfile>('/api/v1/profile').pipe(
      tap((profile) => this.profile.set(profile)),
    );
  }

  loadEstablishments(): Observable<MyEstablishmentListItem[]> {
    return this.api
      .get<MyEstablishmentListItem[]>('/api/v1/users/profile/establishment-list')
      .pipe(tap((rows) => this.establishments.set(rows)));
  }

  /** Triggers both reads in parallel and snapshots the result. */
  loadInitData(): Observable<InitData> {
    return forkJoin({
      profile: this.loadProfile(),
      establishments: this.loadEstablishments(),
    }).pipe(
      tap(() => this.loaded.set(true)),
    );
  }

  setActiveEstablishment(id: string | null): void {
    this.activeIdInternal.set(id);
  }

  clear(): void {
    this.profile.set(null);
    this.establishments.set([]);
    this.activeIdInternal.set(null);
    this.loaded.set(false);
  }
}

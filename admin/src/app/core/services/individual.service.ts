import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../http/api-client';
import { ListIndividualsResponse } from '../models/individual';

/**
 * Talks to the admin individuals endpoint `/api/v1/admin/individuals`. The
 * .NET port of the legacy Filament UserResource list (view-only): individual
 * user accounts (the TPH base `User` rows, excluding back-office admins).
 */
@Injectable({ providedIn: 'root' })
export class IndividualService {
  private readonly api = inject(ApiClient);

  list(search?: string, page = 1, pageSize = 50): Observable<ListIndividualsResponse> {
    return this.api.get<ListIndividualsResponse>('/api/v1/admin/individuals', {
      search,
      page,
      pageSize,
    });
  }
}

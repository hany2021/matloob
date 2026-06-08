import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../http/api-client';
import {
  EstablishmentRoleFilter,
  ListAdminEstablishmentsResponse,
} from '../models/admin-establishment';

/**
 * Talks to the admin establishments-list endpoint
 * `/api/v1/admin/establishments`. The .NET port of the legacy Filament
 * EstablishmentResource list. There is no organizer/operator TYPE: an
 * "organizer" is an establishment with `can_manage_events`, every
 * establishment is an "operator" by default. The `role` filter feeds the two
 * admin screens.
 */
@Injectable({ providedIn: 'root' })
export class AdminEstablishmentListService {
  private readonly api = inject(ApiClient);

  list(
    role?: EstablishmentRoleFilter,
    search?: string,
    page = 1,
    pageSize = 50,
  ): Observable<ListAdminEstablishmentsResponse> {
    return this.api.get<ListAdminEstablishmentsResponse>('/api/v1/admin/establishments', {
      role,
      search,
      page,
      pageSize,
    });
  }
}

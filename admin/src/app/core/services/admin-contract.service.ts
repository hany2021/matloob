import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../http/api-client';
import { ListAdminContractsResponse } from '../models/admin-contract';

/**
 * Talks to the admin contracts endpoint `/api/v1/admin/contracts`. In the
 * Ajeer-stripped model an accepted offer IS the contract, so this lists
 * offers in a binding status (accepted and downstream). View-only.
 */
@Injectable({ providedIn: 'root' })
export class AdminContractService {
  private readonly api = inject(ApiClient);

  list(search?: string, page = 1, pageSize = 50): Observable<ListAdminContractsResponse> {
    return this.api.get<ListAdminContractsResponse>('/api/v1/admin/contracts', {
      search,
      page,
      pageSize,
    });
  }
}

import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../http/api-client';
import {
  CreateOpportunityRequest,
  LinkOpportunityAssetRequest,
  LinkOpportunityAssetResponse,
  Opportunity,
  UpdateOpportunityRequest,
} from '../models/opportunity';

/**
 * Calls the canonical opportunity endpoints
 * `/api/v1/establishments/{establishmentId}/opportunities/...`. Every
 * method requires the caller to know which establishment is acting;
 * the admin shell sources that from the user's establishment list.
 */
@Injectable({ providedIn: 'root' })
export class OpportunityService {
  private readonly api = inject(ApiClient);

  list(establishmentId: string, filters: { status?: string; name?: string; category?: string } = {}):
    Observable<Opportunity[]> {
    return this.api.get<Opportunity[]>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/opportunities`,
      filters as Record<string, string>,
    );
  }

  get(establishmentId: string, id: string): Observable<Opportunity> {
    return this.api.get<Opportunity>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/opportunities/${encodeURIComponent(id)}`,
    );
  }

  create(establishmentId: string, body: CreateOpportunityRequest): Observable<Opportunity> {
    return this.api.post<Opportunity>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/opportunities`,
      body,
    );
  }

  update(establishmentId: string, id: string, body: UpdateOpportunityRequest): Observable<Opportunity> {
    return this.api.patch<Opportunity>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/opportunities/${encodeURIComponent(id)}`,
      body,
    );
  }

  end(establishmentId: string, id: string): Observable<Opportunity> {
    return this.api.patch<Opportunity>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/opportunities/${encodeURIComponent(id)}/end`,
      {},
    );
  }

  delete(establishmentId: string, id: string): Observable<void> {
    return this.api.delete<void>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/opportunities/${encodeURIComponent(id)}`,
    );
  }

  linkAsset(
    establishmentId: string,
    id: string,
    body: LinkOpportunityAssetRequest,
  ): Observable<LinkOpportunityAssetResponse> {
    return this.api.post<LinkOpportunityAssetResponse>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/opportunities/${encodeURIComponent(id)}/assets`,
      body,
    );
  }

  listApplicants<T = unknown>(establishmentId: string, opportunityId: string): Observable<T> {
    return this.api.get<T>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/opportunities/${encodeURIComponent(opportunityId)}/applications`,
    );
  }

  getApplicant<T = unknown>(establishmentId: string, applicantId: string): Observable<T> {
    return this.api.get<T>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/applicants/${encodeURIComponent(applicantId)}`,
    );
  }

  listAllApplications<T = unknown>(establishmentId: string): Observable<T> {
    return this.api.get<T>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/browse/applications`,
    );
  }
}

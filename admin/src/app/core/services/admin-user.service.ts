import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../http/api-client';
import {
  AdminDetail,
  CheckIdmResult,
  CreateAdminRequest,
  ListAdminsResponse,
  UpdateAdminRequest,
} from '../models/admin-user';

/**
 * Talks to the admin-user endpoints `/api/v1/admin/admins/*`. These are the
 * .NET port of the legacy Filament AdminResource: the list reads a local
 * `admins` table, while create/update/delete drive the IdM identity behind the
 * scenes (assign `matloob_admin`, create/deactivate the identity).
 */
@Injectable({ providedIn: 'root' })
export class AdminUserService {
  private readonly api = inject(ApiClient);

  list(search?: string, page = 1, pageSize = 50): Observable<ListAdminsResponse> {
    return this.api.get<ListAdminsResponse>('/api/v1/admin/admins', {
      search,
      page,
      pageSize,
    });
  }

  checkIdm(email: string): Observable<CheckIdmResult> {
    return this.api.get<CheckIdmResult>('/api/v1/admin/admins/check-idm', { email });
  }

  create(body: CreateAdminRequest): Observable<AdminDetail> {
    return this.api.post<AdminDetail>('/api/v1/admin/admins', body);
  }

  get(id: string): Observable<AdminDetail> {
    return this.api.get<AdminDetail>(`/api/v1/admin/admins/${encodeURIComponent(id)}`);
  }

  update(id: string, body: UpdateAdminRequest): Observable<AdminDetail> {
    return this.api.put<AdminDetail>(`/api/v1/admin/admins/${encodeURIComponent(id)}`, body);
  }

  remove(id: string): Observable<void> {
    return this.api.delete<void>(`/api/v1/admin/admins/${encodeURIComponent(id)}`);
  }
}

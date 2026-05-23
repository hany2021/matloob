import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../http/api-client';
import {
  AddMemberRequest,
  ListMembersResponse,
  MemberSummary,
  PendingChangeRequestsResponse,
  PendingReviewResponse,
  ReviewDetail,
  ReviewHistoryResponse,
  UpdateMemberRequest,
} from '../models/admin-establishment';
import { EstablishmentDetail, EstablishmentSummary, ListMineResponse } from '../models/establishment';
import { map } from 'rxjs/operators';

/**
 * Talks to every establishment endpoint the admin UI needs:
 * - Admin-only review queue + transitions (camelCase shapes).
 * - Member management (camelCase).
 * - Self-read composite detail (snake_case, mirrors Laravel resource).
 * - Change-request admin queue + decisions.
 */
@Injectable({ providedIn: 'root' })
export class EstablishmentService {
  private readonly api = inject(ApiClient);

  // -- Admin queue --------------------------------------------------------

  listPendingReview(page = 1, pageSize = 20): Observable<PendingReviewResponse> {
    return this.api.get<PendingReviewResponse>('/api/v1/admin/establishments/pending-review', {
      page,
      pageSize,
    });
  }

  getReviewDetail(id: string): Observable<ReviewDetail> {
    return this.api.get<ReviewDetail>(`/api/v1/admin/establishments/${encodeURIComponent(id)}/review`);
  }

  getReviewHistory(id: string): Observable<ReviewHistoryResponse> {
    return this.api.get<ReviewHistoryResponse>(
      `/api/v1/admin/establishments/${encodeURIComponent(id)}/review-history`,
    );
  }

  approve(id: string): Observable<unknown> {
    return this.api.post(`/api/v1/admin/establishments/${encodeURIComponent(id)}/approve`, {});
  }

  reject(id: string, reason: string): Observable<unknown> {
    return this.api.post(`/api/v1/admin/establishments/${encodeURIComponent(id)}/reject`, { reason });
  }

  suspend(id: string, reason: string): Observable<unknown> {
    return this.api.post(`/api/v1/admin/establishments/${encodeURIComponent(id)}/suspend`, { reason });
  }

  reinstate(id: string): Observable<unknown> {
    return this.api.post(`/api/v1/admin/establishments/${encodeURIComponent(id)}/reinstate`, {});
  }

  // -- Change requests ---------------------------------------------------

  listPendingChangeRequests(page = 1, pageSize = 20): Observable<PendingChangeRequestsResponse> {
    return this.api.get<PendingChangeRequestsResponse>(
      '/api/v1/admin/establishments/change-requests/pending',
      { page, pageSize },
    );
  }

  getChangeRequestDetail<T = unknown>(id: string): Observable<T> {
    return this.api.get<T>(`/api/v1/admin/establishments/change-requests/${encodeURIComponent(id)}`);
  }

  approveChangeRequest(id: string): Observable<unknown> {
    return this.api.post(
      `/api/v1/admin/establishments/change-requests/${encodeURIComponent(id)}/approve`,
      {},
    );
  }

  rejectChangeRequest(id: string, reason: string): Observable<unknown> {
    return this.api.post(
      `/api/v1/admin/establishments/change-requests/${encodeURIComponent(id)}/reject`,
      { reason },
    );
  }

  // -- Members -----------------------------------------------------------

  listMembers(establishmentId: string): Observable<ListMembersResponse> {
    return this.api.get<ListMembersResponse>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/members`,
    );
  }

  addMember(establishmentId: string, body: AddMemberRequest): Observable<MemberSummary> {
    return this.api.post<MemberSummary>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/members`,
      body,
    );
  }

  updateMember(
    establishmentId: string,
    memberId: string,
    body: UpdateMemberRequest,
  ): Observable<MemberSummary> {
    return this.api.patch<MemberSummary>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/members/${encodeURIComponent(memberId)}`,
      body,
    );
  }

  removeMember(establishmentId: string, memberId: string): Observable<void> {
    return this.api.delete<void>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/members/${encodeURIComponent(memberId)}`,
    );
  }

  // -- Self-read ---------------------------------------------------------

  listMine(): Observable<EstablishmentSummary[]> {
    return this.api
      .get<ListMineResponse>('/api/v1/establishments')
      .pipe(map((r) => r.items));
  }

  getDetail(id: string): Observable<EstablishmentDetail> {
    return this.api.get<EstablishmentDetail>(`/api/v1/establishments/${encodeURIComponent(id)}`);
  }
}

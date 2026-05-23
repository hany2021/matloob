import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../http/api-client';
import {
  CreateEvaluationRequest,
  Evaluation,
} from '../models/evaluation';
import { Offer } from '../models/offer';

/**
 * Evaluation endpoints scoped to an establishment. Evaluations attach
 * to `offer_id` (Q-EVAL-1 — Contracts are gone, so there's no
 * contract_id anywhere). The `other_evaluation` lookup surfaces the
 * counterparty's evaluation for the same offer once they've left one.
 */
@Injectable({ providedIn: 'root' })
export class EvaluationService {
  private readonly api = inject(ApiClient);

  list(establishmentId: string): Observable<Evaluation[]> {
    return this.api.get<Evaluation[]>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/evaluations`,
    );
  }

  get(establishmentId: string, id: string): Observable<Evaluation> {
    return this.api.get<Evaluation>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/evaluations/${encodeURIComponent(id)}`,
    );
  }

  create(establishmentId: string, body: CreateEvaluationRequest): Observable<Evaluation> {
    return this.api.post<Evaluation>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/evaluations`,
      body,
    );
  }

  /**
   * Returns accepted offers the establishment hasn't evaluated yet.
   * The backend returns full {@link Offer} records (the same shape as
   * sent/received offer reads), so callers project the fields they
   * need rather than working off a slimmed projection.
   */
  listUnevaluatedOffers(establishmentId: string): Observable<Offer[]> {
    return this.api.get<Offer[]>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/offers/unevaluated`,
    );
  }

  getOtherEvaluation(establishmentId: string, offerId: string): Observable<Evaluation> {
    return this.api.get<Evaluation>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/offers/${encodeURIComponent(offerId)}/other-evaluation`,
    );
  }
}

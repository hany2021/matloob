import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../http/api-client';
import {
  CreateEvaluationRequest,
  Evaluation,
  UnevaluatedOffer,
} from '../models/evaluation';

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

  listUnevaluatedOffers(establishmentId: string): Observable<UnevaluatedOffer[]> {
    return this.api.get<UnevaluatedOffer[]>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/offers/unevaluated`,
    );
  }

  getOtherEvaluation(establishmentId: string, offerId: string): Observable<Evaluation> {
    return this.api.get<Evaluation>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/offers/${encodeURIComponent(offerId)}/other-evaluation`,
    );
  }
}

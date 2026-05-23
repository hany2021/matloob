import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../http/api-client';
import { CancelOfferRequest, Offer, SendOfferRequest } from '../models/offer';

/**
 * Wraps the establishment-side offer endpoints. The user-side flows
 * (worker accept/reject, worker cancel) aren't surfaced in the admin
 * UI — that's for the public frontend / worker app.
 */
@Injectable({ providedIn: 'root' })
export class OfferService {
  private readonly api = inject(ApiClient);

  listSent(establishmentId: string): Observable<Offer[]> {
    return this.api.get<Offer[]>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/sent-offers`,
    );
  }

  getSent(establishmentId: string, id: string): Observable<Offer> {
    return this.api.get<Offer>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/sent-offers/${encodeURIComponent(id)}`,
    );
  }

  listReceived(establishmentId: string): Observable<Offer[]> {
    return this.api.get<Offer[]>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/received-offers`,
    );
  }

  getReceived(establishmentId: string, id: string): Observable<Offer> {
    return this.api.get<Offer>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/received-offers/${encodeURIComponent(id)}`,
    );
  }

  listPendingAction(establishmentId: string): Observable<Offer[]> {
    return this.api.get<Offer[]>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/offers/pending-action`,
    );
  }

  send(establishmentId: string, body: SendOfferRequest): Observable<Offer> {
    return this.api.post<Offer>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/offers/send`,
      body,
    );
  }

  accept(establishmentId: string, id: string): Observable<Offer> {
    return this.api.post<Offer>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/offers/${encodeURIComponent(id)}/accept`,
      {},
    );
  }

  reject(establishmentId: string, id: string): Observable<Offer> {
    return this.api.post<Offer>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/offers/${encodeURIComponent(id)}/reject`,
      {},
    );
  }

  requestCancel(establishmentId: string, body: CancelOfferRequest): Observable<Offer> {
    return this.api.post<Offer>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/offers/cancel`,
      body,
    );
  }

  approveCancellation(establishmentId: string, id: string): Observable<Offer> {
    return this.api.post<Offer>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/offers/${encodeURIComponent(id)}/approve-cancellation`,
      {},
    );
  }

  rejectCancellation(establishmentId: string, id: string): Observable<Offer> {
    return this.api.post<Offer>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/offers/${encodeURIComponent(id)}/reject-cancellation`,
      {},
    );
  }

  sponsorAccept(establishmentId: string, id: string): Observable<Offer> {
    return this.api.post<Offer>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/offers/${encodeURIComponent(id)}/sponsor/accept`,
      {},
    );
  }

  sponsorReject(establishmentId: string, id: string): Observable<Offer> {
    return this.api.post<Offer>(
      `/api/v1/establishments/${encodeURIComponent(establishmentId)}/offers/${encodeURIComponent(id)}/sponsor/reject`,
      {},
    );
  }
}

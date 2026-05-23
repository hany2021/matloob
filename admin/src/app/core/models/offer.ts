import { OpportunityApplication, ApplicationAppliedBy } from './application';
import { Opportunity } from './opportunity';

export type OfferStatus =
  | 'sent'
  | 'accepted'
  | 'rejected'
  | 'cancelled'
  | 'expired'
  | 'pending_sponsor'
  | string;

export interface OfferSender {
  id: string;
  name: string | null;
  email: string | null;
}

export interface OfferJobTitle {
  id: string;
  name: string | null;
}

export interface OfferCancellationRequest {
  id: string;
  requested_by_type: string;
  requested_by_id: string;
  reason_id: string | null;
  other_reason: string | null;
  is_approved: boolean;
  is_rejected: boolean;
  requested_at: string;
  reviewed_at: string | null;
}

export interface Offer {
  id: string;
  sender: OfferSender | null;
  applicant: OpportunityApplication | null;
  opportunity: Opportunity | null;
  job_title: OfferJobTitle | null;
  monthly_salary: number | null;
  daily_wage: number | null;
  number_of_working_days: number | null;
  currency: string;
  status: OfferStatus;
  status_color?: string | null;
  status_label?: string | null;
  offer_validity_from: string | null;
  offer_validity_to: string | null;
  expiry_date: string | null;
  start_date: string | null;
  end_date: string | null;
  other_details: string | null;
  laborer_commitments: string | null;
  created_at: string;
  expired: boolean;
  evaluated: boolean;
  cancelled_by: string | null;
  cancellation_request: OfferCancellationRequest | null;
  applied_by: ApplicationAppliedBy | null;
  sent_by: ApplicationAppliedBy | null;
  is_pending_sponsor_approval: boolean;
  accepted_at: string | null;
}

export interface OfferListResponse {
  data: Offer[];
  page: number;
  page_size: number;
  total: number;
}

export interface SendOfferRequest {
  application_id: string;
  job_title_id: string;
  monthly_salary?: number | null;
  daily_wage?: number | null;
  number_of_working_days?: number | null;
  currency?: string;
  offer_validity_from: string;
  offer_validity_to: string;
  start_date: string;
  end_date: string;
  other_details?: string | null;
  laborer_commitments?: string | null;
}

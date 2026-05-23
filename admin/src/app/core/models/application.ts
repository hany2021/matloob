import { Opportunity } from './opportunity';

export type ApplicationStatus =
  | 'pending'
  | 'accepted'
  | 'rejected'
  | 'cancelled'
  | 'completed'
  | string;

export interface ApplicationApplier {
  id: string;
  name: string | null;
  email: string | null;
}

export interface ApplicationAppliedBy {
  id: string;
  name: string | null;
  email: string | null;
}

export interface OpportunityApplication {
  id: string;
  applier_type: 'user' | 'organization';
  applier: ApplicationApplier | null;
  opportunity: Opportunity | null;
  status: ApplicationStatus;
  status_label?: string | null;
  created_at: string;
  applied_by: ApplicationAppliedBy | null;
}

export interface ApplicationListResponse {
  data: OpportunityApplication[];
  page: number;
  page_size: number;
  total: number;
}

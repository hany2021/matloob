/**
 * Opportunity wire shape returned by `/api/v1/opportunities/*` endpoints.
 * Snake_case to match the .NET payload (we don't camelize on the client
 * — the surface area is too wide and the cost outweighs the prettiness).
 */
export type OpportunityStatus = 'draft' | 'published' | 'ended' | string;

export interface NamedRef {
  id: string;
  name: string;
}

export interface OpportunityCategory {
  id: string;
  title: string;
  description?: string | null;
  icon?: string | null;
  for_vacancy: boolean;
  is_other: boolean;
}

export interface OpportunityUpload {
  id: string;
  file_name: string;
  content_type: string;
  size_bytes: number;
}

export interface OpportunityIssuer {
  id: string;
  name: string | null;
  email: string | null;
  logo: string | null;
}

export interface SuccessCriterion {
  id: string;
  output: string;
  success_criteria: string;
  comment?: string | null;
  uploads: OpportunityUpload[];
}

export interface Opportunity {
  id: string;
  name: string;
  description: string;
  start_date: string;
  end_date: string;
  lat: number;
  lon: number;
  location_title: string;
  required_personnel: number;
  monthly_salary: number | null;
  years_of_experience_required: number | null;
  establishment_classification: string[];
  establishment_classification_label?: string | null;
  working_hours_type: string | null;
  working_hours_type_label?: string | null;
  working_hours_from: string | null;
  working_hours_to: string | null;
  fees: number | null;
  phone_contact_information: string | null;
  email_contact_information: string | null;
  gender: string[];
  gender_label?: string | null;
  nationality: NamedRef | null;
  status: OpportunityStatus;
  card_type?: string | null;
  status_label?: string | null;
  status_icon?: string | null;
  opportunity_category: OpportunityCategory | null;
  uploads: OpportunityUpload[];
  applicants: unknown[];
  applicants_count: number;
  can_end: boolean;
  issuer: OpportunityIssuer | null;
  success_criteria: SuccessCriterion[];
  is_applied?: boolean | null;
}

/**
 * Single-create payload for `POST /api/v1/establishments/{eid}/opportunities`.
 * The bulk `{ event_id, opportunities: [...] }` variant exists on the
 * backend; the admin only ever needs single-create.
 */
export interface CreateOpportunityRequest {
  event_id?: string;
  opportunity_category_id: string;
  name: string;
  description: string;
  start_date: string;
  end_date: string;
  location_title: string;
  lat: number;
  lon: number;
  required_personnel: number;
  monthly_salary?: number | null;
  years_of_experience_required?: number | null;
  working_hours_type?: string | null;
  working_hours_from?: string | null;
  working_hours_to?: string | null;
  fees?: number | null;
  phone_contact_information?: string | null;
  email_contact_information?: string | null;
  establishment_classification?: string[];
  gender?: string[];
  nationality_id?: string | null;
  city_id?: string | null;
}

export type UpdateOpportunityRequest = Partial<Omit<CreateOpportunityRequest, 'event_id'>>;

export interface LinkOpportunityAssetRequest {
  asset_id: string;
}

export interface LinkOpportunityAssetResponse {
  id: string;
  asset_id: string;
}

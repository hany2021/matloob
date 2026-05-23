import { Opportunity } from './opportunity';

export interface EvaluationParty {
  type: 'user' | 'organization' | string;
  id: string;
  name: string | null;
  email: string | null;
}

export interface EvaluationUpload {
  id: string;
  file_name: string;
  content_type: string;
  size_bytes: number;
}

export interface Evaluation {
  id: string;
  rating: number;
  opportunity: Opportunity | null;
  offer_id: string;
  evaluator: EvaluationParty | null;
  evaluable: EvaluationParty | null;
  recommend_for_future_opportunities: boolean;
  comment: string | null;
  matching_percentage: number | null;
  success_management_criteria_comment: string | null;
  uploads: EvaluationUpload[];
  matloob_evaluation: number | null;
  created_at: string;
}

export interface EvaluationListResponse {
  data: Evaluation[];
  page: number;
  page_size: number;
  total: number;
}

export interface CreateEvaluationRequest {
  offer_id: string;
  rating: number;
  recommend_for_future_opportunities: boolean;
  comment?: string | null;
  matching_percentage?: number | null;
  success_management_criteria_comment?: string | null;
  upload_asset_ids?: string[];
}

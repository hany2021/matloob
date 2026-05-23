export type AssetVisibility = 'Private' | 'Public' | string;
export type AssetPurpose =
  | 'EstablishmentDocument'
  | 'OpportunityMedia'
  | 'EvaluationUpload'
  | 'SuccessCriterionUpload'
  | 'Other'
  | string;

export interface AssetMetadata {
  id: string;
  original_file_name: string;
  content_type: string;
  size_bytes: number;
  sha256: string;
  visibility: AssetVisibility;
  purpose: AssetPurpose;
  created_at: string;
}

export interface UploadAssetResponse extends AssetMetadata {}

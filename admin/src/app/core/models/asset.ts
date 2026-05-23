/**
 * Asset wire shapes. The purpose union mirrors the .NET
 * `AssetPurpose` enum exactly — values outside this list cause the
 * backend's model binder to reject the upload with 400.
 */
export type AssetVisibility = 'Private' | 'Public';

export type AssetPurpose =
  | 'Generic'
  | 'AuthorizationLetter'
  | 'CommercialRegistration'
  | 'LegacyMedia';

/**
 * MIME types the backend's asset upload endpoint accepts (see
 * `AllowedAssetUploadContentTypes.cs`). Reused as the default `accept=`
 * value on the picker so users can't pick a file the backend will
 * reject.
 */
export const ALLOWED_ASSET_CONTENT_TYPES =
  'application/pdf,image/jpeg,image/png';

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

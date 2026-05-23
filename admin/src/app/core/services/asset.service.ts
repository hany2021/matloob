import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../http/api-client';
import { AssetMetadata, AssetPurpose, AssetVisibility, UploadAssetResponse } from '../models/asset';

/**
 * Wraps the Local Assets API (`/api/v1/assets`). We don't use signed
 * storage URLs — uploads go through this service as a single multipart
 * POST. Callers receive back an asset id they persist on whichever
 * parent record (establishment doc, opportunity media, evaluation
 * upload) initiated the upload.
 */
@Injectable({ providedIn: 'root' })
export class AssetService {
  private readonly api = inject(ApiClient);

  upload(
    file: File,
    options: { purpose: AssetPurpose; visibility?: AssetVisibility } = { purpose: 'Other' },
  ): Observable<UploadAssetResponse> {
    const form = new FormData();
    form.append('file', file, file.name);
    form.append('purpose', options.purpose);
    if (options.visibility) {
      form.append('visibility', options.visibility);
    }
    return this.api.postForm<UploadAssetResponse>('/api/v1/assets', form);
  }

  getMetadata(id: string): Observable<AssetMetadata> {
    return this.api.get<AssetMetadata>(`/api/v1/assets/${encodeURIComponent(id)}/metadata`);
  }

  downloadUrl(id: string, apiBaseUrl: string): string {
    return `${apiBaseUrl.replace(/\/$/, '')}/api/v1/assets/${encodeURIComponent(id)}`;
  }

  delete(id: string): Observable<void> {
    return this.api.delete<void>(`/api/v1/assets/${encodeURIComponent(id)}`);
  }
}

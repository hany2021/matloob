import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from '../http/api-client';
import { APP_CONFIG } from '../config/app-config';
import { ToastService } from '../../shared/components/toast.service';
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
  private readonly http = inject(HttpClient);
  private readonly toast = inject(ToastService);

  upload(
    file: File,
    options: { purpose: AssetPurpose; visibility?: AssetVisibility } = { purpose: 'Generic' },
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

  /**
   * Fetch the raw bytes WITH auth. Private assets (establishment documents)
   * require the bearer, so a plain anchor href would 401 — we stream the blob
   * through HttpClient (the auth interceptor attaches the token) and let the
   * caller open/save it. The envelope shim leaves binary responses untouched.
   */
  downloadBlob(id: string): Observable<Blob> {
    return this.http.get(`${APP_CONFIG.apiBaseUrl}/api/v1/assets/${encodeURIComponent(id)}`, {
      responseType: 'blob',
    });
  }

  /** Open an asset inline in a new tab (PDF/image preview). Popup-blocker-safe. */
  openInNewTab(id: string): void {
    const win = window.open('', '_blank');
    this.downloadBlob(id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        if (win) win.location.href = url;
        else window.open(url, '_blank');
        setTimeout(() => URL.revokeObjectURL(url), 60_000);
      },
      error: () => {
        if (win) win.close();
        this.toast.error('Could not open the document.');
      },
    });
  }

  /** Download an asset to disk with a friendly file name. */
  saveAs(id: string, fileName: string): void {
    this.downloadBlob(id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName || id;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 60_000);
      },
      error: () => this.toast.error('Could not download the document.'),
    });
  }

  delete(id: string): Observable<void> {
    return this.api.delete<void>(`/api/v1/assets/${encodeURIComponent(id)}`);
  }
}

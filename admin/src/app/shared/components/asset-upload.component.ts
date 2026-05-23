import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, computed, inject, signal } from '@angular/core';

import { AssetService } from '../../core/services/asset.service';
import { AssetPurpose, AssetVisibility, UploadAssetResponse } from '../../core/models/asset';
import { ToastService } from './toast.service';

/**
 * Reusable file picker that uploads through the Local Assets API and
 * emits the resulting asset id. Parents are responsible for persisting
 * the id on whatever record initiated the upload (establishment doc,
 * opportunity media, evaluation upload).
 *
 * Multi-file uploads happen sequentially so failures can be reported per
 * file without leaving the picker in an indeterminate state.
 */
@Component({
  selector: 'app-asset-upload',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="asset-upload">
      <label class="btn btn-secondary">
        <input
          type="file"
          [multiple]="multiple"
          [accept]="accept"
          (change)="onFileChange($event)"
          hidden
        />
        {{ pickerLabel }}
      </label>
      <span class="muted" *ngIf="uploading()">Uploading {{ progressLabel() }}…</span>
      <ul class="uploaded-list" *ngIf="uploaded().length">
        <li *ngFor="let asset of uploaded()">
          <span>{{ asset.original_file_name }}</span>
          <span class="muted">{{ formatSize(asset.size_bytes) }}</span>
        </li>
      </ul>
    </div>
  `,
  styles: [
    `
      .asset-upload { display: flex; flex-direction: column; gap: 8px; align-items: flex-start; }
      .uploaded-list { list-style: none; padding: 0; margin: 4px 0 0; }
      .uploaded-list li { display: flex; gap: 12px; }
    `,
  ],
})
export class AssetUploadComponent {
  private readonly assetService = inject(AssetService);
  private readonly toast = inject(ToastService);

  @Input() purpose: AssetPurpose = 'Other';
  @Input() visibility?: AssetVisibility;
  @Input() multiple = false;
  @Input() accept?: string;
  @Input() pickerLabel = 'Upload file';

  @Output() readonly uploadedAsset = new EventEmitter<UploadAssetResponse>();

  readonly uploaded = signal<UploadAssetResponse[]>([]);
  readonly uploading = signal(false);
  private readonly progress = signal({ done: 0, total: 0 });
  readonly progressLabel = computed(() => {
    const { done, total } = this.progress();
    return total > 1 ? `${done + 1}/${total}` : '';
  });

  onFileChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    if (!files.length) return;
    void this.uploadAll(files);
  }

  private async uploadAll(files: File[]): Promise<void> {
    this.uploading.set(true);
    this.progress.set({ done: 0, total: files.length });
    try {
      for (const file of files) {
        try {
          const asset = await this.uploadOne(file);
          this.uploaded.update((list) => [...list, asset]);
          this.uploadedAsset.emit(asset);
        } catch (err) {
          // ProblemDetails interceptor has already toasted the technical
          // detail; surface a per-file context line here.
          this.toast.error(`Failed to upload ${file.name}`);
        }
        this.progress.update(({ done, total }) => ({ done: done + 1, total }));
      }
    } finally {
      this.uploading.set(false);
    }
  }

  private uploadOne(file: File): Promise<UploadAssetResponse> {
    return new Promise<UploadAssetResponse>((resolve, reject) => {
      this.assetService
        .upload(file, { purpose: this.purpose, visibility: this.visibility })
        .subscribe({ next: resolve, error: reject });
    });
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }
}

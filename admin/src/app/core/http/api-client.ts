import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { APP_CONFIG } from '../config/app-config';

/**
 * Thin typed wrapper around <c>HttpClient</c> that prefixes every URL
 * with <see cref="APP_CONFIG.apiBaseUrl"/>. Feature services build their
 * URL fragments from constants; this client only knows where the
 * backend lives, never the route catalogue.
 */
@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);

  get<T>(path: string, query?: Record<string, string | number | boolean | undefined | null>): Observable<T> {
    return this.http.get<T>(this.build(path), { params: this.params(query) });
  }

  post<T>(path: string, body: unknown): Observable<T> {
    return this.http.post<T>(this.build(path), body);
  }

  patch<T>(path: string, body: unknown): Observable<T> {
    return this.http.patch<T>(this.build(path), body);
  }

  put<T>(path: string, body: unknown): Observable<T> {
    return this.http.put<T>(this.build(path), body);
  }

  delete<T>(path: string): Observable<T> {
    return this.http.delete<T>(this.build(path));
  }

  postForm<T>(path: string, form: FormData): Observable<T> {
    return this.http.post<T>(this.build(path), form);
  }

  private build(path: string): string {
    return `${APP_CONFIG.apiBaseUrl}${path.startsWith('/') ? path : '/' + path}`;
  }

  private params(
    query?: Record<string, string | number | boolean | undefined | null>,
  ): HttpParams {
    let params = new HttpParams();
    if (!query) return params;
    for (const [k, v] of Object.entries(query)) {
      if (v === undefined || v === null || v === '') continue;
      params = params.set(k, String(v));
    }
    return params;
  }
}

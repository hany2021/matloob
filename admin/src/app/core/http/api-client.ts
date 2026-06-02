import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { APP_CONFIG } from '../config/app-config';

/**
 * Thin typed wrapper around <c>HttpClient</c> that prefixes every URL
 * with <see cref="APP_CONFIG.apiBaseUrl"/>. Feature services build their
 * URL fragments from constants; this client only knows where the
 * backend lives, never the route catalogue.
 *
 * Envelope: the whole API wraps every 2xx JSON body as `{ data }` (or
 * `{ data, meta, links }` for lists). This client unwraps `.data` so feature
 * services keep working with plain payload shapes. A 204 (null body) or any
 * already-unwrapped response passes through untouched.
 */
@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);

  private unwrap<T>(source: Observable<unknown>): Observable<T> {
    return source.pipe(
      map((res) =>
        res !== null && typeof res === 'object' && 'data' in (res as Record<string, unknown>)
          ? ((res as { data: T }).data)
          : (res as T),
      ),
    );
  }

  get<T>(path: string, query?: Record<string, string | number | boolean | undefined | null>): Observable<T> {
    return this.unwrap<T>(this.http.get(this.build(path), { params: this.params(query) }));
  }

  post<T>(path: string, body: unknown): Observable<T> {
    return this.unwrap<T>(this.http.post(this.build(path), body));
  }

  patch<T>(path: string, body: unknown): Observable<T> {
    return this.unwrap<T>(this.http.patch(this.build(path), body));
  }

  put<T>(path: string, body: unknown): Observable<T> {
    return this.unwrap<T>(this.http.put(this.build(path), body));
  }

  delete<T>(path: string): Observable<T> {
    return this.unwrap<T>(this.http.delete(this.build(path)));
  }

  postForm<T>(path: string, form: FormData): Observable<T> {
    return this.unwrap<T>(this.http.post(this.build(path), form));
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

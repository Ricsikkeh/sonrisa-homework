import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  Observable,
  Subscription,
  catchError,
  defer,
  finalize,
  map,
  of,
  switchMap,
  tap,
} from 'rxjs';
import { parseApiError } from '../../shared/http/api-error';

export interface EventTypeItem {
  id: string;
  name: string;
}

@Injectable()
export class EventTypesFacade {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly currentItems = signal<EventTypeItem[]>([]);
  private readonly currentLoading = signal(false);
  private readonly currentBusy = signal(false);
  private readonly currentError = signal<string | null>(null);
  private readonly denied = signal(false);
  private listRequest?: Subscription;
  readonly items = this.currentItems.asReadonly();
  readonly loading = this.currentLoading.asReadonly();
  readonly busy = this.currentBusy.asReadonly();
  readonly error = this.currentError.asReadonly();
  readonly accessDenied = this.denied.asReadonly();

  load(): void {
    if (!this.currentBusy()) this.refresh();
  }
  create(name: string): Observable<boolean> {
    return this.mutate('POST', '/api/event-types', { name });
  }
  rename(id: string, name: string): Observable<boolean> {
    return this.mutate('PUT', `/api/event-types/${encodeURIComponent(id)}`, { name });
  }
  remove(id: string): Observable<boolean> {
    return this.mutate('DELETE', `/api/event-types/${encodeURIComponent(id)}`);
  }

  private refresh(): void {
    this.listRequest?.unsubscribe();
    this.currentLoading.set(true);
    this.currentError.set(null);
    this.listRequest = this.http
      .get<unknown>('/api/event-types')
      .pipe(
        map((value) => {
          if (
            !Array.isArray(value) ||
            value.some(
              (item) =>
                item === null ||
                typeof item !== 'object' ||
                typeof item.id !== 'string' ||
                typeof item.name !== 'string',
            )
          ) {
            throw new Error('Invalid event-type list response.');
          }
          return value as EventTypeItem[];
        }),
        finalize(() => this.currentLoading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (items) => {
          this.currentItems.set(items);
          this.denied.set(false);
        },
        error: (error: unknown) => this.handleError(error),
      });
  }

  private mutate(method: string, route: string, body?: { name: string }): Observable<boolean> {
    return defer(() => {
      if (this.currentBusy() || this.currentLoading() || this.denied()) return of(false);
      this.currentBusy.set(true);
      this.currentError.set(null);
      return this.http.get<{ requestToken: string }>('/api/auth/csrf').pipe(
        switchMap(({ requestToken }) =>
          this.http.request(method, route, {
            body,
            headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }),
          }),
        ),
        tap(() => this.refresh()),
        map(() => true),
        catchError((error: unknown) => {
          this.handleError(error);
          return of(false);
        }),
        finalize(() => this.currentBusy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      );
    });
  }

  private handleError(error: unknown): void {
    const parsed = parseApiError(error);
    if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
      this.currentItems.set([]);
      this.denied.set(true);
      this.currentError.set(
        error.status === 401
          ? 'Your session expired. Sign in again to manage event types.'
          : 'Administrator access is required to manage event types.',
      );
    } else {
      const messages: Record<string, string> = {
        event_type_name_invalid: 'Enter a name of 1–100 characters without control characters.',
        event_type_name_conflict: 'An active event type with that name already exists.',
        event_type_in_use: 'This event type has subscriptions and cannot be deleted.',
        event_type_change_in_progress:
          'This event type is being used by another request. Reload or explicitly try again.',
        event_type_not_found: 'This event type no longer exists. Reload the list.',
        antiforgery_failed: 'Your session changed. Please submit the form again.',
      };
      this.currentError.set((parsed.code && messages[parsed.code]) || parsed.message);
    }
  }
}

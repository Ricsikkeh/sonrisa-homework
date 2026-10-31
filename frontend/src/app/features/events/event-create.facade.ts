import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { DestroyRef, Injectable, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  Observable,
  Subject,
  Subscription,
  catchError,
  defer,
  finalize,
  map,
  of,
  switchMap,
  takeUntil,
} from 'rxjs';
import { AuthFacade } from '../auth/auth.facade';
import { parseApiError } from '../../shared/http/api-error';
import { validEventTypeName } from '../event-types/event-type-name';
import { trimEventText, validMessage, validTitle, validUuid } from './event-content';

export interface EventDraft {
  eventTypeId: string;
  title: string;
  message: string;
}
export interface CreatedEvent extends EventDraft {
  id: string;
  eventTypeName: string;
  publishedAt: string;
}
interface PendingEvent {
  version: 1;
  ownerId: string;
  key: string;
  payload: EventDraft;
}
interface TypeItem {
  id: string;
  name: string;
}
const storageKey = 'wea.event-create.pending.v1';

function validDraft(value: unknown): value is EventDraft {
  if (value === null || typeof value !== 'object') return false;
  const draft = value as Partial<EventDraft>;
  return (
    validUuid(draft.eventTypeId) &&
    typeof draft.title === 'string' &&
    validTitle(draft.title) &&
    typeof draft.message === 'string' &&
    validMessage(draft.message)
  );
}
function validResult(value: unknown, payload: EventDraft): value is CreatedEvent {
  if (value === null || typeof value !== 'object') return false;
  const result = value as Partial<CreatedEvent>;
  return (
    validUuid(result.id) &&
    validUuid(result.eventTypeId) &&
    result.eventTypeId.toLowerCase() === payload.eventTypeId.toLowerCase() &&
    result.title === payload.title &&
    result.message === payload.message &&
    typeof result.eventTypeName === 'string' &&
    result.eventTypeName.length > 0 &&
    typeof result.publishedAt === 'string' &&
    Number.isFinite(Date.parse(result.publishedAt))
  );
}

@Injectable()
export class EventCreateFacade {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthFacade);
  private readonly destroyRef = inject(DestroyRef);
  private ownerId: string | null = null;
  private readonly currentTypes = signal<TypeItem[]>([]);
  private readonly currentPending = signal<PendingEvent | null>(null);
  private readonly currentResult = signal<CreatedEvent | null>(null);
  private readonly currentLoading = signal(false);
  private readonly currentBusy = signal(false);
  private readonly currentError = signal<string | null>(null);
  private readonly denied = signal(false);
  private readonly currentLoaded = signal(false);
  readonly typesLoaded = this.currentLoaded.asReadonly();
  private listRequest?: Subscription;
  private readonly accountChanged = new Subject<void>();
  readonly types = this.currentTypes.asReadonly();
  readonly loading = this.currentLoading.asReadonly();
  readonly busy = this.currentBusy.asReadonly();
  readonly error = this.currentError.asReadonly();
  readonly accessDenied = this.denied.asReadonly();
  readonly pending = computed(() => (this.sameAccount() ? this.currentPending() : null));
  readonly result = computed(() => (this.sameAccount() ? this.currentResult() : null));

  constructor() {
    this.syncAccount();
    effect(() => this.syncAccount());
  }

  load(): void {
    this.syncAccount();
    if (this.currentBusy() || !this.allowed()) return;
    this.listRequest?.unsubscribe();
    this.currentLoading.set(true);
    this.currentError.set(null);
    this.currentLoaded.set(false);
    this.listRequest = this.http
      .get<unknown>('/api/events/event-types')
      .pipe(
        map((value) => {
          if (
            !Array.isArray(value) ||
            value.some(
              (item) =>
                item === null ||
                typeof item !== 'object' ||
                !validUuid(item.id) ||
                typeof item.name !== 'string' ||
                !validEventTypeName(item.name),
            ) ||
            new Set(value.map((item) => item.id.toLowerCase())).size !== value.length
          )
            throw new Error('Invalid type selector response.');
          return value as TypeItem[];
        }),
        finalize(() => this.currentLoading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (types) => {
          this.currentTypes.set(types);
          this.currentLoaded.set(true);
          this.denied.set(false);
        },
        error: (error: unknown) => {
          this.currentTypes.set([]);
          this.handleError(error, false);
        },
      });
  }

  submit(input: EventDraft): Observable<boolean> {
    return defer(() => {
      this.syncAccount();
      if (
        this.currentBusy() ||
        this.currentLoading() ||
        this.denied() ||
        !this.allowed() ||
        this.currentPending() ||
        !this.currentLoaded() ||
        !validDraft(input) ||
        !this.currentTypes().some((type) => type.id === input.eventTypeId)
      )
        return of(false);
      const pending: PendingEvent = {
        version: 1,
        ownerId: this.ownerId!,
        key: crypto.randomUUID(),
        payload: {
          eventTypeId: input.eventTypeId,
          title: trimEventText(input.title),
          message: trimEventText(input.message),
        },
      };
      try {
        sessionStorage.setItem(storageKey, JSON.stringify(pending));
      } catch {
        this.currentError.set(
          'Unable to save retry information in this tab. Enable session storage before creating an event.',
        );
        return of(false);
      }
      this.currentPending.set(pending);
      return this.send(pending);
    });
  }

  checkSession(): void {
    if (!this.currentBusy()) this.auth.restore();
  }

  retry(): Observable<boolean> {
    return defer(() => {
      this.syncAccount();
      const pending = this.pending();
      if (
        !pending ||
        this.currentBusy() ||
        this.currentLoading() ||
        this.denied() ||
        !this.allowed()
      )
        return of(false);
      return this.send(pending);
    });
  }

  // UI must require explicit confirmation: an unresolved request may already have succeeded.
  discardPending(): EventDraft | null {
    if (this.currentBusy()) return null;
    const payload = this.pending()?.payload ?? null;
    this.currentPending.set(null);
    this.currentResult.set(null);
    this.currentError.set(null);
    this.clearStorage();
    return payload;
  }

  private send(pending: PendingEvent): Observable<boolean> {
    this.currentBusy.set(true);
    this.currentResult.set(null);
    this.currentError.set(null);
    // An explicit retry uses the captured immutable payload, never the current form values.
    return this.http.get<{ requestToken: string }>('/api/auth/csrf').pipe(
      switchMap(({ requestToken }) => {
        if (!this.sameAccount() || !this.allowed()) throw new Error('Session changed.');
        return this.http.post<unknown>('/api/events', pending.payload, {
          headers: new HttpHeaders({
            'X-XSRF-TOKEN': requestToken,
            'Idempotency-Key': pending.key,
            'X-Expected-User-Id': pending.ownerId,
          }),
        });
      }),
      map((value) => {
        if (!this.sameAccount() || !validResult(value, pending.payload))
          throw new Error('Invalid creation response.');
        this.currentResult.set(value);
        this.currentPending.set(null);
        this.clearStorage();
        return true;
      }),
      catchError((error: unknown) => {
        this.handleError(error, true);
        return of(false);
      }),
      finalize(() => this.currentBusy.set(false)),
      takeUntil(this.accountChanged),
      takeUntilDestroyed(this.destroyRef),
    );
  }

  private sameAccount(): boolean {
    return this.auth.status() === 'authenticated' && this.auth.user()?.id === this.ownerId;
  }
  private allowed(): boolean {
    const roles = this.auth.user()?.roles;
    return this.sameAccount() && !!(roles?.includes('Editor') || roles?.includes('Administrator'));
  }
  private syncAccount(): void {
    const status = this.auth.status();
    if (status === 'loading') return;
    const owner = status === 'authenticated' ? (this.auth.user()?.id ?? null) : null;
    if (owner === this.ownerId) return;
    this.listRequest?.unsubscribe();
    this.accountChanged.next();
    this.ownerId = owner;
    this.currentLoaded.set(false);
    this.currentTypes.set([]);
    this.currentPending.set(null);
    this.currentResult.set(null);
    this.currentError.set(null);
    this.denied.set(false);
    if (!owner) return;
    try {
      const raw = sessionStorage.getItem(storageKey);
      if (!raw) return;
      const saved: unknown = JSON.parse(raw);
      if (saved !== null && typeof saved === 'object') {
        const pending = saved as Partial<PendingEvent>;
        if (
          pending.version === 1 &&
          pending.ownerId === owner &&
          validUuid(pending.key) &&
          validDraft(pending.payload) &&
          pending.payload.title === trimEventText(pending.payload.title) &&
          pending.payload.message === trimEventText(pending.payload.message)
        ) {
          this.currentPending.set(pending as PendingEvent);
          this.currentError.set(
            'A previous creation may have succeeded. Retry the saved request to recover its result.',
          );
          return;
        }
      }
      this.clearStorage();
    } catch {
      this.clearStorage();
    }
  }
  private clearStorage(): void {
    try {
      sessionStorage.removeItem(storageKey);
    } catch {
      /* Duplicate protection remains on the server. */
    }
  }
  private handleError(error: unknown, mutation: boolean): void {
    if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
      this.denied.set(true);
      this.currentTypes.set([]);
      this.currentLoaded.set(false);
      this.currentError.set(
        error.status === 401
          ? 'Your session expired. Sign in again before retrying this saved request.'
          : 'Editor or administrator access is required.',
      );
      return;
    }
    const parsed = parseApiError(error);
    if (parsed.code === 'event_session_changed') {
      this.denied.set(true);
      this.currentTypes.set([]);
      this.currentLoaded.set(false);
      this.currentError.set(
        'The signed-in account changed. Check the current session, then reload available types before continuing. The saved request was not submitted under another account.',
      );
      return;
    }
    const messages: Record<string, string> = {
      event_type_unavailable:
        'The selected event type is unavailable. Reload available types; confirm starting a different event to change the saved request.',
      event_validation_failed:
        'Check the event type, title (1–200), and message (1–4,000 characters). Confirm starting a different event to edit the saved request.',
      idempotency_key_conflict:
        'This request key was already used with different content. Do not retry changed content with this key.',
      idempotency_key_invalid: 'The saved request key is invalid. No automatic retry was made.',
      idempotency_request_in_progress:
        'Creation is temporarily busy. Wait briefly, then retry the same saved request.',
      antiforgery_failed:
        'Your session changed. Retry the saved request with a fresh session token.',
    };
    this.currentError.set(
      (parsed.code && messages[parsed.code]) ||
        (mutation
          ? 'Creation may have succeeded. Retry the saved request to recover the result without creating a duplicate.'
          : parsed.message),
    );
  }
}

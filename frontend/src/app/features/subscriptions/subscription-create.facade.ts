import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { DestroyRef, Injector, Injectable, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import {
  Observable,
  Subject,
  Subscription,
  catchError,
  defer,
  filter,
  finalize,
  forkJoin,
  map,
  of,
  switchMap,
  take,
  takeUntil,
} from 'rxjs';
import { AuthFacade } from '../auth/auth.facade';
import { parseApiError } from '../../shared/http/api-error';
import { validUuid } from '../events/event-content';
import { validEventTypeName } from '../event-types/event-type-name';

interface EventType {
  id: string;
  name: string;
}
interface Destinations {
  email: string | null;
  slackLabel: string | null;
}
type Channel = 'email' | 'slack';
export interface CreatedSubscription {
  id: string;
  eventTypeId: string;
  eventTypeName: string;
  channels: Channel[];
  createdAt: string;
}
function eventTypesValid(value: unknown): value is EventType[] {
  return (
    Array.isArray(value) &&
    value.every(
      (item) =>
        !!item &&
        typeof item === 'object' &&
        validUuid(item.id) &&
        typeof item.name === 'string' &&
        validEventTypeName(item.name),
    ) &&
    new Set(value.map((item) => item.id.toLowerCase())).size === value.length
  );
}
function destinationsValid(value: unknown): value is Destinations {
  if (!value || typeof value !== 'object') return false;
  const item = value as Partial<Destinations>;
  return (
    (item.email === null ||
      (typeof item.email === 'string' &&
        item.email.trim().length > 0 &&
        item.email.length <= 254)) &&
    (item.slackLabel === null ||
      (typeof item.slackLabel === 'string' &&
        item.slackLabel.trim().length > 0 &&
        item.slackLabel.length <= 100))
  );
}
function createdValid(
  value: unknown,
  type: string,
  channels: Channel[],
): value is CreatedSubscription {
  if (!value || typeof value !== 'object') return false;
  const item = value as Partial<CreatedSubscription>;
  return (
    validUuid(item.id) &&
    validUuid(item.eventTypeId) &&
    item.eventTypeId.toLowerCase() === type.toLowerCase() &&
    typeof item.eventTypeName === 'string' &&
    validEventTypeName(item.eventTypeName) &&
    Array.isArray(item.channels) &&
    item.channels.length === channels.length &&
    new Set(item.channels).size === channels.length &&
    item.channels.every((channel) => channels.includes(channel)) &&
    typeof item.createdAt === 'string' &&
    Number.isFinite(Date.parse(item.createdAt))
  );
}

@Injectable()
export class SubscriptionCreateFacade {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthFacade);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly owner = signal<string | null>(null);
  private readonly typesState = signal<EventType[]>([]);
  private readonly destinationsState = signal<Destinations | null>(null);
  private readonly selectedState = signal('');
  private readonly emailState = signal(false);
  private readonly slackState = signal(false);
  private readonly loadedState = signal(false);
  private readonly loadingState = signal(false);
  private readonly busyState = signal(false);
  private readonly deniedState = signal(false);
  private readonly errorState = signal<string | null>(null);
  private readonly resultState = signal<CreatedSubscription | null>(null);
  private readonly accountChanged = new Subject<void>();
  private request?: Subscription;
  private sessionRequest?: Subscription;
  readonly loading = this.loadingState.asReadonly();
  readonly busy = this.busyState.asReadonly();
  readonly denied = this.deniedState.asReadonly();
  readonly error = this.errorState.asReadonly();
  readonly eventTypes = computed(() => (this.sameAccount() ? this.typesState() : []));
  readonly hasEmail = computed(() => this.sameAccount() && !!this.destinationsState()?.email);
  readonly hasSlack = computed(() => this.sameAccount() && !!this.destinationsState()?.slackLabel);
  readonly loaded = computed(() => this.sameAccount() && this.loadedState() && !this.loading());
  readonly selectedType = computed(() => (this.sameAccount() ? this.selectedState() : ''));
  readonly emailSelected = computed(() => this.sameAccount() && this.emailState());
  readonly slackSelected = computed(() => this.sameAccount() && this.slackState());
  readonly canCreate = computed(
    () =>
      this.canEdit() &&
      this.eventTypes().some((type) => type.id === this.selectedType()) &&
      (this.emailSelected() || this.slackSelected()) &&
      (!this.emailSelected() || this.hasEmail()) &&
      (!this.slackSelected() || this.hasSlack()),
  );
  readonly result = computed(() => (this.sameAccount() ? this.resultState() : null));

  constructor() {
    effect(() => {
      if (this.owner() !== null && !this.sameAccount())
        this.invalidate(
          'Your session changed. Restore your session and reload before creating a subscription.',
        );
    });
  }
  setType(value: string): void {
    if (this.canEdit() && (value === '' || this.typesState().some((type) => type.id === value))) {
      this.selectedState.set(value);
      this.resultState.set(null);
    }
  }
  setEmail(value: boolean): void {
    if (this.canEdit() && (!value || this.hasEmail())) {
      this.emailState.set(value);
      this.resultState.set(null);
    }
  }
  setSlack(value: boolean): void {
    if (this.canEdit() && (!value || this.hasSlack())) {
      this.slackState.set(value);
      this.resultState.set(null);
    }
  }

  load(): void {
    if (
      this.loading() ||
      this.busy() ||
      this.denied() ||
      this.auth.status() !== 'authenticated' ||
      !this.auth.user()
    )
      return;
    const owner = this.auth.user()!.id;
    this.owner.set(owner);
    this.loadedState.set(false);
    this.loadingState.set(true);
    this.errorState.set(null);
    this.resultState.set(null);
    const headers = new HttpHeaders({ 'X-Expected-User-Id': owner });
    this.request = forkJoin({
      types: this.http.get<unknown>('/api/subscriptions/event-types', { headers }),
      destinations: this.http.get<unknown>('/api/me/destinations', { headers }),
    })
      .pipe(
        map(({ types, destinations }) => {
          if (!this.sameAccount() || !eventTypesValid(types) || !destinationsValid(destinations))
            throw new Error('Invalid subscription form response.');
          return { types, destinations };
        }),
        finalize(() => this.loadingState.set(false)),
        takeUntil(this.accountChanged),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ types, destinations }) => {
          this.typesState.set(types);
          this.destinationsState.set(destinations);
          this.loadedState.set(true);
        },
        error: (error: unknown) => {
          this.loadedState.set(false);
          this.typesState.set([]);
          this.destinationsState.set(null);
          this.handleError(error, false);
        },
      });
  }

  reload(): void {
    if (this.loading() || this.busy()) return;
    this.invalidate(null);
    this.sessionRequest?.unsubscribe();
    this.loadingState.set(true);
    this.auth.restore();
    this.sessionRequest = toObservable(this.auth.status, { injector: this.injector })
      .pipe(
        filter((status) => status !== 'loading'),
        take(1),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((status) => {
        this.loadingState.set(false);
        if (status === 'authenticated' && this.auth.user()) {
          this.deniedState.set(false);
          this.load();
        } else this.errorState.set('Unable to restore your session. Sign in, then reload.');
      });
  }

  create(): Observable<boolean> {
    return defer(() => {
      if (this.owner() !== null && !this.sameAccount())
        this.invalidate('Your session changed. Restore your session and reload.');
      if (!this.canCreate()) return of(false);
      const owner = this.owner()!;
      const type = this.selectedType();
      const channels: Channel[] = [];
      if (this.emailSelected()) channels.push('email');
      if (this.slackSelected()) channels.push('slack');
      let sent = false;
      this.busyState.set(true);
      this.errorState.set(null);
      this.resultState.set(null);
      return this.http.get<{ requestToken: string }>('/api/auth/csrf').pipe(
        switchMap(({ requestToken }) => {
          if (!this.sameAccount() || typeof requestToken !== 'string' || !requestToken)
            throw new Error('Session unavailable.');
          sent = true;
          return this.http.post<unknown>(
            '/api/subscriptions',
            { eventTypeId: type, channels },
            {
              headers: new HttpHeaders({
                'X-XSRF-TOKEN': requestToken,
                'X-Expected-User-Id': owner,
              }),
            },
          );
        }),
        map((value) => {
          if (!this.sameAccount() || !createdValid(value, type, channels))
            throw new Error('Invalid subscription response.');
          this.resultState.set(value);
          this.selectedState.set('');
          this.emailState.set(false);
          this.slackState.set(false);
          return true;
        }),
        catchError((error: unknown) => {
          this.handleError(error, sent);
          return of(false);
        }),
        finalize(() => this.busyState.set(false)),
        takeUntil(this.accountChanged),
        takeUntilDestroyed(this.destroyRef),
      );
    });
  }

  private canEdit(): boolean {
    return this.loaded() && !this.busy() && !this.denied();
  }
  private sameAccount(): boolean {
    return (
      this.owner() !== null &&
      this.auth.status() === 'authenticated' &&
      this.auth.user()?.id === this.owner()
    );
  }
  private invalidate(message: string | null): void {
    this.accountChanged.next();
    this.request?.unsubscribe();
    this.owner.set(null);
    this.loadedState.set(false);
    this.typesState.set([]);
    this.destinationsState.set(null);
    this.selectedState.set('');
    this.emailState.set(false);
    this.slackState.set(false);
    this.resultState.set(null);
    this.deniedState.set(true);
    this.errorState.set(message);
  }
  private handleError(error: unknown, sent: boolean): void {
    const parsed = parseApiError(error);
    if (
      (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) ||
      parsed.code === 'subscription_session_changed' ||
      parsed.code === 'destination_session_changed'
    ) {
      this.invalidate('Your session changed or expired. Restore your session and reload.');
      return;
    }
    if (parsed.code === 'subscription_already_exists')
      this.errorState.set(
        'A subscription for this event type already exists. The channels selected here have not been confirmed or changed.',
      );
    else if (parsed.code === 'event_type_unavailable' || parsed.code === 'destination_required') {
      this.loadedState.set(false);
      this.errorState.set(
        parsed.code === 'event_type_unavailable'
          ? 'This event type is no longer available. Reload and select an active type.'
          : 'A selected destination is no longer configured. Open destination settings or reload before creating a subscription.',
      );
    } else if (parsed.code === 'subscription_create_in_progress')
      this.errorState.set('Another request is changing related data. Reload or explicitly retry.');
    else if (parsed.code === 'subscription_validation_failed')
      this.errorState.set(
        'Select an active event type and at least one supported channel. Your subscription was not created.',
      );
    else if (parsed.code === 'antiforgery_failed')
      this.errorState.set(
        'Your session changed. Restore your session and reload before submitting again.',
      );
    else if (sent)
      this.errorState.set(
        'Unable to confirm creation. A subscription may already exist. An explicit retry can return an already-exists conflict; no channel changes will be applied.',
      );
    else this.errorState.set(parsed.message);
  }
}

import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { DestroyRef, Injectable, Injector, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import {
  Observable,
  Subject,
  Subscription,
  catchError,
  defer,
  filter,
  finalize,
  map,
  of,
  switchMap,
  take,
  takeUntil,
} from 'rxjs';
import { AuthFacade } from '../auth/auth.facade';
import { parseApiError } from '../../shared/http/api-error';

interface Destinations {
  email: string | null;
  slackLabel: string | null;
}
interface DestinationDraft {
  email: string;
  slackLabel: string;
}
const emptyDraft: DestinationDraft = { email: '', slackLabel: '' };
const controls = /[\u0000-\u001f\u007f-\u009f]/u;
function normalized(draft: DestinationDraft): Destinations {
  return { email: draft.email.trim() || null, slackLabel: draft.slackLabel.trim() || null };
}
function valid(value: Destinations): boolean {
  return (
    (value.email === null ||
      (value.email.length <= 254 &&
        !controls.test(value.email) &&
        /^[^@\s<>]+@[^@\s<>]+$/u.test(value.email))) &&
    (value.slackLabel === null ||
      (value.slackLabel.length <= 100 && !controls.test(value.slackLabel)))
  );
}
function isSettings(value: unknown): value is Destinations {
  if (!value || typeof value !== 'object') return false;
  const item = value as Partial<Destinations>;
  return (
    (item.email === null || typeof item.email === 'string') &&
    (item.slackLabel === null || typeof item.slackLabel === 'string') &&
    valid(item as Destinations)
  );
}

@Injectable()
export class DestinationsFacade {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthFacade);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly owner = signal<string | null>(null);
  private readonly currentDraft = signal<DestinationDraft>(emptyDraft);
  private readonly loaded = signal(false);
  private readonly denied = signal(false);
  private readonly currentLoading = signal(false);
  private readonly currentBusy = signal(false);
  private readonly currentError = signal<string | null>(null);
  private readonly currentSaved = signal(false);
  private readonly accountChanged = new Subject<void>();
  private loadRequest?: Subscription;
  private sessionRequest?: Subscription;
  readonly loading = this.currentLoading.asReadonly();
  readonly busy = this.currentBusy.asReadonly();
  readonly error = this.currentError.asReadonly();
  readonly saved = computed(() => this.sameAccount() && this.currentSaved());
  readonly accessDenied = this.denied.asReadonly();
  readonly draft = computed(() => (this.sameAccount() ? this.currentDraft() : emptyDraft));
  readonly editable = computed(
    () => this.sameAccount() && this.loaded() && !this.denied() && !this.loading() && !this.busy(),
  );
  readonly valid = computed(() => valid(normalized(this.draft())));

  constructor() {
    effect(() => {
      if (this.owner() !== null && !this.sameAccount())
        this.invalidate('Your session changed. Check your session and reload settings.');
    });
  }

  setEmail(value: string): void {
    this.edit('email', value);
  }
  setSlackLabel(value: string): void {
    this.edit('slackLabel', value);
  }
  private edit(field: keyof DestinationDraft, value: string): void {
    if (!this.editable()) return;
    this.currentDraft.update((draft) => ({ ...draft, [field]: value }));
    this.currentSaved.set(false);
  }

  load(): void {
    if (
      this.busy() ||
      this.loading() ||
      this.denied() ||
      this.auth.status() !== 'authenticated' ||
      !this.auth.user()
    )
      return;
    const owner = this.auth.user()!.id;
    this.owner.set(owner);
    this.loaded.set(false);
    this.currentLoading.set(true);
    this.currentError.set(null);
    this.currentSaved.set(false);
    this.loadRequest = this.http
      .get<unknown>('/api/me/destinations', {
        headers: new HttpHeaders({ 'X-Expected-User-Id': owner }),
      })
      .pipe(
        map((settings) => {
          if (!this.sameAccount() || !isSettings(settings))
            throw new Error('Invalid settings response.');
          return settings;
        }),
        finalize(() => this.currentLoading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (settings) => {
          this.currentDraft.set({
            email: settings.email ?? '',
            slackLabel: settings.slackLabel ?? '',
          });
          this.loaded.set(true);
        },
        error: (error: unknown) => this.handleError(error, false),
      });
  }

  // Explicit action: re-establish the current account before displaying or editing settings.
  reload(): void {
    if (this.busy() || this.loading()) return;
    this.invalidate(null);
    this.sessionRequest?.unsubscribe();
    this.currentLoading.set(true);
    this.auth.restore();
    this.sessionRequest = toObservable(this.auth.status, { injector: this.injector })
      .pipe(
        filter((status) => status !== 'loading'),
        take(1),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((status) => {
        this.currentLoading.set(false);
        if (status === 'authenticated' && this.auth.user()) {
          this.denied.set(false);
          this.load();
        } else
          this.currentError.set('Unable to restore your session. Sign in, then reload settings.');
      });
  }

  save(): Observable<boolean> {
    return defer(() => {
      if (this.owner() !== null && !this.sameAccount())
        this.invalidate('Your session changed. Check your session and reload settings.');
      if (!this.editable() || !this.valid()) return of(false);
      const owner = this.owner()!;
      const payload = normalized(this.currentDraft());
      let sent = false;
      this.currentBusy.set(true);
      this.currentSaved.set(false);
      this.currentError.set(null);
      return this.http.get<{ requestToken: string }>('/api/auth/csrf').pipe(
        switchMap(({ requestToken }) => {
          if (!this.sameAccount() || typeof requestToken !== 'string' || !requestToken)
            throw new Error('Session unavailable.');
          sent = true;
          return this.http.put<unknown>('/api/me/destinations', payload, {
            headers: new HttpHeaders({
              'X-XSRF-TOKEN': requestToken,
              'X-Expected-User-Id': owner,
            }),
          });
        }),
        map((settings) => {
          if (
            !this.sameAccount() ||
            !isSettings(settings) ||
            settings.email !== payload.email ||
            settings.slackLabel !== payload.slackLabel
          )
            throw new Error('Invalid save response.');
          this.currentDraft.set({
            email: settings.email ?? '',
            slackLabel: settings.slackLabel ?? '',
          });
          this.currentSaved.set(true);
          return true;
        }),
        catchError((error: unknown) => {
          this.handleError(error, sent);
          return of(false);
        }),
        finalize(() => this.currentBusy.set(false)),
        takeUntil(this.accountChanged),
        takeUntilDestroyed(this.destroyRef),
      );
    });
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
    this.loadRequest?.unsubscribe();
    this.owner.set(null);
    this.loaded.set(false);
    this.denied.set(true);
    this.currentDraft.set(emptyDraft);
    this.currentSaved.set(false);
    this.currentError.set(message);
  }
  private handleError(error: unknown, sent: boolean): void {
    const parsed = parseApiError(error);
    if (
      (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) ||
      parsed.code === 'destination_session_changed'
    ) {
      this.invalidate('Your session changed or expired. Check your session and reload settings.');
      return;
    }
    if (parsed.code === 'destination_validation_failed')
      this.currentError.set(
        'Check the email address and Slack label limits. Your changes were not saved.',
      );
    else if (parsed.code === 'destination_save_in_progress')
      this.currentError.set(
        'Your settings are being changed by another request. Reload or explicitly submit again.',
      );
    else if (parsed.code === 'antiforgery_failed')
      this.currentError.set('Your session changed. Reload your session before submitting again.');
    else if (sent)
      this.currentError.set(
        'Unable to confirm the save. Your changes may already be saved. Reload settings to check, or explicitly submit again.',
      );
    else this.currentError.set(parsed.message);
  }
}

import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
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

export interface AuthUser {
  id: string;
  userName: string;
  roles: string[];
}
export type AuthStatus = 'loading' | 'authenticated' | 'anonymous' | 'unavailable';

@Injectable({ providedIn: 'root' })
export class AuthFacade {
  private readonly http = inject(HttpClient);
  private readonly currentUser = signal<AuthUser | null>(null);
  private readonly currentStatus = signal<AuthStatus>('loading');
  private readonly submitting = signal(false);
  private readonly currentError = signal<string | null>(null);
  private restoreRequest?: Subscription;
  private restorationStarted = false;
  readonly user = this.currentUser.asReadonly();
  readonly status = this.currentStatus.asReadonly();
  readonly busy = this.submitting.asReadonly();
  readonly error = this.currentError.asReadonly();

  ensureRestored(): void {
    if (!this.restorationStarted) this.restore();
  }

  restore(): void {
    this.restorationStarted = true;
    this.restoreRequest?.unsubscribe();
    this.currentStatus.set('loading');
    this.restoreRequest = this.http.get<AuthUser>('/api/auth/me').subscribe({
      next: (user) => {
        this.currentUser.set(user);
        this.currentStatus.set('authenticated');
      },
      error: (error: unknown) => {
        this.currentUser.set(null);
        this.currentStatus.set(
          error instanceof HttpErrorResponse && error.status === 401 ? 'anonymous' : 'unavailable',
        );
      },
    });
  }

  register(userName: string, password: string): Observable<boolean> {
    return this.submit('register', userName, password);
  }

  login(userName: string, password: string): Observable<boolean> {
    return this.submit('login', userName, password);
  }

  private submit(
    action: 'register' | 'login',
    userName: string,
    password: string,
  ): Observable<boolean> {
    return defer(() => {
      if (this.submitting()) return of(false);
      this.submitting.set(true);
      this.currentError.set(null);
      // A fresh token handles the anonymous -> authenticated identity transition too.
      return this.http.get<{ requestToken: string }>('/api/auth/csrf').pipe(
        switchMap(({ requestToken }) =>
          this.http.post<AuthUser>(
            `/api/auth/${action}`,
            { userName, password },
            {
              headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }),
            },
          ),
        ),
        tap((user) => {
          if (action === 'login') {
            // A delayed startup /me response must not overwrite a successful login.
            this.restoreRequest?.unsubscribe();
            this.restorationStarted = true;
            this.currentUser.set(user);
            this.currentStatus.set('authenticated');
          }
        }),
        map(() => true),
        catchError((error: unknown) => {
          this.currentError.set(this.safeError(action, error));
          return of(false);
        }),
        finalize(() => this.submitting.set(false)),
      );
    });
  }

  private safeError(action: 'register' | 'login', error: unknown): string {
    const parsed = parseApiError(error);
    if (parsed.code === 'antiforgery_failed')
      return 'Your session changed. Please submit the form again.';
    if (error instanceof HttpErrorResponse) {
      if (error.status === 409 && action === 'register')
        return 'That username is unavailable. Choose another or sign in.';
      if (error.status === 401 && action === 'login')
        return 'Invalid username or password. If you recently tried several times, wait five minutes before trying again.';
      if (error.status === 400)
        return 'Check your username and password format. The password must meet the requirements below.';
    }
    return parsed.message;
  }
}

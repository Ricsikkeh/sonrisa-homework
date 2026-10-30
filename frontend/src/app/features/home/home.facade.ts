import { HttpClient } from '@angular/common/http';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

export type BackendStatus = 'loading' | 'available' | 'unavailable';

@Injectable()
export class HomeFacade {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly backendStatus = signal<BackendStatus>('loading');
  readonly status = this.backendStatus.asReadonly();

  checkAvailability(): void {
    this.backendStatus.set('loading');
    this.http
      .get<{ status?: string } | null>('/api/health', { timeout: 5000 })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) =>
          this.backendStatus.set(response?.status === 'ok' ? 'available' : 'unavailable'),
        error: () => this.backendStatus.set('unavailable'),
      });
  }
}

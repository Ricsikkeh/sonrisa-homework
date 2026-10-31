import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('App', () => {
  for (const roles of [[], ['Editor'], ['Administrator']]) {
    it(`shows role-appropriate event creation navigation (${roles})`, async () => {
      await TestBed.configureTestingModule({
        imports: [App],
        providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
      }).compileComponents();
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();
      const http = TestBed.inject(HttpTestingController);
      http.expectOne('/api/auth/me').flush({ id: 'a', userName: 'alice', roles });
      fixture.detectChanges();
      const menu = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
        'button[aria-label="Administration"]',
      );
      expect(menu !== null).toBe(roles.includes('Administrator'));
      const page = fixture.nativeElement as HTMLElement;
      expect(page.querySelector('a[href="/events/create"]') !== null).toBe(
        roles.includes('Editor') && !roles.includes('Administrator'),
      );
      if (menu) {
        menu.click();
        fixture.detectChanges();
        expect(document.querySelector('a[href="/admin/event-types"]')).not.toBeNull();
        expect(document.querySelector('a[href="/events/create"]')).not.toBeNull();
      } else if (roles.includes('Editor')) {
        expect(page.querySelector('a[href="/events/create"]')).not.toBeNull();
        expect(page.querySelector('button[aria-label="Administration"]')).toBeNull();
      }
      http.verify();
    });
  }
  it('renders anonymous navigation and offers retry when session restoration is unavailable', async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/auth/me').error(new ProgressEvent('error'));
    fixture.detectChanges();
    const page: HTMLElement = fixture.nativeElement;
    expect(page.textContent).toContain('Unable to check your session');
    page.querySelector<HTMLButtonElement>('nav button')!.click();
    http.expectOne('/api/auth/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    expect(page.querySelector('a[href="/login"]')).not.toBeNull();
    expect(page.querySelector('a[href="/register"]')).not.toBeNull();
    http.verify();
  });
  it('renders the application shell and route outlet', async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const page: HTMLElement = fixture.nativeElement;
    expect(page.querySelector('mat-toolbar')?.textContent).toContain('World Event Alerts');
    expect(page.querySelector('main router-outlet')).not.toBeNull();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/auth/me').flush({ id: 'u1', userName: 'alice', roles: [] });
    fixture.detectChanges();
    expect(page.textContent).toContain('Signed in as alice');
    expect(page.querySelector('a[href="/login"]')).toBeNull();
    http.verify();
  });
});

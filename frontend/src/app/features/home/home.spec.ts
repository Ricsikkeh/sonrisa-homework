import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Home } from './home';

describe('Home', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Home],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('shows loading followed by backend availability and identifies this as a scaffold', () => {
    const fixture = TestBed.createComponent(Home);
    fixture.detectChanges();
    const page: HTMLElement = fixture.nativeElement;
    expect(page.querySelector('h1')?.textContent).toContain('World event alerts');
    expect(page.textContent).toContain('No real notifications are sent');
    expect(page.querySelector('[role="status"]')?.textContent).toContain('Checking backend');

    http.expectOne('/api/health').flush({ status: 'ok' });
    fixture.detectChanges();
    expect(page.querySelector('[role="status"]')?.textContent).toContain('Backend available');
  });

  it('shows a safe failure summary and lets the user retry', () => {
    const fixture = TestBed.createComponent(Home);
    fixture.detectChanges();
    http.expectOne('/api/health').flush('private server diagnostic', {
      status: 503,
      statusText: 'Service Unavailable',
    });
    fixture.detectChanges();
    const page: HTMLElement = fixture.nativeElement;
    expect(page.querySelector('[role="status"]')?.textContent).toContain('Backend unavailable');
    expect(page.textContent).not.toContain('private server diagnostic');

    const button = page.querySelector('button');
    expect(button?.textContent).toContain('Check again');
    button?.click();
    fixture.detectChanges();
    expect(button?.disabled).toBe(true);
    http.expectOne('/api/health').flush({ status: 'ok' });
    fixture.detectChanges();
    expect(page.querySelector('[role="status"]')?.textContent).toContain('Backend available');
    expect(button?.disabled).toBe(false);
  });
});

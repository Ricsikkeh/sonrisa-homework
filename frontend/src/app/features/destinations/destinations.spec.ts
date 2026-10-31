import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthFacade } from '../auth/auth.facade';
import { destinationOptions } from './destination-options';
import { Destinations } from './destinations';

describe('Catalog-driven destination form', () => {
  async function setup() {
    await TestBed.configureTestingModule({
      imports: [Destinations],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthFacade).restore();
    http.expectOne('/api/auth/me').flush({ id: 'owner-a', userName: 'alice', roles: [] });
    const fixture = TestBed.createComponent(Destinations);
    fixture.detectChanges();
    http.expectOne('/api/me/destinations').flush({ email: null, slackLabel: null });
    fixture.detectChanges();
    await fixture.whenStable();
    return { fixture, http, page: fixture.nativeElement as HTMLElement };
  }

  it('renders every option with its own label, hint, input metadata and draft binding', async () => {
    const { fixture, http, page } = await setup();
    expect(page.querySelectorAll('input').length).toBe(destinationOptions.length);
    for (const option of destinationOptions) {
      const input = page.querySelector<HTMLInputElement>(`input[name="${option.field}"]`)!;
      expect(input.type).toBe(option.inputType);
      expect(input.maxLength).toBe(option.maxLength);
      expect(input.autocomplete).toBe(option.autocomplete);
      expect(input.disabled).toBe(false);
      expect(page.textContent).toContain(option.fieldLabel);
      expect(page.textContent).toContain(option.hint);
      input.value = option.channel === 'email' ? 'alice@example.com' : '#weather';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(fixture.componentInstance.facade.draft()[option.field]).toBe(input.value);
    }
    page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'fresh' });
    const save = http.expectOne('/api/me/destinations');
    expect(save.request.body).toEqual({ email: 'alice@example.com', slackLabel: '#weather' });
    save.flush(save.request.body);
    http.verify();
  });

  it('shows option-specific validation feedback and prevents invalid writes', async () => {
    const { fixture, http, page } = await setup();
    fixture.componentInstance.facade.setDestination('slackLabel', 'x'.repeat(101));
    fixture.detectChanges();
    expect(page.querySelector('[role="alert"]')?.textContent).toContain(
      destinationOptions[1].validationMessage,
    );
    expect(page.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);
    page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    http.expectNone('/api/auth/csrf');
    http.verify();
  });
});

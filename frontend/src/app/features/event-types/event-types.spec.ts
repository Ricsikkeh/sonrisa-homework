import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { EventTypes } from './event-types';

describe('Event type management', () => {
  async function setup(items: { id: string; name: string }[] = []) {
    await TestBed.configureTestingModule({
      imports: [EventTypes],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(EventTypes);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/event-types').flush(items);
    fixture.detectChanges();
    return { fixture, http, page: fixture.nativeElement as HTMLElement };
  }
  function fill(page: HTMLElement, name: string, value: string) {
    const input = page.querySelector<HTMLInputElement>(`input[name="${name}"]`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }
  it('renders an empty state and validates trimmed/control-character/length input before HTTP', async () => {
    const { fixture, http, page } = await setup();
    expect(page.textContent).toContain('No event types');
    for (const invalid of ['   ', 'bad\u007fname', 'x'.repeat(101)]) {
      fill(page, 'newName', invalid);
      page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      fixture.detectChanges();
      http.expectNone('/api/auth/csrf');
    }
    http.verify();
  });
  it('creates with trimmed name, prevents duplicates, and clears input only on success', async () => {
    const { fixture, http, page } = await setup();
    fill(page, 'newName', '  Weather  ');
    page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    fixture.detectChanges();
    expect(page.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'x' });
    const create = http.expectOne('/api/event-types');
    expect(create.request.body).toEqual({ name: 'Weather' });
    create.flush({ id: 'a', name: 'Weather' });
    http.expectOne('/api/event-types').flush([{ id: 'a', name: 'Weather' }]);
    fixture.detectChanges();
    expect(page.querySelector<HTMLInputElement>('input[name="newName"]')!.value).toBe('');
    expect(page.textContent).toContain('Weather');
    http.verify();
  });
  it('keeps a failed rename input and renders safe inline conflict feedback', async () => {
    const { fixture, http, page } = await setup([{ id: 'a', name: 'Weather' }]);
    page.querySelector<HTMLButtonElement>('button[data-action="rename"]')!.click();
    fixture.detectChanges();
    fill(page, 'renameName', 'Markets');
    page.querySelector('form[data-edit]')!.dispatchEvent(new Event('submit', { cancelable: true }));
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'x' });
    http.expectOne('/api/event-types/a').flush(
      {
        type: 'about:blank',
        title: 'Conflict',
        status: 409,
        code: 'event_type_name_conflict',
        detail: 'SECRET',
      },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();
    expect(page.querySelector<HTMLInputElement>('input[name="renameName"]')!.value).toBe('Markets');
    expect(page.textContent).toContain('already exists');
    expect(page.textContent).not.toContain('SECRET');
    http.verify();
  });
  it('shows safe load failure/retry and removes management forms after access denial', async () => {
    await TestBed.configureTestingModule({
      imports: [EventTypes],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(EventTypes);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const page: HTMLElement = fixture.nativeElement;
    http.expectOne('/api/event-types').error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(page.textContent).toContain('Unable to connect');
    page.querySelector<HTMLButtonElement>('button')!.click();
    http.expectOne('/api/event-types').flush(null, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(page.textContent).toContain('Administrator access is required');
    expect(page.querySelector('form')).toBeNull();
    http.verify();
  });
  it('requires confirmation before deleting and renders names as text, not markup', async () => {
    const { fixture, http, page } = await setup([
      { id: 'a', name: '<img src=x onerror=alert(1)>' },
    ]);
    expect(page.querySelector('img')).toBeNull();
    page.querySelector<HTMLButtonElement>('button[data-action="delete"]')!.click();
    fixture.detectChanges();
    http.expectNone('/api/auth/csrf');
    expect(page.textContent).toContain('Delete this event type?');
    page.querySelector<HTMLButtonElement>('button[data-action="confirm-delete"]')!.click();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'x' });
    const deletion = http.expectOne('/api/event-types/a');
    expect(deletion.request.method).toBe('DELETE');
    deletion.flush(null);
    http.expectOne('/api/event-types').flush([]);
    fixture.detectChanges();
    expect(page.textContent).toContain('No event types');
    http.verify();
  });
});

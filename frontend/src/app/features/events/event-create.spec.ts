import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthFacade } from '../auth/auth.facade';
import { EventCreate } from './event-create';

const id = '11111111-1111-4111-8111-111111111111';
describe('EventCreate form', () => {
  beforeEach(() => {
    sessionStorage.clear();
  });
  afterEach(() => {
    sessionStorage.clear();
  });
  async function setup() {
    await TestBed.configureTestingModule({
      imports: [EventCreate],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthFacade).restore();
    http.expectOne('/api/auth/me').flush({ id: 'owner-a', userName: 'alice', roles: ['Editor'] });
    const fixture = TestBed.createComponent(EventCreate);
    fixture.detectChanges();
    return {
      fixture,
      http,
      component: fixture.componentInstance,
      page: fixture.nativeElement as HTMLElement,
    };
  }
  it('renders loading/empty/reload and never submits an invalid form', async () => {
    const { fixture, http, component, page } = await setup();
    expect(page.textContent).toContain('Loading');
    http.expectOne('/api/events/event-types').flush([]);
    fixture.detectChanges();
    expect(page.textContent).toContain('No active event types');
    component.create();
    http.expectNone('/api/auth/csrf');
    expect(component.form.invalid).toBe(true);
    http.verify();
  });
  it('creates, locks failed input for retry, then resets after the original result is recovered', async () => {
    const { fixture, http, component, page } = await setup();
    http.expectOne('/api/events/event-types').flush([{ id, name: 'Weather' }]);
    component.form.setValue({ eventTypeId: id, title: '  Storm  ', message: ' Message ' });
    component.create();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'token' });
    const request = http.expectOne('/api/events');
    expect(request.request.body).toEqual({ eventTypeId: id, title: 'Storm', message: 'Message' });
    request.error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(component.form.disabled).toBe(true);
    expect(page.textContent).toContain('Retry saved request');
    component.retry();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'new-token' });
    http.expectOne('/api/events').flush({
      id: '22222222-2222-4222-8222-222222222222',
      eventTypeId: id,
      eventTypeName: 'Weather',
      title: 'Storm',
      message: 'Message',
      publishedAt: '2026-10-31T12:00:00Z',
    });
    fixture.detectChanges();
    expect(component.form.controls.title.value).toBe('');
    expect(page.textContent).toContain('Event created');
    expect(page.textContent).toContain('No alerts are sent');
    expect(page.querySelector('button[data-action="delete"]')).toBeNull();
    http.verify();
  });
  it('does not describe a selector failure as an empty type list', async () => {
    const { fixture, http, page } = await setup();
    http.expectOne('/api/events/event-types').flush(null, { status: 500, statusText: 'Error' });
    fixture.detectChanges();
    expect(page.querySelector('[role="alert"]')).not.toBeNull();
    expect(page.textContent).not.toContain('No active event types');
    http.verify();
  });
  it('requires confirmation before discarding an unresolved key to edit content', async () => {
    const { fixture, http, component, page } = await setup();
    http.expectOne('/api/events/event-types').flush([{ id, name: 'Weather' }]);
    component.form.setValue({ eventTypeId: id, title: 'Storm', message: 'Message' });
    component.create();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'token' });
    http.expectOne('/api/events').error(new ProgressEvent('error'));
    fixture.detectChanges();
    component.confirmDifferent.set(true);
    fixture.detectChanges();
    expect(page.textContent).toContain('may already have succeeded');
    expect(component.facade.pending()).not.toBeNull();
    component.startDifferent();
    fixture.detectChanges();
    expect(component.facade.pending()).toBeNull();
    expect(component.form.controls.title.value).toBe('Storm');
    expect(component.form.enabled).toBe(true);
    http.verify();
  });
});

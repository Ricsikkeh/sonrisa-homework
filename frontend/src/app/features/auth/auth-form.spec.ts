import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { AuthForm } from './auth-form';
import { AuthFacade } from './auth.facade';

describe('Authentication forms', () => {
  async function setup(mode: 'register' | 'login', registered = false) {
    await TestBed.configureTestingModule({
      imports: [AuthForm],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              data: { mode },
              queryParamMap: { get: () => (registered ? 'true' : null) },
            },
          },
        },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(AuthForm);
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    fixture.detectChanges();
    return {
      fixture,
      page: fixture.nativeElement as HTMLElement,
      http: TestBed.inject(HttpTestingController),
      navigate,
    };
  }

  function fill(page: HTMLElement, userName: string, password: string) {
    for (const [name, value] of [
      ['userName', userName],
      ['password', password],
    ]) {
      const input = page.querySelector<HTMLInputElement>(`input[name="${name}"]`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
  }

  it('renders accessible registration fields without role or persistent-login choices and validates before sending', async () => {
    const { fixture, page, http } = await setup('register');
    expect(page.querySelector('h1')?.textContent).toContain('Register');
    expect(page.querySelector('input[name="userName"]')?.getAttribute('autocomplete')).toBe(
      'username',
    );
    expect(page.querySelector('input[name="password"]')?.getAttribute('autocomplete')).toBe(
      'new-password',
    );
    expect(page.querySelectorAll('input').length).toBe(2);
    expect(page.querySelector('label')?.textContent).toContain('Username');
    fill(page, 'alice', 'short');
    page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    fixture.detectChanges();
    http.expectNone('/api/auth/csrf');
    expect(page.textContent).toContain('6');
    http.verify();
  });

  it('registers without signing in, clears the password, and moves to login with a success notice', async () => {
    const { fixture, page, http, navigate } = await setup('register');
    fill(page, 'alice', 'Test-password1!');
    page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    fixture.detectChanges();
    expect(page.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'token' });
    http
      .expectOne('/api/auth/register')
      .flush({ id: 'u1', userName: 'alice', roles: [] }, { status: 201, statusText: 'Created' });
    fixture.detectChanges();
    expect(page.querySelector<HTMLInputElement>('input[name="password"]')!.value).toBe('');
    expect(TestBed.inject(AuthFacade).user()).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { registered: true } });
    http.verify();
  });

  it('logs in and restores shell session state, clears password, and navigates home', async () => {
    const { fixture, page, http, navigate } = await setup('login', true);
    expect(page.textContent).toContain('Account created');
    expect(page.querySelector('input[name="password"]')?.getAttribute('autocomplete')).toBe(
      'current-password',
    );
    fill(page, 'alice', 'Test-password1!');
    page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'token' });
    const user = { id: 'u1', userName: 'alice', roles: [] };
    http.expectOne('/api/auth/login').flush(user);
    fixture.detectChanges();
    expect(TestBed.inject(AuthFacade).user()).toEqual(user);
    expect(page.querySelector<HTMLInputElement>('input[name="password"]')!.value).toBe('');
    expect(navigate).toHaveBeenCalledWith(['/']);
    http.verify();
  });

  it('shows safe inline login errors and allows a new deliberate submission', async () => {
    const { fixture, page, http } = await setup('login');
    fill(page, 'alice', 'wrong');
    page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'token' });
    http
      .expectOne('/api/auth/login')
      .flush({ title: 'secret-server-message' }, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    expect(page.querySelector('[role="alert"]')?.textContent).toContain(
      'Invalid username or password',
    );
    expect(page.textContent).not.toContain('secret-server-message');
    expect(page.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(false);
    http.verify();
  });
});

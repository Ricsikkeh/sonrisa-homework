import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthFacade } from './auth.facade';

@Component({
  selector: 'app-auth-form',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, RouterLink],
  templateUrl: './auth-form.html',
  styles: `
    :host {
      display: block;
      max-width: 28rem;
      margin: 2rem auto;
      padding: 0 1rem;
    }
    form {
      display: grid;
      gap: 1rem;
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthForm {
  readonly auth = inject(AuthFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly registering = this.route.snapshot.data['mode'] === 'register';
  readonly registered =
    !this.registering && this.route.snapshot.queryParamMap.get('registered') === 'true';
  readonly form = inject(FormBuilder).nonNullable.group({
    userName: [
      '',
      [Validators.required, Validators.maxLength(256), Validators.pattern(/^[a-zA-Z0-9@._+\-]+$/)],
    ],
    password: [
      '',
      [
        Validators.required,
        Validators.maxLength(128),
        ...(this.registering ? [Validators.minLength(6)] : []),
      ],
    ],
  });

  submit(): void {
    if (this.form.invalid || this.auth.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    const { userName, password } = this.form.getRawValue();
    const operation = this.registering
      ? this.auth.register(userName, password)
      : this.auth.login(userName, password);
    operation.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((succeeded) => {
      if (!succeeded) return;
      this.form.controls.password.reset();
      if (this.registering) {
        void this.router.navigate(['/login'], { queryParams: { registered: true } });
      } else {
        void this.router.navigate(['/']);
      }
    });
  }
}

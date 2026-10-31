import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, effect, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, ValidatorFn } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { AuthFacade } from '../auth/auth.facade';
import { validMessage, validTitle, validUuid } from './event-content';
import { EventCreateFacade } from './event-create.facade';

const titleValidator: ValidatorFn = (control) =>
  validTitle(String(control.value ?? '')) ? null : { title: true };
const messageValidator: ValidatorFn = (control) =>
  validMessage(String(control.value ?? '')) ? null : { message: true };
const typeValidator: ValidatorFn = (control) =>
  validUuid(control.value) ? null : { eventType: true };

@Component({
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
  ],
  providers: [EventCreateFacade],
  templateUrl: './event-create.html',
  styleUrl: './event-create.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EventCreate implements OnInit {
  readonly facade = inject(EventCreateFacade);
  private readonly auth = inject(AuthFacade);
  private previousOwner = this.auth.user()?.id;
  readonly confirmDifferent = signal(false);
  readonly form = new FormGroup({
    eventTypeId: new FormControl('', { nonNullable: true, validators: [typeValidator] }),
    title: new FormControl('', { nonNullable: true, validators: [titleValidator] }),
    message: new FormControl('', { nonNullable: true, validators: [messageValidator] }),
  });
  constructor() {
    effect(() => {
      const owner = this.auth.user()?.id;
      if (owner !== this.previousOwner) {
        this.form.reset();
        this.confirmDifferent.set(false);
        this.previousOwner = owner;
      }
      const pending = this.facade.pending();
      if (pending) {
        this.form.patchValue(pending.payload, { emitEvent: false });
        this.form.disable({ emitEvent: false });
      } else {
        this.form.enable({ emitEvent: false });
        if (
          !this.facade.loading() &&
          !this.facade.types().some((type) => type.id === this.form.controls.eventTypeId.value)
        )
          this.form.controls.eventTypeId.setValue('', { emitEvent: false });
      }
    });
  }
  ngOnInit(): void {
    this.facade.load();
  }
  create(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.form.disabled) return;
    this.facade.submit(this.form.getRawValue()).subscribe((success) => {
      if (success) this.resetAfterSuccess();
    });
  }
  retry(): void {
    this.facade.retry().subscribe((success) => {
      if (success) this.resetAfterSuccess();
    });
  }
  startDifferent(): void {
    if (!this.confirmDifferent()) return;
    const payload = this.facade.discardPending();
    if (!payload) return;
    this.confirmDifferent.set(false);
    this.form.enable();
    this.form.setValue({
      ...payload,
      eventTypeId: this.facade.types().some((type) => type.id === payload.eventTypeId)
        ? payload.eventTypeId
        : '',
    });
  }
  private resetAfterSuccess(): void {
    const eventTypeId = this.form.controls.eventTypeId.value;
    this.form.enable();
    this.form.reset({ eventTypeId, title: '', message: '' });
    this.confirmDifferent.set(false);
  }
}

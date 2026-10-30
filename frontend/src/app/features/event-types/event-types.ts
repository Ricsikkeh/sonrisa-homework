import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, ValidatorFn } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { trimEventTypeName, validEventTypeName } from './event-type-name';
import { EventTypeItem, EventTypesFacade } from './event-types.facade';

const nameValidator: ValidatorFn = (control) =>
  validEventTypeName(String(control.value ?? '')) ? null : { eventTypeName: true };

@Component({
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  providers: [EventTypesFacade],
  templateUrl: './event-types.html',
  styleUrl: './event-types.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EventTypes implements OnInit {
  readonly facade = inject(EventTypesFacade);
  readonly createForm = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [nameValidator] }),
  });
  readonly renameForm = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [nameValidator] }),
  });
  readonly editing = signal<string | null>(null);
  readonly deleting = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  ngOnInit(): void {
    this.facade.load();
  }
  create(): void {
    this.createForm.markAllAsTouched();
    if (this.createForm.invalid) return;
    this.notice.set(null);
    this.facade
      .create(trimEventTypeName(this.createForm.controls.name.value))
      .subscribe((success) => {
        if (success) {
          this.createForm.reset();
          this.notice.set('Event type created.');
        }
      });
  }
  edit(item: EventTypeItem): void {
    this.deleting.set(null);
    this.editing.set(item.id);
    this.renameForm.controls.name.setValue(item.name);
  }
  rename(id: string): void {
    this.renameForm.markAllAsTouched();
    if (this.renameForm.invalid) return;
    this.notice.set(null);
    this.facade
      .rename(id, trimEventTypeName(this.renameForm.controls.name.value))
      .subscribe((success) => {
        if (success) {
          this.editing.set(null);
          this.notice.set('Event type renamed.');
        }
      });
  }
  confirmDelete(id: string): void {
    this.notice.set(null);
    this.facade.remove(id).subscribe((success) => {
      if (success) {
        this.deleting.set(null);
        this.notice.set('Event type deleted.');
      }
    });
  }
}

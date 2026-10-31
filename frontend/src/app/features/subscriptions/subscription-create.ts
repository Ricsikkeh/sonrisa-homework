import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { SubscriptionCreateFacade } from './subscription-create.facade';

@Component({
  selector: 'app-subscription-create',
  imports: [
    FormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatSelectModule,
    RouterLink,
  ],
  providers: [SubscriptionCreateFacade],
  templateUrl: './subscription-create.html',
  styles: [
    'section { max-width: 42rem; margin: 1.5rem auto; padding: 0 1rem; } mat-form-field { width: 100%; } mat-checkbox { margin-right: 1rem; } [role=alert] { color: var(--mat-sys-error); }',
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SubscriptionCreate implements OnInit {
  readonly facade = inject(SubscriptionCreateFacade);
  ngOnInit(): void {
    this.facade.load();
  }
  create(): void {
    this.facade.create().subscribe();
  }
}

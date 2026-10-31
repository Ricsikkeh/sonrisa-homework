import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { DestinationsFacade } from './destinations.facade';

@Component({
  selector: 'app-destinations',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  providers: [DestinationsFacade],
  templateUrl: './destinations.html',
  styleUrl: './destinations.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Destinations implements OnInit {
  readonly facade = inject(DestinationsFacade);
  ngOnInit(): void {
    this.facade.load();
  }
  save(): void {
    this.facade.save().subscribe();
  }
}

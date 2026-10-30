import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { HomeFacade } from './home.facade';

@Component({
  selector: 'app-home',
  imports: [MatButtonModule, MatCardModule],
  providers: [HomeFacade],
  templateUrl: './home.html',
  styleUrl: './home.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Home implements OnInit {
  protected readonly facade = inject(HomeFacade);

  ngOnInit(): void {
    this.facade.checkAvailability();
  }
}

import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { AuthFacade } from './features/auth/auth.facade';

@Component({
  imports: [MatToolbarModule, MatButtonModule, RouterOutlet, RouterLink],
  selector: 'app-root',
  templateUrl: './app.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App implements OnInit {
  readonly auth = inject(AuthFacade);

  ngOnInit(): void {
    this.auth.restore();
  }
}

import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import {
  EmsUiScopeDirective,
} from '@ems-management-consultants/ui';

@Component({
  selector: 'app-root',
  imports: [EmsUiScopeDirective, RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div emsUiScope page>
      <router-outlet />
    </div>
  `,
})
export class App {}

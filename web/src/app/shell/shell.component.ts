import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import {
  EmsButtonDirective,
  EmsLogoComponent,
  EmsNavBarComponent,
  EmsNavLogoDirective,
  EmsNavTabDirective,
  EmsNavTrailingDirective,
  EmsProfileComponent,
  EmsThemeService,
} from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';

type ColorModePreference = 'system' | 'e2-light' | 'e2-dark';

const NEXT_MODE: Readonly<Record<string, ColorModePreference>> = {
  system: 'e2-light',
  'e2-light': 'e2-dark',
  'e2-dark': 'system',
};

const MODE_LABEL: Readonly<Record<string, string>> = {
  system: 'System',
  'e2-light': 'Light',
  'e2-dark': 'Dark',
};

/** Signed-in frame: top navigation, profile and theme, around the routed page. */
@Component({
  selector: 'app-shell',
  imports: [EmsButtonDirective, EmsLogoComponent, EmsNavBarComponent, EmsNavLogoDirective, EmsNavTabDirective, EmsNavTrailingDirective, EmsProfileComponent, RouterLink, RouterLinkActive, RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ems-nav-bar label="Primary">
      <span emsNavLogo><ems-logo variant="emsmc" /></span>
      <a emsNavTab routerLink="/dashboard" routerLinkActive #dashboardLink="routerLinkActive" [active]="dashboardLink.isActive">
        Dashboard
      </a>
      <!-- Placeholders for the next slices. Disabled, so nothing pretends to work yet. -->
      <button emsNavTab type="button" disabled title="Coming soon">Reports</button>
      <button emsNavTab type="button" disabled title="Coming soon">Saved views</button>
      <span emsNavTrailing class="shell__trailing">
        <button emsButton type="button" variant="flat" color="neutral" (click)="cycleTheme()">
          Theme: {{ themeLabel() }}
        </button>
        <!-- eslint-disable-next-line @angular-eslint/template/elements-content -- ems-profile renders its own content and accessible name from [name] -->
        <button ems-profile type="button" [name]="displayName()"></button>
        <button emsButton type="button" variant="flat" color="neutral" (click)="signOut()">Sign out</button>
      </span>
    </ems-nav-bar>
    <router-outlet />
  `,
  styles: `
    .shell__trailing {
      display: inline-flex;
      align-items: center;
      gap: 0.75rem;
    }
  `,
})
export class ShellComponent {
  private readonly auth = inject(AuthService);
  private readonly theme = inject(EmsThemeService);

  protected readonly displayName = computed(() => this.auth.me()?.displayName ?? '');
  protected readonly themeLabel = computed(() => MODE_LABEL[this.theme.colorMode()] ?? 'System');

  protected cycleTheme(): void {
    const next = NEXT_MODE[this.theme.colorMode()] ?? 'system';
    this.theme.setColorMode(next);
  }

  protected signOut(): void {
    this.auth.logout();
  }
}

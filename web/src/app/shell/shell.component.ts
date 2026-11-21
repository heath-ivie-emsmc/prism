import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatMenuModule } from '@angular/material/menu';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import {
  EmsButtonDirective,
  EmsIconComponent,
  EmsLogoComponent,
  EmsNavBarComponent,
  EmsNavLogoDirective,
  EmsNavTabDirective,
  EmsNavTrailingDirective,
  EmsProfileComponent,
  EmsThemeService,
} from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';

import { providerLogo as findProviderLogo } from './provider-logo';

type ColorModePreference = 'system' | 'e2-light' | 'e2-dark';

const NEXT_MODE: Readonly<Record<string, ColorModePreference>> = {
  system: 'e2-light',
  'e2-light': 'e2-dark',
  'e2-dark': 'system',
};

const MODE_ICON: Readonly<Record<string, string>> = {
  system: 'brightness_auto',
  'e2-light': 'light_mode',
  'e2-dark': 'dark_mode',
};

const MODE_LABEL: Readonly<Record<string, string>> = {
  system: 'System',
  'e2-light': 'Light',
  'e2-dark': 'Dark',
};

/** Signed-in frame: top navigation, profile and theme, around the routed page. */
@Component({
  selector: 'app-shell',
  imports: [EmsButtonDirective, EmsIconComponent, EmsLogoComponent, MatMenuModule, EmsNavBarComponent, EmsNavLogoDirective, EmsNavTabDirective, EmsNavTrailingDirective, EmsProfileComponent, RouterLink, RouterLinkActive, RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a class="skip-link" href="#main" (click)="skipToMain($event)">Skip to main content</a>
    <header>
    <ems-nav-bar label="Primary">
      <a emsNavLogo class="shell__brand" routerLink="/dashboard" [attr.aria-label]="providerName() + ' home'">
        @if (providerLogo(); as logo) {
          <!-- On a plate so a logo stays readable in any color mode: light for a logo drawn for a white page, dark for a white logo. The link carries the name, so the image has no alt. -->
          <span class="shell__logo-plate" [class.shell__logo-plate--dark]="logo.plate === 'dark'"><img class="shell__logo" [src]="logo.url" alt="" /></span>
        } @else {
          <span class="shell__brand-name">{{ providerName() }}</span>
        }
      </a>
      <a emsNavTab module routerLink="/dashboard">
        <ems-icon name="show_chart" size="sm" />
        Prism
      </a>
      <a emsNavTab routerLink="/dashboard" routerLinkActive #dashboardLink="routerLinkActive" [active]="dashboardLink.isActive">
        <ems-icon name="dashboard" size="sm" />
        Dashboard
      </a>
      <!-- Placeholders for the next slices. Disabled, so nothing pretends to work yet. -->
      <button emsNavTab type="button" disabled title="Coming soon">
        <ems-icon name="Document" size="sm" />
        Reports
      </button>
      <button emsNavTab type="button" disabled title="Coming soon">
        <ems-icon name="filter_none" size="sm" />
        Saved views
      </button>
      <span emsNavTrailing class="shell__trailing">
        <button emsButton="flat" fab color="neutral" type="button" [attr.aria-label]="'Theme: ' + themeLabel()" [title]="'Theme: ' + themeLabel()" (click)="cycleTheme()">
          <ems-icon [name]="themeIcon()" />
        </button>
        <button emsButton="flat" fab color="neutral" type="button" disabled aria-label="Notifications" title="Coming soon">
          <ems-icon name="Bell" />
        </button>
        <!-- eslint-disable-next-line @angular-eslint/template/elements-content -- ems-profile renders its own content and accessible name from [name] -->
        <button ems-profile type="button" [name]="displayName()" [matMenuTriggerFor]="account"></button>
        <mat-menu #account="matMenu">
          <button mat-menu-item type="button" (click)="signOut()">Sign out</button>
        </mat-menu>
      </span>
    </ems-nav-bar>
    </header>
    <router-outlet />
    <footer class="shell__footer">
      <span class="shell__powered">Powered by</span>
      <ems-logo variant="emsmc" />
    </footer>
  `,
  styles: `
    /* Off screen until it has keyboard focus, then the first thing on the page (WCAG 2.4.1). */
    .skip-link {
      position: absolute;
      left: 0.5rem;
      top: -4rem;
      z-index: 1000;
      padding: 0.75rem 1rem;
      border-radius: var(--ems-corner-sm, 0.5rem);
      background: var(--ems-surface-neutral-elevated);
      color: var(--ems-text-default);
      font-weight: var(--ems-font-weight-bold);
    }
    .skip-link:focus {
      top: 0.5rem;
      outline: 2px solid var(--ems-border-focus-default);
      outline-offset: 2px;
    }
    :host {
      display: flex;
      flex-direction: column;
      min-height: 100dvh;
    }
    :host > router-outlet + * {
      flex: 1 0 auto;
    }
    .shell__logo-plate {
      display: inline-flex;
      align-items: center;
      padding: 0.25rem 0.75rem;
      border-radius: var(--ems-corner-sm, 0.5rem);
      background: #fff;
    }
    .shell__logo-plate--dark {
      background: #26262b;
    }
    .shell__logo {
      display: block;
      height: 2.5rem;
      width: auto;
    }
    .shell__brand-name {
      font-weight: var(--ems-font-weight-heavy);
      max-width: 14rem;
    }
    /* Stays at the bottom of the window, or just after the content when the page is short. */
    .shell__footer {
      position: sticky;
      bottom: 0;
      z-index: 10;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 0.75rem;
      margin-top: auto;
      padding: 0.5rem 1.5rem;
      border-top: 1px solid var(--ems-border-neutral-default);
      background: var(--ems-surface-neutral-page);
      color: var(--ems-text-muted);
      font-size: var(--ems-font-size-small);
    }
    .shell__trailing {
      display: inline-flex;
      align-items: center;
      gap: 0.5rem;
    }
    /* The library profile has no dropdown affordance, so draw the chevron after it (decorative, the menu role comes from the trigger). */
    .shell__trailing [ems-profile]::after {
      content: 'keyboard_arrow_down';
      font-family: 'Material Symbols Rounded';
      font-size: 20px;
      line-height: 1;
      padding: 0 var(--ems-padding-xs, 0.25rem);
      color: var(--ems-text-subtle, currentColor);
      font-feature-settings: 'liga';
    }
  `,
})
export class ShellComponent {
  private readonly auth = inject(AuthService);
  private readonly theme = inject(EmsThemeService);

  protected readonly providerName = computed(() => this.auth.me()?.providers[0]?.name ?? 'Prism');
  protected readonly providerLogo = computed(() => findProviderLogo(this.auth.me()?.providers[0]?.name));
  protected readonly displayName = computed(() => this.auth.me()?.displayName ?? '');
  protected readonly themeLabel = computed(() => MODE_LABEL[this.theme.colorMode()] ?? 'System');

  protected readonly themeIcon = computed(() => MODE_ICON[this.theme.colorMode()] ?? 'brightness_auto');

  protected cycleTheme(): void {
    const next = NEXT_MODE[this.theme.colorMode()] ?? 'system';
    this.theme.setColorMode(next);
  }

  /** A hash link would reload the app because of the base href, so move focus to the page's main region directly. */
  protected skipToMain(event: Event): void {
    event.preventDefault();
    const main = document.querySelector<HTMLElement>('main');
    if (main) {
      main.tabIndex = -1;
      main.focus();
    }
  }

  protected signOut(): void {
    this.auth.logout();
  }
}

import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { AuthService } from '../auth/auth.service';

interface NavItem {
  readonly label: string;
  readonly path: string;
  /** Exact match for the overview, so it is not "active" on every admin page. */
  readonly exact: boolean;
  /** The tier the user lacks, in plain words, when the area is shown but not open to them. */
  readonly needs: string | null;
}

/** The frame around every admin page: a navigation list on the left (on top on a phone) and the page beside it. What shows only decides what to offer; the admin API checks every call itself. */
@Component({
  selector: 'app-admin-layout',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'escape()', '(document:click)': 'clickAway($event)' },
  template: `
    <div class="adminshell">
      <div class="adminshell__bar">
        <button
          type="button"
          class="adminshell__toggle"
          aria-controls="admin-menu"
          [attr.aria-expanded]="open()"
          (click)="toggle()"
        >
          {{ open() ? 'Hide menu' : 'Admin menu' }}
        </button>
      </div>
      <div class="adminshell__body" [class.adminshell__body--docked]="docked() && open()">
        @if (open()) {
          <nav
            id="admin-menu"
            class="adminshell__nav"
            [class.adminshell__nav--floating]="!docked()"
            aria-label="Admin"
          >
            <h2 class="adminshell__title">Admin</h2>
            <ul class="adminshell__list">
              @for (item of items(); track item.path) {
                <li>
                  @if (item.needs) {
                    <span class="adminshell__link adminshell__link--off" aria-disabled="true">
                      {{ item.label }}
                      <small class="adminshell__needs">Needs {{ item.needs }}</small>
                    </span>
                  } @else {
                    <a
                      class="adminshell__link"
                      [routerLink]="item.path"
                      routerLinkActive="adminshell__link--active"
                      #active="routerLinkActive"
                      [routerLinkActiveOptions]="{ exact: item.exact }"
                      [attr.aria-current]="active.isActive ? 'page' : null"
                      (click)="chosenLink()"
                      >{{ item.label }}</a
                    >
                  }
                </li>
              }
            </ul>
          </nav>
        }
        <div class="adminshell__page"><router-outlet /></div>
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .adminshell__bar {
      padding: 0.5rem 1.5rem 0;
    }
    .adminshell__toggle {
      min-block-size: 2.75rem;
      padding: 0.5rem 0.75rem;
      font: inherit;
      color: inherit;
      background: transparent;
      border: 1px solid var(--ems-border-neutral-strong, currentColor);
      border-radius: var(--ems-corner-sm, 0.5rem);
      cursor: pointer;
    }
    .adminshell__body {
      position: relative;
      display: grid;
      grid-template-columns: minmax(0, 1fr);
      align-items: start;
    }
    .adminshell__nav {
      padding: 0.5rem 1.5rem 0;
    }
    .adminshell__nav--floating {
      position: absolute;
      inset-block-start: 0;
      inset-inline-start: 1.5rem;
      z-index: 20;
      inline-size: min(16rem, calc(100% - 3rem));
      box-sizing: border-box;
      padding: 0.75rem;
      background: var(--ems-surface-neutral-elevated, Canvas);
      color: var(--ems-text-default, CanvasText);
      border: 1px solid var(--ems-border-neutral-strong, currentColor);
      border-radius: var(--ems-corner-sm, 0.5rem);
      box-shadow: 0 0.5rem 1.5rem rgb(0 0 0 / 20%);
    }
    .adminshell__title {
      margin: 0 0 0.5rem;
      font-size: var(--ems-font-size-subheading);
    }
    .adminshell__list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    .adminshell__link {
      display: block;
      min-block-size: 2.75rem;
      padding: 0.625rem 0.75rem;
      box-sizing: border-box;
      border-radius: var(--ems-corner-sm, 0.5rem);
      color: inherit;
      text-decoration: none;
      overflow-wrap: anywhere;
    }
    .adminshell__link--off {
      color: var(--ems-text-muted);
      cursor: not-allowed;
    }
    .adminshell__needs {
      display: block;
      font-size: var(--ems-font-size-small);
    }
    .adminshell__link:not(.adminshell__link--off):hover {
      text-decoration: underline;
    }
    .adminshell__link--active {
      font-weight: var(--ems-font-weight-heavy);
      background: var(--ems-surface-subtle, rgb(0 0 0 / 6%));
    }
    .adminshell__page {
      min-inline-size: 0;
    }
    @media (min-width: 48rem) {
      .adminshell__body--docked {
        grid-template-columns: 14rem minmax(0, 1fr);
      }
      .adminshell__body--docked > .adminshell__nav {
        padding: 0.5rem 0 1.5rem 1.5rem;
      }
    }
  `,
})
export class AdminLayoutComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** The page the user is on. The editors need the width, so the menu starts closed there. */
  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url),
      startWith(this.router.url),
    ),
    { initialValue: this.router.url },
  );

  /** On a page that does not need the width the menu sits beside it; on an editor it floats over the page when opened. */
  protected readonly docked = computed(() => !/^\/admin\/templates\/(new|\d+)/.test(this.url()));

  /** What the user chose with the button, or null to follow the page: open beside the page, closed over an editor. */
  private readonly chosen = signal<boolean | null>(null);
  protected readonly open = computed(() => this.chosen() ?? this.docked());

  protected toggle(): void {
    this.chosen.set(!this.open());
  }

  /** Escape closes a menu that is floating over the page. A menu beside the page stays as the user left it. */
  protected escape(): void {
    if (!this.docked() && this.open()) {
      this.chosen.set(false);
    }
  }

  /** A click anywhere outside a floating menu and its button closes it. A menu beside the page is left alone. */
  protected clickAway(event: Event): void {
    if (this.docked() || !this.open()) {
      return;
    }

    const target = event.target as Node | null;
    const inside =
      target !== null &&
      (this.host.nativeElement.querySelector('.adminshell__nav')?.contains(target) ||
        this.host.nativeElement.querySelector('.adminshell__bar')?.contains(target));
    if (!inside) {
      this.chosen.set(false);
    }
  }

  /** A floating menu closes once a place is chosen, so it never covers the page it led to. */
  protected chosenLink(): void {
    if (!this.docked()) {
      this.chosen.set(false);
    }
  }

  protected readonly items = computed<readonly NavItem[]>(() => {
    const held = new Set(this.auth.adminPermissions());
    const templates =
      held.has('prism.admin.templates.author') || held.has('prism.admin.templates.publish');
    return [
      { label: 'Overview', path: '/admin', exact: true, needs: null },
      {
        label: 'View Templates',
        path: '/admin/templates',
        exact: false,
        needs: templates ? null : 'the Template author role',
      },
      {
        label: 'Personas and questions',
        path: '/admin/personas',
        exact: false,
        needs: templates ? null : 'the Template author role',
      },
      {
        label: 'Customer users',
        path: '/admin/customers',
        exact: false,
        needs: held.has('prism.admin.manage') ? null : 'the Admin manager role',
      },
    ];
  });
}

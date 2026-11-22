import { CdkDragHandle } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, ElementRef, HostListener, computed, inject, input, output, signal } from '@angular/core';
import { MatMenuModule } from '@angular/material/menu';
import { EmsButtonDirective, EmsCardComponent, EmsChipComponent, EmsIconComponent } from '@ems-management-consultants/ui';
import { CatalogMetricDto, LayoutWidget, SizePreset } from './layout.model';
import { MetricWidgetComponent, WidgetStatus } from './metric-widget.component';

/** Everything a widget's toolbar can ask the page to do. The page owns the layout, so the host only reports. */
export type WidgetAction =
  | { readonly kind: 'move-start' }
  | { readonly kind: 'move-key'; readonly event: KeyboardEvent }
  | { readonly kind: 'move-blur' }
  | { readonly kind: 'preset'; readonly preset: SizePreset }
  | { readonly kind: 'settings' }
  | { readonly kind: 'remove' }
  | { readonly kind: 'reorder'; readonly direction: -1 | 1 };

/**
 * One widget on the grid: a card with the Metric's name as its h2 and the Metric inside. In edit mode it adds a toolbar (one Tab stop, arrow keys inside) with
 * Move, Size, Settings and Remove, each named after the widget. In the one-column layout Move up and Move down stand in for dragging.
 */
@Component({
  selector: 'app-widget-host',
  imports: [CdkDragHandle, EmsButtonDirective, EmsCardComponent, EmsChipComponent, EmsIconComponent, MatMenuModule, MetricWidgetComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ems-card [heading]="title()">
      <div emsCardActions>
      @if (editing()) {
        <div class="host__tools" role="toolbar" [attr.aria-label]="title() + ' actions'">
          <button emsButton="flat" fab color="neutral" type="button" class="host__tool" cdkDragHandle [tabindex]="tab(0)" [attr.aria-label]="'Move ' + title()" [attr.aria-pressed]="moving()" (keydown)="moveKey($event)" (blur)="action.emit({ kind: 'move-blur' })" (click)="!moving() && action.emit({ kind: 'move-start' })">
            <ems-icon name="drag_indicator" />
          </button>
          @if (stacked()) {
            <button emsButton="flat" fab color="neutral" type="button" class="host__tool" [tabindex]="tab(1)" [attr.aria-label]="'Move ' + title() + ' up'" (click)="action.emit({ kind: 'reorder', direction: -1 })"><ems-icon name="arrow_upward" /></button>
            <button emsButton="flat" fab color="neutral" type="button" class="host__tool" [tabindex]="tab(2)" [attr.aria-label]="'Move ' + title() + ' down'" (click)="action.emit({ kind: 'reorder', direction: 1 })"><ems-icon name="arrow_downward" /></button>
          }
          <button emsButton="flat" type="button" class="host__tool host__size" [tabindex]="tab(3)" [attr.aria-label]="'Size ' + (sizeName() ?? 'custom') + ', ' + title()" [matMenuTriggerFor]="sizes">
            {{ size() ?? '·' }}<ems-icon name="expand_more" size="sm" />
          </button>
          <mat-menu #sizes="matMenu">
            @for (preset of presets(); track preset.key) {
              <button mat-menu-item type="button" role="menuitemradio" [attr.aria-checked]="size() === preset.key" (click)="action.emit({ kind: 'preset', preset: preset.key })">{{ preset.label }}</button>
            }
          </mat-menu>
          <button emsButton="flat" fab color="neutral" type="button" class="host__tool" [tabindex]="tab(4)" [attr.aria-label]="'Settings for ' + title()" (click)="action.emit({ kind: 'settings' })"><ems-icon name="tune" /></button>
          <button emsButton="flat" fab color="neutral" type="button" class="host__tool" [tabindex]="tab(5)" [attr.aria-label]="'Remove ' + title()" (click)="action.emit({ kind: 'remove' })"><ems-icon name="delete" /></button>
        </div>
      }
      </div>
      @if (moving()) {
        <ems-chip state="info" icon="open_with">Moving</ems-chip>
      }
      <app-metric-widget [widget]="widget()" [metric]="metric()" [editing]="editing()" (status)="status.emit($event)" />
    </ems-card>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
      height: 100%;
    }
    .host__tools {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.125rem;
    }
    .host__tool {
      min-width: 2rem;
      min-height: 2rem;
    }
    .host__size {
      display: inline-flex;
      align-items: center;
      font-weight: 700;
    }
    @media (pointer: coarse) {
      .host__tool {
        min-width: 2.75rem;
        min-height: 2.75rem;
      }
    }
  `,
})
export class WidgetHostComponent {
  readonly widget = input.required<LayoutWidget>();
  readonly metric = input.required<CatalogMetricDto>();
  /** The title with "(1 of 2)" when two widgets share a name, so each toolbar has its own accessible name. */
  readonly title = input.required<string>();
  readonly editing = input(false);
  readonly moving = input(false);
  /** The one-column layout: dragging and left and right mean nothing, so up and down buttons appear. */
  readonly stacked = input(false);
  readonly size = input<SizePreset | null>(null);

  readonly action = output<WidgetAction>();
  readonly status = output<WidgetStatus>();

  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly active = signal(0);

  protected readonly sizeName = computed(() => ({ S: 'small', M: 'medium', L: 'large' })[this.size() ?? 'M']);
  protected readonly presets = computed(() => {
    const allowed = this.metric().presentations.find((p) => p.key === this.widget().presentation)?.sizes ?? ['S', 'M', 'L'];
    return (['S', 'M', 'L'] as const).filter((key) => allowed.includes(key)).map((key) => ({ key, label: { S: 'Small', M: 'Medium', L: 'Large' }[key] }));
  });

  /** Roving tabindex: only the active tool is in the Tab order, the arrow keys move between the rest. */
  protected tab(index: number): number {
    return this.active() === index ? 0 : -1;
  }

  @HostListener('keydown', ['$event'])
  protected toolbarKey(event: KeyboardEvent): void {
    // The page cancels the default of a key it used for a move, so a move in progress never also moves focus along the toolbar.
    if (event.defaultPrevented || !(event.target as HTMLElement).closest?.('.host__tools') || this.moving() || !['ArrowRight', 'ArrowLeft', 'Home', 'End'].includes(event.key)) {
      return;
    }
    const tools = [...this.element.nativeElement.querySelectorAll<HTMLButtonElement>('.host__tool')];
    const current = tools.findIndex((tool) => tool === document.activeElement);
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? tools.length - 1 : (current + (event.key === 'ArrowRight' ? 1 : -1) + tools.length) % tools.length;
    tools[next]?.focus();
    this.active.set(this.slotOf(next));
    event.preventDefault();
  }

  // Each tool keeps a fixed tabindex slot, so map a tool's position in the row back to its slot.
  private slotOf(index: number): number {
    const slots = [0, ...(this.stacked() ? [1, 2] : []), 3, 4, 5];
    return slots[index] ?? 0;
  }

  /**
   * Every key on the Move button goes to the page, which knows whether a move is on. This host's own `moving` input lags a frame behind a click, so a quick
   * Tab right after starting would otherwise slip through. Starting a move is the button's click, so a screen reader or voice command works too.
   */
  protected moveKey(event: KeyboardEvent): void {
    this.action.emit({ kind: 'move-key', event });
  }
}

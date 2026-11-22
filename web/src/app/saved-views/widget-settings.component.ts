import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { EmsButtonDirective, EmsIconComponent, EmsPickerOption, EmsSelectComponent } from '@ems-management-consultants/ui';
import { FormsModule } from '@angular/forms';
import { SegmentOption, SegmentedControlComponent } from '../dashboard/segmented-control.component';
import { SwitchComponent } from '../dashboard/switch.component';
import { presetOf } from './grid-layout';
import { CatalogMetricDto, CatalogOptionDto, GRID_COLUMNS, LayoutWidget, MAX_ROWS, SizePreset } from './layout.model';

/** What the pane asks the page to change. The page applies it through the grid rules and announces it. */
export type SettingsChange =
  | { readonly kind: 'presentation'; readonly key: string }
  | { readonly kind: 'option'; readonly key: string; readonly value: string }
  | { readonly kind: 'preset'; readonly preset: SizePreset }
  | { readonly kind: 'size'; readonly w: number; readonly h: number }
  | { readonly kind: 'position'; readonly x: number; readonly y: number };

/**
 * Settings for one widget, drawn from the catalog: how to show it, its options, and a plain form for size and position. The form is the way to place a
 * widget with a single click or tap and no dragging (WCAG 2.5.7), and it works under a screen reader that takes the arrow keys for itself.
 * A new option is catalog metadata: the control for it follows from its type, with no change here.
 */
@Component({
  selector: 'app-widget-settings',
  imports: [EmsButtonDirective, EmsIconComponent, EmsSelectComponent, FormsModule, SegmentedControlComponent, SwitchComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 #heading tabindex="-1" id="settings-heading">Settings: {{ title() }}</h2>

    <fieldset class="settings__group">
      <legend>How to show it</legend>
      <app-segmented-control label="Presentation" [options]="presentationOptions()" [value]="widget().presentation" (valueChange)="settingsChange.emit({ kind: 'presentation', key: $event })" />
    </fieldset>

    @for (option of options(); track option.key) {
      <fieldset class="settings__group">
        <legend>{{ option.name }}</legend>
        @if (option.type === 'toggle') {
          <app-switch [label]="option.name" [checked]="valueOf(option) === 'true'" (checkedChange)="settingsChange.emit({ kind: 'option', key: option.key, value: $event ? 'true' : 'false' })" />
        } @else if (option.choices.length <= 4) {
          <app-segmented-control [label]="option.name" [options]="choiceOptions(option)" [value]="valueOf(option)" (valueChange)="settingsChange.emit({ kind: 'option', key: option.key, value: $event })" />
        } @else {
          <ems-select [label]="option.name" [options]="selectOptions(option)" [ngModel]="valueOf(option)" (ngModelChange)="settingsChange.emit({ kind: 'option', key: option.key, value: $event })" [clearable]="false" />
        }
      </fieldset>
    }

    <fieldset class="settings__group">
      <legend>Size and position</legend>
      <app-segmented-control label="Size" [options]="sizeOptions()" [value]="preset() ?? 'M'" (valueChange)="settingsChange.emit({ kind: 'preset', preset: $event })" />
      <div class="settings__steppers">
        @for (field of fields(); track field.id) {
          <div class="settings__stepper">
            <label [for]="field.id">{{ field.label }}</label>
            <div class="settings__stepperrow">
              <button emsButton="raised" type="button" class="settings__step" [attr.aria-label]="'Decrease ' + field.label.toLowerCase()" (click)="step(field.id, -1)"><ems-icon name="remove" size="sm" /></button>
              <input [id]="field.id" type="number" inputmode="numeric" [min]="field.min" [max]="field.max" step="1" [value]="field.value" (change)="set(field.id, $any($event.target).valueAsNumber)" />
              <button emsButton="raised" type="button" class="settings__step" [attr.aria-label]="'Increase ' + field.label.toLowerCase()" (click)="step(field.id, 1)"><ems-icon name="add" size="sm" /></button>
            </div>
          </div>
        }
      </div>
      <div class="settings__nudge" role="group" aria-label="Nudge position">
        <button emsButton="raised" type="button" aria-label="Move up one row" (click)="nudge(0, -1)"><ems-icon name="arrow_upward" size="sm" /></button>
        <button emsButton="raised" type="button" aria-label="Move left one column" (click)="nudge(-1, 0)"><ems-icon name="arrow_back" size="sm" /></button>
        <button emsButton="raised" type="button" aria-label="Move right one column" (click)="nudge(1, 0)"><ems-icon name="arrow_forward" size="sm" /></button>
        <button emsButton="raised" type="button" aria-label="Move down one row" (click)="nudge(0, 1)"><ems-icon name="arrow_downward" size="sm" /></button>
      </div>
    </fieldset>

    <div class="settings__done"><button emsButton="filled" type="button" (click)="done.emit()">Done</button></div>
  `,
  styles: `
    :host {
      display: block;
    }
    h2 {
      margin: 0 0 0.5rem;
      font-size: var(--ems-font-size-subheading);
      overflow-wrap: anywhere;
    }
    .settings__group {
      margin: 0 0 1rem;
      padding: 0;
      border: 0;
    }
    legend {
      padding: 0;
      margin-block-end: 0.25rem;
      font-weight: 600;
    }
    .settings__steppers {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr));
      gap: 0.75rem;
      margin-block-start: 0.75rem;
    }
    .settings__stepper label {
      display: block;
      font-size: var(--ems-font-size-small);
      color: var(--ems-text-muted);
    }
    .settings__stepperrow {
      display: flex;
      align-items: center;
      gap: 0.25rem;
    }
    .settings__step {
      min-width: 2rem;
      min-height: 2rem;
    }
    input[type='number'] {
      width: 3.5rem;
      min-height: 2rem;
      text-align: center;
      font: inherit;
      color: var(--ems-text-default);
      background: var(--ems-surface-neutral-default);
      border: 1px solid var(--ems-border-neutral-strong, currentColor);
      border-radius: 4px;
    }
    .settings__nudge {
      display: flex;
      flex-wrap: wrap;
      gap: 0.25rem;
      margin-block-start: 0.75rem;
    }
    .settings__done {
      display: flex;
      justify-content: flex-end;
    }
    @media (pointer: coarse) {
      .settings__step,
      input[type='number'] {
        min-height: 2.75rem;
        min-width: 2.75rem;
      }
    }
  `,
})
export class WidgetSettingsComponent {
  readonly widget = input.required<LayoutWidget>();
  readonly metric = input.required<CatalogMetricDto>();
  readonly title = input.required<string>();

  readonly settingsChange = output<SettingsChange>();
  readonly done = output<void>();

  private readonly presentation = computed(() => this.metric().presentations.find((p) => p.key === this.widget().presentation));
  protected readonly preset = computed(() => presetOf(this.widget()));

  protected readonly presentationOptions = computed<SegmentOption<string>[]>(() => this.metric().presentations.map((p) => ({ value: p.key, label: p.name, icon: p.icon })));
  protected readonly options = computed(() => this.metric().options.filter((o) => o.appliesTo.includes(this.widget().presentation)));
  protected readonly sizeOptions = computed<SegmentOption<SizePreset>[]>(() => {
    const allowed = this.presentation()?.sizes ?? ['S', 'M', 'L'];
    return (['S', 'M', 'L'] as const).filter((key) => allowed.includes(key)).map((key) => ({ value: key, label: { S: 'Small', M: 'Medium', L: 'Large' }[key] }));
  });

  protected readonly fields = computed(() => {
    const w = this.widget();
    const p = this.presentation();
    return [
      { id: 'w', label: 'Width', value: w.w, min: p?.minWidth ?? 1, max: p?.maxWidth ?? GRID_COLUMNS },
      { id: 'h', label: 'Height', value: w.h, min: p?.minHeight ?? 1, max: p?.maxHeight ?? 12 },
      { id: 'x', label: 'Column', value: w.x + 1, min: 1, max: GRID_COLUMNS - w.w + 1 },
      { id: 'y', label: 'Row', value: w.y + 1, min: 1, max: MAX_ROWS - w.h + 1 },
    ];
  });

  protected valueOf(option: CatalogOptionDto): string {
    return this.widget().options?.[option.key] ?? option.default;
  }

  protected choiceOptions(option: CatalogOptionDto): SegmentOption<string>[] {
    return option.choices.map((choice) => ({ value: choice, label: choice }));
  }

  protected selectOptions(option: CatalogOptionDto): EmsPickerOption<string>[] {
    return option.choices.map((choice) => ({ value: choice, label: choice }));
  }

  protected step(id: string, delta: number): void {
    const field = this.fields().find((f) => f.id === id);
    if (field) {
      this.set(id, field.value + delta);
    }
  }

  protected set(id: string, value: number): void {
    if (!Number.isFinite(value)) {
      return;
    }
    const w = this.widget();
    switch (id) {
      case 'w':
        this.settingsChange.emit({ kind: 'size', w: value, h: w.h });
        break;
      case 'h':
        this.settingsChange.emit({ kind: 'size', w: w.w, h: value });
        break;
      case 'x':
        this.settingsChange.emit({ kind: 'position', x: value - 1, y: w.y });
        break;
      default:
        this.settingsChange.emit({ kind: 'position', x: w.x, y: value - 1 });
    }
  }

  protected nudge(dx: number, dy: number): void {
    const w = this.widget();
    this.settingsChange.emit({ kind: 'position', x: w.x + dx, y: w.y + dy });
  }
}

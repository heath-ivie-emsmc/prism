import { ChangeDetectionStrategy, Component, Signal, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogClose } from '@angular/material/dialog';
import { EmsButtonDirective, EmsDialogActionsDirective, EmsDialogComponent } from '@ems-management-consultants/ui';
import { CatalogDto, CatalogMetricDto } from './layout.model';
import { MetricPaletteComponent } from './metric-palette.component';

export interface PaletteDialogData {
  readonly catalog: CatalogDto;
  readonly canAdd: Signal<boolean>;
  readonly add: (metric: CatalogMetricDto) => void;
  readonly blocked: (event: { metric: CatalogMetricDto; reason: 'planned' | 'full' }) => void;
  readonly searched: (event: { count: number; query: string }) => void;
}

/** The palette in a dialog, for screens too narrow to dock it. It stays open so several metrics can be added in a row. */
@Component({
  selector: 'app-palette-dialog',
  imports: [EmsButtonDirective, EmsDialogActionsDirective, EmsDialogComponent, MatDialogClose, MetricPaletteComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ems-dialog heading="Add metrics">
      <div class="palette-dialog__body">
        <app-metric-palette [catalog]="data.catalog" [canAdd]="data.canAdd()" [dragEnabled]="false" (add)="data.add($event)" (blocked)="data.blocked($event)" (searched)="data.searched($event)" />
      </div>
      <div emsDialogActions><button emsButton="filled" type="button" mat-dialog-close>Done</button></div>
    </ems-dialog>
  `,
  styles: `
    /* The list scrolls inside the dialog so Done stays on screen on a phone, in landscape and at larger text. */
    .palette-dialog__body {
      max-height: min(60dvh, 28rem);
      overflow-y: auto;
      padding-inline-end: 0.25rem;
    }
  `,
})
export class PaletteDialogComponent {
  protected readonly data = inject<PaletteDialogData>(MAT_DIALOG_DATA);
}

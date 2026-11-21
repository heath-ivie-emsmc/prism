import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';
import { SegmentedControlComponent } from './segmented-control.component';
import { ServiceLineOption, ServiceLineSelection } from './service-line';

/** The Service Line switch: Air only, Ground only, or both. */
@Component({
  selector: 'app-service-line-toggle',
  imports: [SegmentedControlComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-segmented-control label="Service line" [options]="options()" [(value)]="value" />`,
})
export class ServiceLineToggleComponent {
  readonly options = input.required<readonly ServiceLineOption[]>();
  readonly value = model.required<ServiceLineSelection>();
}

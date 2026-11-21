import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { axeViolations } from '../axe';
import { SegmentedControlComponent } from './segmented-control.component';

@Component({
  imports: [SegmentedControlComponent],
  template: `<app-segmented-control label="Range" [options]="options" [(value)]="value" [iconOnly]="iconOnly()" />`,
})
class HostComponent {
  options = [
    { value: 'a', label: 'Alpha', icon: 'show_chart' },
    { value: 'b', label: 'Beta', icon: 'bar_chart' },
  ];
  readonly value = signal('a');
  readonly iconOnly = signal(false);
}

describe('SegmentedControlComponent', () => {
  function create() {
    TestBed.configureTestingModule({ providers: [provideEmsUi()] });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const radios = () => [...root.querySelectorAll<HTMLButtonElement>('[role="radio"]')];
    return { fixture, root, radios };
  }

  it('selects on click and keeps only the chosen option in the tab order', () => {
    const { fixture, radios } = create();

    radios()[1]!.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.value()).toBe('b');
    expect(radios().map((r) => r.tabIndex)).toEqual([-1, 0]);
  });

  it('wraps with the arrow keys', () => {
    const { fixture, radios } = create();

    radios()[0]!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    fixture.detectChanges();

    expect(fixture.componentInstance.value()).toBe('b');
  });

  it('names icon-only options by their label', () => {
    const { fixture, radios } = create();
    fixture.componentInstance.iconOnly.set(true);
    fixture.detectChanges();

    expect(radios().map((r) => r.getAttribute('aria-label'))).toEqual(['Alpha', 'Beta']);
    expect(radios()[0]!.textContent?.trim()).not.toContain('Alpha');
  });

  it('has no axe violations', async () => {
    const { root } = create();

    expect(await axeViolations(root)).toEqual([]);
  });
});

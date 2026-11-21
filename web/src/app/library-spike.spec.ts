import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMS_UI, EmsPickerOption, provideEmsUi } from '@ems-management-consultants/ui';

/**
 * Compatibility spike: @ems-management-consultants/ui is built against Angular and Material 19.2,
 * and Prism runs on the latest Angular. These tests render the components that sit on Angular Material and
 * the CDK overlay, which are the likeliest to break across majors.
 */
@Component({
  imports: [EMS_UI],
  template: `
    <section emsUiScope page>
      <ems-nav-bar>
        <span emsNavLogo><ems-logo /></span>
        <a emsNavTab href="#">Dashboard</a>
      </ems-nav-bar>
      <ems-select label="Period" [options]="options" />
      <ems-calendar />
      <ems-pagination [length]="100" />
    </section>
  `,
})
class SpikeHost {
  protected readonly options: EmsPickerOption<string>[] = [
    { value: 'oct', label: 'October 2026' },
    { value: 'sep', label: 'September 2026' },
  ];
}

describe('@ems-management-consultants/ui on the current Angular', () => {
  let fixture: ComponentFixture<SpikeHost>;
  let root: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SpikeHost],
      providers: [provideEmsUi()],
    }).compileComponents();
    fixture = TestBed.createComponent(SpikeHost);
    await fixture.whenStable();
    root = fixture.nativeElement as HTMLElement;
  });

  it('renders the nav bar', () => {
    expect(root.querySelector('ems-nav-bar')?.textContent).toContain('Dashboard');
  });

  it('renders the select with its label', () => {
    expect(root.querySelector('ems-select')?.textContent).toContain('Period');
  });

  it('renders the calendar with day cells', () => {
    expect(root.querySelectorAll('ems-calendar button, ems-calendar [role="gridcell"]').length).toBeGreaterThan(20);
  });

  it('renders the pagination', () => {
    expect(root.querySelector('ems-pagination')?.textContent).toMatch(/\d/);
  });

  it('opens the select panel and lists its options', async () => {
    // The select opens on pointerdown/mousedown over its field, not on click.
    const field = root.querySelector<HTMLElement>('.ems-select__field');
    expect(field).not.toBeNull();

    field!.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true }));
    field!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    fixture.detectChanges();
    await fixture.whenStable();

    // The panel renders outside the <ems-select> element, so look in the whole document.
    expect(document.body.textContent).toContain('October 2026');
    expect(document.body.textContent).toContain('September 2026');
  });
});

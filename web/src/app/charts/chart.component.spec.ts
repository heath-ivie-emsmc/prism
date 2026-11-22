import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { axeViolations } from '../axe';
import { ChartColors } from './chart.component';
import { ChartComponent } from './chart.component';

@Component({
  imports: [ChartComponent],
  template: `<app-chart [option]="option" label="Net cash rose to $2,500 in Feb." renderer="svg" />`,
})
class HostComponent {
  colorsSeen: ChartColors | undefined;
  readonly option = (colors: ChartColors) => {
    this.colorsSeen = colors;
    return {
      xAxis: { type: 'category', data: ['Jan', 'Feb'] },
      yAxis: { type: 'value' },
      series: [{ type: 'line', data: [1000, 2500] }],
    };
  };
}

describe('ChartComponent', () => {
  async function render() {
    TestBed.configureTestingModule({ providers: [provideEmsUi()] });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    await vi.waitFor(() => expect(root.querySelector('svg')).not.toBeNull(), { timeout: 5000 });
    return { fixture, root };
  }

  it('names the chart for screen readers with the text it is given', async () => {
    const { root } = await render();

    expect(root.querySelector('[role="img"]')?.getAttribute('aria-label')).toBe('Net cash rose to $2,500 in Feb.');
  });

  it('draws the option after ECharts loads, with the design token colors', async () => {
    const { fixture } = await render();

    expect(fixture.componentInstance.colorsSeen).toBeDefined();
    expect(Object.keys(fixture.componentInstance.colorsSeen!)).toEqual(['text', 'mutedText', 'grid', 'actual', 'highlight', 'projected', 'categorical', 'narrow']);
  });

  it('has no axe violations', async () => {
    const { root } = await render();

    expect(await axeViolations(root)).toEqual([]);
  });
});

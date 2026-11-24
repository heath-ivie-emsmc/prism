import { escapeHtml } from '../shared/escape-html';
import { ChartColors } from './chart.component';
import { NetCashMonth } from './net-cash';
import { buildNetCashOption, niceStep } from './net-cash-chart.options';

const COLORS: ChartColors = { text: '#000', mutedText: '#555', grid: '#ddd', actual: '#666', highlight: '#a00', projected: '#070', narrow: false };
const MONTHS: NetCashMonth[] = [
  { label: 'May', year: 2026, value: 1_180_000, kind: 'actual' },
  { label: 'Oct', year: 2026, value: 1_100_000, kind: 'actual' },
  { label: 'Nov', year: 2026, value: 1_210_000, kind: 'projected', low: 1_150_000, high: 1_280_000 },
  { label: 'Jan', year: 2027, value: 1_200_000, kind: 'projected', low: 1_080_000, high: 1_320_000 },
];

interface Probe {
  animation: boolean;
  xAxis: { data: string[] };
  yAxis: { min: number; max: number; interval: number };
  series: { name: string; type: string; data: unknown[] }[];
}
const build = (kind: 'line' | 'bar', months = MONTHS) => buildNetCashOption(months, kind, COLORS) as unknown as Probe;

describe('buildNetCashOption', () => {
  it('shows one tooltip line per month, never NaN, with the forecast range for projected months', () => {
    const tooltip = (build('line') as unknown as { tooltip: { formatter: (p: unknown) => string } }).tooltip;
    const rows = (index: number) => [{ dataIndex: index, seriesName: 'Net cash collected' }, { dataIndex: index, seriesName: 'Projected', value: '-' }];

    const actual = tooltip.formatter(rows(1));
    const projected = tooltip.formatter(rows(2));

    expect(actual).toContain('Oct 2026');
    expect(actual).toContain('$1,100,000.00');
    expect(projected).toContain('Projected');
    expect(projected).toContain('Forecast range $1,150,000.00 to $1,280,000.00');
    expect(actual + projected).not.toContain('NaN');
  });

  it('writes the year under the first month and under January', () => {
    expect(build('bar').xAxis.data).toEqual(['May\n2026', 'Oct', 'Nov', 'Jan\n2027']);
  });

  it('draws one bar per month: gray actual, the latest actual highlighted, projected outlined and dashed after the first', () => {
    const bars = build('bar').series[0]!.data as { itemStyle: { color: string; borderColor?: string; borderType?: string } }[];

    expect(bars[0]!.itemStyle.color).toBe('#666');
    expect(bars[1]!.itemStyle.color).toBe('#a00');
    expect(bars[2]!.itemStyle).toMatchObject({ borderColor: '#070', borderType: 'solid' });
    expect(bars[3]!.itemStyle).toMatchObject({ borderType: 'dashed' });
  });

  it('draws the actual line, a projected line that joins it, whiskers and a divider for the line view', () => {
    const series = build('line').series;

    expect(series.map((s) => s.name)).toEqual(['Net cash collected', 'Projected', 'Forecast range', 'Actual and projected divider']);
    expect((series[1]!.data as (unknown | null)[]).slice(0, 1)).toEqual([null]);
    expect(series[2]!.data).toEqual([[2, 1_150_000, 1_280_000, 1_210_000], [3, 1_080_000, 1_320_000, 1_200_000]]);
  });

  it('has no divider or projection series when every month is actual', () => {
    const series = build('line', MONTHS.slice(0, 2)).series;

    expect(series.map((s) => s.name)).not.toContain('Actual and projected divider');
    expect(series[2]!.data).toEqual([]);
  });

  it('starts the bar axis at zero and leaves room above the highest whisker', () => {
    const axis = build('bar').yAxis;

    expect(axis.min).toBe(0);
    expect(axis.max).toBeGreaterThan(1_320_000);
  });

  it('uses round axis bounds that contain every value and whisker', () => {
    const axis = build('line').yAxis;

    expect(axis.min).toBeLessThanOrEqual(1_080_000);
    expect(axis.max).toBeGreaterThanOrEqual(1_320_000);
    expect(axis.min % axis.interval).toBe(0);
    expect(axis.max % axis.interval).toBe(0);
  });

  it('turns animation off for dense series', () => {
    const dense = Array.from({ length: 30 }, (_, i) => ({ ...MONTHS[0]!, label: `M${i}` }));

    expect(build('line', dense).animation).toBe(false);
  });
});

describe('niceStep', () => {
  it('picks 1, 2 or 5 times a power of ten', () => {
    expect(niceStep(1_500_000)).toBe(500_000);
    expect(niceStep(300_000)).toBe(100_000);
    expect(niceStep(0.9)).toBe(0.2);
  });
});

describe('net cash tooltip escaping', () => {
  it.each(['<img src=x onerror=alert(1)>', '"><script>alert(1)</script>'])('escapes a hostile month label %s', (hostile) => {
    const months: NetCashMonth[] = [{ label: hostile, year: 2026, value: 1, kind: 'actual' }];
    const tooltip = (buildNetCashOption(months, 'line', COLORS) as unknown as { tooltip: { formatter: (p: unknown) => string } }).tooltip;
    const html = tooltip.formatter([{ dataIndex: 0, seriesName: 'Net cash collected' }]);
    expect(html).not.toContain('<img');
    expect(html).not.toContain('<script');
    expect(html).toContain(escapeHtml(hostile));
  });
});

import { MOCK_CONTEXT, divisionsFor } from './dashboard-context';
import { defaultLabels } from './service-line';

describe('reporting divisions and labels', () => {
  it('limits divisions to the chosen Service Line, or shows all of them for both', () => {
    expect(divisionsFor(MOCK_CONTEXT.reportingDivisions, 'air').every((d) => d.serviceLine === 'air')).toBe(true);
    expect(divisionsFor(MOCK_CONTEXT.reportingDivisions, 'ground').every((d) => d.serviceLine === 'ground')).toBe(true);
    expect(divisionsFor(MOCK_CONTEXT.reportingDivisions, 'both')).toHaveLength(MOCK_CONTEXT.reportingDivisions.length);
  });

  it('names the filter by Service Line: air sees bases, everyone else sees reporting divisions', () => {
    expect(defaultLabels('air')).toEqual({ reportingDivision: 'Base location', allReportingDivisions: 'All bases' });
    expect(defaultLabels('ground').reportingDivision).toBe('Reporting division');
    expect(defaultLabels('both').reportingDivision).toBe('Reporting division');
  });
});

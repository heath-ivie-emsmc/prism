import { describe, expect, it } from 'vitest';
import { defaultLabels } from '../dashboard/service-line';
import { EmptyScope, emptyMessage, isNarrowed } from './empty-scope';

const AIR = defaultLabels('air');
const PLAIN = 'No net cash collected data for September 2026.';
const say = (scope: Partial<EmptyScope>): string => emptyMessage('net cash collected', 'September 2026', PLAIN, { line: null, divisionApplied: false, labels: AIR, ...scope });

describe('the empty-state sentence', () => {
  it('names the division and the line, and what to try, when both narrowed the answer', () => {
    expect(say({ line: 'air', divisionApplied: true, divisionName: 'Mercy Flights Incorporated OR - Ground Critical Care' })).toBe(
      'No net cash collected for Mercy Flights Incorporated OR - Ground Critical Care under Air only in September 2026. Try All bases or Air + ground.',
    );
  });

  it('names only what narrowed it', () => {
    expect(say({ divisionApplied: true, divisionName: 'Summit Base' })).toBe('No net cash collected for Summit Base in September 2026. Try All bases.');
    expect(say({ line: 'ground' })).toBe('No net cash collected under Ground only in September 2026. Try Air + ground.');
  });

  it('is the plain sentence when nothing narrowed it, whether the line is all, absent or the Provider has only one', () => {
    expect(say({})).toBe(PLAIN);
    expect(say({ line: 'all' })).toBe(PLAIN);
    expect(isNarrowed({ line: 'all', divisionApplied: false, labels: AIR })).toBe(false);
  });

  it('uses the Label when the division name is not known yet', () => {
    expect(say({ divisionApplied: true })).toBe('No net cash collected for the chosen base location in September 2026. Try All bases.');
  });

  it('carries a hostile name through as text, unchanged', () => {
    expect(say({ divisionApplied: true, divisionName: '<img src=x onerror=alert(1)> Base' })).toContain('for <img src=x onerror=alert(1)> Base in');
  });
});

import { describe, expect, it } from 'vitest';
import { splitName } from './payer-donuts.component';

describe('donut centre name', () => {
  it('keeps a short name on one line', () => {
    expect(splitName('Medicaid')).toEqual(['Medicaid']);
    expect(splitName('VA')).toEqual(['VA']);
  });

  it('splits a long name over two lines at the space nearest the middle', () => {
    expect(splitName('Patient responsibility')).toEqual(['Patient', 'responsibility']);
    expect(splitName('Other government')).toEqual(['Other', 'government']);
  });

  it('has no lines for no name', () => {
    expect(splitName('')).toEqual([]);
  });
});

import { providerLogo } from './provider-logo';

describe('providerLogo', () => {
  it('finds a logo by name, ignoring case and spaces at the ends', () => {
    expect(providerLogo('  Mercy Flights Incorporated OR ')).toEqual({ url: '/providers/mercy-flights-incorporated-or.png', plate: 'dark' });
    expect(providerLogo('Seminole County Fire Department')?.plate).toBe('light');
  });

  it('has none for an unknown or missing name, so the name is shown instead', () => {
    expect(providerLogo('Somebody Else')).toBeNull();
    expect(providerLogo(undefined)).toBeNull();
  });
});

import { describe, expect, it } from 'vitest';
import { captionLines, imageFileName } from './view-image';

describe('view image', () => {
  const day = new Date(2026, 10, 22);

  it('names the file from letters, digits and hyphens only', () => {
    expect(imageFileName('Cash overview', day)).toBe('prism-Cash-overview-2026-11-22.png');
    expect(imageFileName('../../etc/passwd "x"; \\ ', day)).toBe('prism-etc-passwd-x-2026-11-22.png');
    expect(imageFileName('Déjà vu: Q4 / ground', day)).toBe('prism-Deja-vu-Q4-ground-2026-11-22.png');
  });

  it('falls back to a plain name and caps the length', () => {
    expect(imageFileName('***', day)).toBe('prism-view-2026-11-22.png');
    expect(imageFileName('', day)).toBe('prism-view-2026-11-22.png');
    const long = imageFileName('a'.repeat(500), day);
    expect(long.length).toBeLessThan(90);
    expect(long).toMatch(/^prism-a+-2026-11-22\.png$/);
  });

  it('prints the view, the Provider, the filters and Data Through', () => {
    const lines = captionLines({ viewName: 'Cash overview', providerName: 'Mercy Flights', filters: ['Period October 2026', 'Air'], dataThrough: '11/22/2026', takenAt: 'Nov 22, 2026, 1:00 PM' });
    expect(lines).toEqual(['Cash overview', 'Mercy Flights', 'Period October 2026 · Air', 'Data through 11/22/2026', 'Image taken Nov 22, 2026, 1:00 PM']);
  });

  it('says so when Data Through is not known and skips an empty Provider', () => {
    const lines = captionLines({ viewName: 'V', providerName: '', filters: [], dataThrough: '', takenAt: 't' });
    expect(lines).toEqual(['V', 'Data through date not available', 'Image taken t']);
  });
});

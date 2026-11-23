import { describe, expect, it } from 'vitest';
import { EXITED_ANNOUNCEMENT, UNAVAILABLE_ANNOUNCEMENT, enteredAnnouncement, expandButtonName, frameContext, fullscreenSupported } from './fullscreen';

describe('fullscreenSupported', () => {
  const target = { requestFullscreen: () => Promise.resolve() };

  it('is true only when the document allows it and the element can ask', () => {
    expect(fullscreenSupported({ fullscreenEnabled: true }, target)).toBe(true);
  });

  it('is false when the document does not allow full screen', () => {
    expect(fullscreenSupported({ fullscreenEnabled: false }, target)).toBe(false);
    expect(fullscreenSupported({}, target)).toBe(false);
  });

  it('is false when the element has no way to ask, as on iPhone', () => {
    expect(fullscreenSupported({ fullscreenEnabled: true }, {})).toBe(false);
    expect(fullscreenSupported({ fullscreenEnabled: true }, { requestFullscreen: 'nope' })).toBe(false);
  });
});

describe('full screen words', () => {
  it('names the widget on the expand button and drops the name on exit', () => {
    expect(expandButtonName('Net cash collected', false)).toBe('Expand Net cash collected to full screen');
    expect(expandButtonName('Net cash collected', true)).toBe('Exit full screen');
  });

  it('says how to leave when it enters', () => {
    expect(enteredAnnouncement('Net cash collected')).toBe('Net cash collected full screen. Press Escape to exit.');
    expect(EXITED_ANNOUNCEMENT).toBe('Exited full screen.');
    expect(UNAVAILABLE_ANNOUNCEMENT).toBe("Full screen isn't available here.");
  });
});

describe('frameContext', () => {
  it('joins the period and the Service Line', () => {
    expect(frameContext('October 2026', 'Air')).toBe('October 2026 · Air');
  });

  it('leaves out what is not known yet', () => {
    expect(frameContext('', 'Air')).toBe('Air');
    expect(frameContext('', '')).toBe('');
  });
});

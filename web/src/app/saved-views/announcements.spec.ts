import { describe, expect, it } from 'vitest';
import { announce } from './announcements';

describe('announcements', () => {
  it('counts rows and columns from one and says how many metrics there are', () => {
    expect(announce.added('Net cash collected', 6, 1, 'M', 6, 3)).toBe('Net cash collected added at row 2, column 7, size M, 6 columns wide. 3 metrics on this view.');
    expect(announce.added('Payer mix', 0, 0, 'S', 3, 1)).toBe('Payer mix added at row 1, column 1, size S, 3 columns wide. 1 metric on this view.');
  });

  it('explains why a metric was not added', () => {
    expect(announce.limitReached('Payer mix', 24)).toBe("Can't add Payer mix. A view holds up to 24 metrics.");
    expect(announce.noRoom('Payer mix')).toBe("Can't add Payer mix. There is no room left on the grid.");
    expect(announce.planned('AR days')).toBe("AR days is coming soon and can't be added yet.");
  });

  it('speaks the whole of a move', () => {
    expect(announce.moveStart('Payer mix', 0, 0)).toBe('Moving Payer mix. Row 1, column 1. Use the arrow keys to move, Enter to drop, Escape to cancel.');
    expect(announce.moveStep(2, 3, 0)).toBe('Row 4, column 3.');
    expect(announce.moveStep(2, 3, 1)).toBe('Row 4, column 3. 1 other metric moves down.');
    expect(announce.moveStep(2, 3, 2)).toBe('Row 4, column 3. 2 other metrics move down.');
    expect(announce.edgeLeft()).toBe('Left edge.');
    expect(announce.edgeRight('Payer mix', 6)).toBe('Right edge. Payer mix is 6 columns wide.');
    expect(announce.edgeTop()).toBe('Top.');
    expect(announce.dropped('Payer mix', 4, 1)).toBe('Payer mix dropped at row 2, column 5.');
    expect(announce.moveCancelled('Payer mix', 0, 0)).toBe('Move cancelled. Payer mix is back at row 1, column 1.');
  });

  it('speaks resizing and presets', () => {
    expect(announce.resizeStart('Payer mix', 6, 3)).toBe('Resizing Payer mix. 6 columns by 3 rows. Use the arrow keys to resize, Enter to confirm, Escape to cancel.');
    expect(announce.resizeStep(6, 3, null)).toBe('6 columns by 3 rows.');
    expect(announce.resizeStep(3, 2, 'min')).toBe('3 columns by 2 rows. Smallest size for this metric.');
    expect(announce.resizeStep(12, 6, 'max')).toBe('12 columns by 6 rows. Largest size for this metric.');
    expect(announce.preset('Payer mix', 'L', 12, 4)).toBe('Payer mix size set to L, 12 columns by 4 rows.');
    expect(announce.optionChanged('Months shown', '12', 'Net cash collected')).toBe('Months shown set to 12. Net cash collected updated.');
  });

  it('speaks removing, undo and redo', () => {
    expect(announce.imageSaved('prism-x.png')).toBe('Image saved as prism-x.png.');
    expect(announce.imageBlocked(2)).toBe("Can't save an image yet. 2 metrics couldn't load. Try again first.");
    expect(announce.imageFailed()).toBe("We couldn't make the image. Try again.");
    expect(announce.arranged(3)).toBe('3 metrics arranged to fill the gaps. Undo is available.');
    expect(announce.alreadyTidy()).toBe('Nothing to tidy. There are no gaps.');
    expect(announce.removed('Payer mix', 2)).toBe('Payer mix removed. 2 metrics left. Undo is available.');
    expect(announce.removed('Payer mix', 1)).toBe('Payer mix removed. 1 metric left. Undo is available.');
    expect(announce.undone('remove Payer mix')).toBe('Undid: remove Payer mix.');
    expect(announce.redone('remove Payer mix')).toBe('Redid: remove Payer mix.');
  });

  it('speaks a filter change once, not once per metric', () => {
    expect(announce.filtersUpdating(6, 'October 2026, air')).toBe('Updating 6 metrics for October 2026, air.');
    expect(announce.filtersDone(6, '10/31/2026')).toBe('All 6 metrics updated. Data through 10/31/2026.');
    expect(announce.filtersDone(1, '')).toBe('All 1 metric updated.');
    expect(announce.filtersPartial(2, 6)).toBe("2 of 6 metrics couldn't load.");
  });

  it('speaks search results, and cuts a long query', () => {
    expect(announce.searchResults(3, 'cash')).toBe('3 metrics match "cash".');
    expect(announce.searchResults(1, 'cash')).toBe('1 metric match "cash".');
    expect(announce.searchResults(0, 'zzz')).toBe('No metrics match "zzz".');
    expect(announce.searchResults(0, 'x'.repeat(100))).toBe(`No metrics match "${'x'.repeat(40)}".`);
  });

  it('speaks modes, saving and sharing', () => {
    expect(announce.editing('My view')).toBe('Editing My view.');
    expect(announce.viewing('My view')).toBe('Viewing My view.');
    expect(announce.dirty()).toBe('You have unsaved changes.');
    expect(announce.saved('My view')).toBe('My view saved.');
    expect(announce.saveFailed('My view')).toBe("We couldn't save My view. Your changes are still here. Try again.");
    expect(announce.conflict('My view')).toBe('My view was changed by someone else while you were editing. Choose how to continue.');
    expect(announce.discarded()).toBe('Changes discarded.');
    expect(announce.invited(1)).toBe('Invitation sent to 1 person.');
    expect(announce.invited(3)).toBe('Invitation sent to 3 people.');
    expect(announce.sharingRemoved()).toBe('Access removed.');
    expect(announce.deleted('My view')).toBe('My view deleted.');
  });
});

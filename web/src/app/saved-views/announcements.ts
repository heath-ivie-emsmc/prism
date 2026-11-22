/** Every live-region message the builder speaks, as pure functions so each string is tested word for word. Rows and columns are counted from 1 for people. */

const plural = (n: number, one: string, many: string): string => `${n} ${n === 1 ? one : many}`;
const metrics = (n: number): string => plural(n, 'metric', 'metrics');

export const announce = {
  added: (title: string, x: number, y: number, size: string, w: number, count: number): string =>
    `${title} added at row ${y + 1}, column ${x + 1}, size ${size}, ${w} columns wide. ${metrics(count)} on this view.`,
  limitReached: (title: string, max: number): string => `Can't add ${title}. A view holds up to ${max} metrics.`,
  noRoom: (title: string): string => `Can't add ${title}. There is no room left on the grid.`,
  planned: (title: string): string => `${title} is coming soon and can't be added yet.`,
  moveStart: (title: string, x: number, y: number): string =>
    `Moving ${title}. Row ${y + 1}, column ${x + 1}. Use the arrow keys to move, Enter to drop, Escape to cancel.`,
  moveStep: (x: number, y: number, pushed: number): string =>
    `Row ${y + 1}, column ${x + 1}.${pushed > 0 ? ` ${plural(pushed, 'other metric moves', 'other metrics move')} down.` : ''}`,
  edgeLeft: (): string => 'Left edge.',
  edgeRight: (title: string, w: number): string => `Right edge. ${title} is ${w} columns wide.`,
  edgeTop: (): string => 'Top.',
  dropped: (title: string, x: number, y: number): string => `${title} dropped at row ${y + 1}, column ${x + 1}.`,
  moveCancelled: (title: string, x: number, y: number): string => `Move cancelled. ${title} is back at row ${y + 1}, column ${x + 1}.`,
  resizeStart: (title: string, w: number, h: number): string =>
    `Resizing ${title}. ${w} columns by ${h} rows. Use the arrow keys to resize, Enter to confirm, Escape to cancel.`,
  resizeStep: (w: number, h: number, limit: 'min' | 'max' | null): string =>
    `${w} columns by ${h} rows.${limit === 'min' ? ' Smallest size for this metric.' : limit === 'max' ? ' Largest size for this metric.' : ''}`,
  preset: (title: string, preset: string, w: number, h: number): string => `${title} size set to ${preset}, ${w} columns by ${h} rows.`,
  optionChanged: (option: string, value: string, title: string): string => `${option} set to ${value}. ${title} updated.`,
  imageSaved: (fileName: string): string => `Image saved as ${fileName}.`,
  imageWaiting: (): string => "Metrics are still loading. Try again in a moment.",
  imageBlocked: (failed: number): string => `Can't save an image yet. ${plural(failed, 'metric', 'metrics')} couldn't load. Try again first.`,
  imageFailed: (): string => "We couldn't make the image. Try again.",
  arranged: (count: number): string => `${metrics(count)} arranged to fill the gaps. Undo is available.`,
  alreadyTidy: (): string => 'Nothing to tidy. There are no gaps.',
  removed: (title: string, left: number): string => `${title} removed. ${metrics(left)} left. Undo is available.`,
  undone: (action: string): string => `Undid: ${action}.`,
  redone: (action: string): string => `Redid: ${action}.`,
  filtersUpdating: (count: number, summary: string): string => `Updating ${metrics(count)} for ${summary}.`,
  filtersDone: (count: number, dataThrough: string): string => `All ${metrics(count)} updated.${dataThrough ? ` Data through ${dataThrough}.` : ''}`,
  filtersPartial: (failed: number, count: number): string => `${failed} of ${metrics(count)} couldn't load.`,
  searchResults: (count: number, query: string): string =>
    count === 0 ? `No metrics match "${query.slice(0, 40)}".` : `${metrics(count)} match "${query.slice(0, 40)}".`,
  editing: (name: string): string => `Editing ${name}.`,
  viewing: (name: string): string => `Viewing ${name}.`,
  dirty: (): string => 'You have unsaved changes.',
  saved: (name: string): string => `${name} saved.`,
  saveFailed: (name: string): string => `We couldn't save ${name}. Your changes are still here. Try again.`,
  conflict: (name: string): string => `${name} was changed by someone else while you were editing. Choose how to continue.`,
  discarded: (): string => 'Changes discarded.',
  invited: (count: number): string => `Invitation sent to ${plural(count, 'person', 'people')}.`,
  sharingRemoved: (): string => 'Access removed.',
  deleted: (name: string): string => `${name} deleted.`,
} as const;

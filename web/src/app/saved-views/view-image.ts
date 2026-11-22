import { toBlob } from 'html-to-image';

/** An element carrying this attribute is left out of a snapshot: buttons and toggles mean nothing in a picture. */
export const SNAPSHOT_SKIP = 'data-snapshot-skip';

const MAX_NAME_LENGTH = 60;

/** The facts printed above a snapshot. Saved views do not keep filters, so the picture has to say what it was showing. */
export interface SnapshotCaption {
  readonly viewName: string;
  readonly providerName: string;
  readonly filters: readonly string[];
  readonly dataThrough: string;
  /** When the picture was taken, as the viewer's local time already formatted. */
  readonly takenAt: string;
}

/** A file name from letters, digits and hyphens only: no path characters, no spaces, nothing a file system or a download dialog could misread. */
export function imageFileName(viewName: string, date: Date): string {
  const slug = viewName
    .normalize('NFKD')
    .replace(/\p{M}/gu, '')
    .replace(/[^A-Za-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, MAX_NAME_LENGTH)
    .replace(/-+$/g, '');
  const day = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
  return `prism-${slug || 'view'}-${day}.png`;
}

/** The caption as lines of text, in the order they are printed. */
export function captionLines(caption: SnapshotCaption): string[] {
  const lines = [caption.viewName, caption.providerName, caption.filters.join(' · ')];
  lines.push(caption.dataThrough ? `Data through ${caption.dataThrough}` : 'Data through date not available');
  lines.push(`Image taken ${caption.takenAt}`);
  return lines.filter((line) => line.length > 0);
}

/** Draws a page region to a PNG. Controls marked with {@link SNAPSHOT_SKIP} are left out, and the background is the page's own, so dark mode comes out dark. */
export async function renderSnapshot(node: HTMLElement): Promise<Blob> {
  const background = getComputedStyle(node.closest('.ems-ui') ?? document.body).backgroundColor;
  const blob = await toBlob(node, {
    pixelRatio: 2,
    cacheBust: false,
    backgroundColor: background && background !== 'rgba(0, 0, 0, 0)' ? background : '#ffffff',
    filter: (element) => !(element instanceof HTMLElement && element.hasAttribute(SNAPSHOT_SKIP)),
  });
  if (!blob) {
    throw new Error('The browser returned no image.');
  }
  return blob;
}

/** Hands a blob to the browser as a download. */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 0);
}

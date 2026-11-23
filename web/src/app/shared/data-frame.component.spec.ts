import { LiveAnnouncer } from '@angular/cdk/a11y';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { axeViolations } from '../axe';
import { DataFrameComponent } from './data-frame.component';

@Component({
  imports: [DataFrameComponent],
  template: `
    <app-data-frame heading="Net cash collected" [expandable]="expandable()" context="October 2026 · Air">
      <button frameActions type="button">Action</button>
      <p class="body">Chart goes here</p>
    </app-data-frame>
  `,
})
class HostComponent {
  readonly expandable = signal(true);
}

@Component({
  imports: [DataFrameComponent],
  template: `<app-data-frame heading="Stamped" [dataThrough]="through()" serviceLine="ground"><p>Body</p></app-data-frame>`,
})
class StampedHostComponent {
  readonly through = signal<string | null>('2026-11-22');
}

/** jsdom has no Fullscreen API, so stand one in: the document remembers which element is full screen and tells the frame with the same event the browser sends. */
function installFullscreen(supported: boolean) {
  let current: Element | null = null;
  const doc = document as unknown as Record<string, unknown>;
  Object.defineProperty(doc, 'fullscreenEnabled', { configurable: true, value: supported });
  Object.defineProperty(doc, 'fullscreenElement', { configurable: true, get: () => current });
  const exit = vi.fn(async () => {
    current = null;
    document.dispatchEvent(new Event('fullscreenchange'));
  });
  doc['exitFullscreen'] = exit;
  const request = vi.fn(async function (this: Element) {
    // eslint-disable-next-line @typescript-eslint/no-this-alias -- the browser records which element asked, and the fake must do the same.
    current = this;
    document.dispatchEvent(new Event('fullscreenchange'));
  });
  if (supported) (HTMLElement.prototype as unknown as Record<string, unknown>)['requestFullscreen'] = request;
  else delete (HTMLElement.prototype as unknown as Record<string, unknown>)['requestFullscreen'];
  return {
    request,
    exit,
    /** What Escape does in a real browser: leave full screen with no click of ours. */
    escape: () => {
      current = null;
      document.dispatchEvent(new Event('fullscreenchange'));
    },
  };
}

function make(): { fixture: ComponentFixture<HostComponent>; root: HTMLElement; button: () => HTMLButtonElement | null; announced: string[] } {
  const fixture = TestBed.createComponent(HostComponent);
  const announced: string[] = [];
  vi.spyOn(TestBed.inject(LiveAnnouncer), 'announce').mockImplementation(async (message) => void announced.push(String(message)));
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  return { fixture, root, button: () => root.querySelector<HTMLButtonElement>('.frame__expand'), announced };
}

describe('DataFrameComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [HostComponent] }));
  afterEach(() => {
    delete (document as unknown as Record<string, unknown>)['fullscreenEnabled'];
    delete (document as unknown as Record<string, unknown>)['fullscreenElement'];
    delete (HTMLElement.prototype as unknown as Record<string, unknown>)['requestFullscreen'];
  });

  it('shows the freshness stamp and what the figures cover only when it is given a date, and passes axe', async () => {
    installFullscreen(true);
    const fixture = TestBed.createComponent(StampedHostComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.frame__foot')?.textContent).toContain('Data through Nov 22');
    expect(root.querySelector('.frame__foot')?.textContent).toContain('Ground');
    expect(await axeViolations(root)).toEqual([]);

    fixture.componentInstance.through.set(null);
    fixture.detectChanges();
    expect(root.querySelector('.frame__foot')).toBeNull();
  });

  it('draws the heading, the actions and the content in one card', () => {
    installFullscreen(true);
    const { root } = make();

    expect(root.querySelector('h2')?.textContent).toContain('Net cash collected');
    expect(root.textContent).toContain('Action');
    expect(root.querySelector('.body')?.textContent).toBe('Chart goes here');
  });

  it('names the expand button after the widget, and says nothing about the context until it is full screen', () => {
    installFullscreen(true);
    const { button, root } = make();

    expect(button()?.getAttribute('aria-label')).toBe('Expand Net cash collected to full screen');
    expect(root.textContent).not.toContain('October 2026 · Air');
  });

  it('draws no button where the browser cannot do full screen', () => {
    installFullscreen(false);
    const { button } = make();

    expect(button()).toBeNull();
  });

  it('draws no button while the layout is being edited', () => {
    installFullscreen(true);
    const { fixture, button } = make();

    fixture.componentInstance.expandable.set(false);
    fixture.detectChanges();

    expect(button()).toBeNull();
  });

  it('asks the browser to put the frame itself in full screen, then shows the way out and what the figures cover', async () => {
    const browser = installFullscreen(true);
    const { fixture, button, root, announced } = make();

    button()!.click();
    await Promise.resolve();
    fixture.detectChanges();

    expect(browser.request).toHaveBeenCalledTimes(1);
    expect(browser.request.mock.contexts[0]).toBe(root.querySelector('app-data-frame'));
    expect(button()?.getAttribute('aria-label')).toBe('Exit full screen');
    expect(root.textContent).toContain('October 2026 · Air');
    expect(announced).toEqual(['Net cash collected full screen. Press Escape to exit.']);
  });

  it('leaves full screen on a second click', async () => {
    const browser = installFullscreen(true);
    const { fixture, button, announced } = make();
    button()!.click();
    await Promise.resolve();
    fixture.detectChanges();

    button()!.click();
    await Promise.resolve();
    fixture.detectChanges();

    expect(browser.exit).toHaveBeenCalledTimes(1);
    expect(button()?.getAttribute('aria-label')).toBe('Expand Net cash collected to full screen');
    expect(announced.at(-1)).toBe('Exited full screen.');
  });

  it('follows Escape, which the browser handles without any click of ours', async () => {
    const browser = installFullscreen(true);
    const { fixture, button, root, announced } = make();
    button()!.click();
    await Promise.resolve();
    fixture.detectChanges();

    browser.escape();
    fixture.detectChanges();

    expect(button()?.getAttribute('aria-label')).toBe('Expand Net cash collected to full screen');
    expect(root.textContent).not.toContain('October 2026 · Air');
    expect(announced.at(-1)).toBe('Exited full screen.');
  });

  it('puts focus back on the button when leaving full screen dropped it to the page', async () => {
    const browser = installFullscreen(true);
    const { fixture, button } = make();
    document.body.appendChild(fixture.nativeElement as HTMLElement);
    button()!.click();
    await Promise.resolve();
    fixture.detectChanges();
    (document.activeElement as HTMLElement | null)?.blur();

    browser.escape();
    fixture.detectChanges();
    await Promise.resolve();

    expect(document.activeElement).toBe(button());
  });

  it('says so, and stays as it was, when the browser refuses', async () => {
    const browser = installFullscreen(true);
    browser.request.mockRejectedValueOnce(new Error('denied'));
    const { fixture, button, announced } = make();

    button()!.click();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    expect(announced).toEqual(["Full screen isn't available here."]);
    expect(button()?.getAttribute('aria-label')).toBe('Expand Net cash collected to full screen');
  });

  it('leaves full screen if it is taken off the page while expanded', async () => {
    const browser = installFullscreen(true);
    const { fixture, button } = make();
    button()!.click();
    await Promise.resolve();
    fixture.detectChanges();

    fixture.destroy();

    expect(browser.exit).toHaveBeenCalledTimes(1);
  });

  it('passes axe, collapsed and expanded', async () => {
    installFullscreen(true);
    const { fixture, button, root } = make();
    expect(await axeViolations(root)).toEqual([]);

    button()!.click();
    await Promise.resolve();
    fixture.detectChanges();
    expect(await axeViolations(root)).toEqual([]);
  });
});

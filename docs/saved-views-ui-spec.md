# Saved Views: UI and accessibility contract

The design contract for the saved views pages, written before they were built and updated to what shipped. The target is WCAG 2.2 AA (ADA and Section 508).
Tests for each rule are in `web/e2e/` (`saved-views.spec.ts`, and the saved views blocks of `layout.spec.ts`, `accessibility.spec.ts` and `keyboard.spec.ts`)
and in `web/src/app/saved-views/*.spec.ts`.

## Layout and responsive

- One `main`, one h1 (the view name), no heading skips. Order: skip links, breadcrumbs, header, filter bar, builder. Widget titles are h2, palette categories h3.
- The palette is docked as an 18rem left pane from a 75rem (1200px) viewport. Below that the same palette opens in a dialog ("Add metrics") that stays open for
  several adds and scrolls inside itself so Done stays on screen.
- The grid is `repeat(12, minmax(0, 1fr))` while the canvas is at least 56rem wide (a container query, in rem so larger text switches sooner) and one column
  below that, in reading order (top to bottom, then left to right). Row heights are minimums, never fixed, so nothing clips at 200% text.
- Presets: S 3x2, M 6x3, L 12x4, narrowed per presentation by the catalog. Vertical compaction is on; a collision pushes the other widget down and says so.
- Density: comfortable or compact (6rem rows, half the gap). Targets stay at least 24px, 44px for a coarse pointer.
- Forced colors, reduced motion, dark mode and 400% zoom are covered by browser tests. Under forced colors the ghost and moving outlines use system colors.

## Four ways to place and size a widget (WCAG 2.5.7)

1. **Add** on any palette entry places it in the first free slot.
2. **Pointer drag** (grid mode only) from the palette grip or a widget's Move handle. The ghost says in words where it will land ("Drop here: row 2, column 4").
3. **Keyboard Move mode**: Enter or click on Move, arrow keys (one cell), PageUp and PageDown (four rows), Home and End, Enter to drop, Escape to cancel. Tab or
   leaving the button cancels, so it never traps. Ctrl+Z and Ctrl+Shift+Z undo and redo (not inside a text field).
4. **Settings pane**: size presets, width and height steppers, column and row steppers, and four nudge buttons. This is the guaranteed path for anyone whose
   screen reader keeps the arrow keys. In the one-column layout each widget also has Move up and Move down.

## Components and ARIA

- Palette: `role="search"`, categories as headings containing a button with `aria-expanded`, entries with an Add button named "Add {metric} to view". A planned
  entry stays focusable with `aria-disabled`, a "Coming soon" chip and a described-by reason. The dashboard-only insight is not listed.
- Canvas: a list in reading order, not `role="grid"` (irregular spans would clash with the arrow keys in Move mode).
- Widget toolbar (edit mode): `role="toolbar"`, one Tab stop, Left, Right, Home and End move along it. Move, Size (S, M, L menu), Settings and Remove are each
  named with the widget's title; two widgets of one Metric are "(1 of 2)" and "(2 of 2)".
- Dialogs use `EmsDialog` (focus trap, Escape, focus returns to the trigger). The conflict dialog starts on "Save as a copy".

## Announcements

`announcements.ts` builds every live-region string (add, move start, step, edge, drop, cancel, resize, preset, remove with undo, save, conflict, share). One
message says the metrics are updating when a filter changes and one says they are done, not one per widget. Each string is unit tested word for word.

## Focus

Adding moves focus to the new widget's Move button. Removing moves it to the next widget, then the previous, then the empty-canvas button. Settings opens on its
heading and Done returns to the Settings button. Saving keeps focus where it is. The page's h1 takes focus on arrival.

## Charts

Every chart has an `aria-label` of 250 characters or fewer and the same numbers as a real table one control away (caption, `th scope`, a focusable scroll
region). Series differ by shape and dash as well as colour, decals turn on from four series, and ECharts' own generated description is off.

## Configurability

Per widget: presentation, the options the catalog lists for it (a new option is catalog data, and the control follows from its type), size and position. Per view:
name, description, density. Per viewer, not saved: Chart or Table. Not configurable in v1: filters per widget, custom titles, colours, auto-refresh, export,
per-user defaults.

## What differs from the first design

- On a screen narrower than 75rem the widget settings appear in a panel above the canvas, not in a dialog.
- There is no separate keyboard resize mode. Size is set with the Size menu and the width and height steppers in the settings pane.
- The palette's categories are plain buttons with `aria-expanded`, not `@angular/cdk/accordion`.
- A `LabelService` was not added. The Reporting Division wording still comes from `defaultLabels`.
- Sharing is by emailed invitation, so the share dialog takes addresses rather than picking users.

## Needs a person

Screen reader passes (NVDA with Chrome, JAWS, VoiceOver on macOS and iOS, TalkBack) for Move mode and the touch long-press drag cannot be automated.

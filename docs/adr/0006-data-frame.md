# 6. Data frame: one container for data on screen, with full screen

Status: accepted

## Context

Charts and tables were each drawn in their own card: the library's `ems-card` on Saved views, and hand-written panels on Net cash and Payer mix. Users who want a larger chart had no way to get one, and any behaviour every data view should share would have had to be added three times.

## Decision

- **`app-data-frame` wraps every data widget.** It is the library card with the heading, a button at the top left of the header, a slot for actions at the top right, and the content. Saved view widgets, the Net cash panel, the Payer mix panel and the two KPI tiles with a picture (Transports, Loaded miles) use it. Clean claim rate and Open negotiation have no picture worth enlarging, Insights and the needs-attention table are not charts, so they do not.
- **Full screen uses the browser's Fullscreen API on the frame itself.** The element is not moved, so a chart keeps its state and only resizes. Escape is the browser's own. The button follows the `fullscreenchange` event, never the click, so Escape and the browser's own controls stay in step. A CSS overlay was rejected: `.app-panel` uses `backdrop-filter` and the grid wrapper uses `container-type`, and both make a fixed child position against the card instead of the window.
- **The frame says what it covers when it is full screen** (period and Service Line), because the page filters are out of sight then.
- **The button is not drawn where the browser cannot do it.** iPhone Safari has no element full screen, so there is no icon there rather than a dead one. iPad and Android work.
- **Not expandable while a layout is being arranged.**
- **The frame never changes what data a widget loads.** It owns chrome and behaviour only.

## Consequences

- Menus and dialogs open in the CDK overlay container, outside the fullscreen element, so they would not show over a full screen frame. Nothing inside a frame opens one today; if something must, the overlay container has to move into the fullscreen element first.
- Headless Chromium does not act on a synthetic Escape key for full screen, so the browser tests leave full screen through the button or `exitFullscreen()`. The Escape path is covered in the component test through the same `fullscreenchange` event the browser sends.
- Loading, error and empty states, the Data Through footer and download are the likely next behaviours to move into the frame.

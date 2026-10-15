# Documentation screenshots

These images show the actual KeeStashDock WPF views in English with synthetic
sample data. Light theme is used except where noted. They were rendered in memory
on Windows for versions 1.2.1 and 1.2.2 as identified below. Revision-4 previews
show inward perspective, layered cards, fixed presentation and pointer feedback.
Window title bars are omitted; no real desktop or clipboard content was captured.

| File | View |
| --- | --- |
| `main-window.png` | Workbench with sample text, link, and image entries |
| `screenshot-entry.png` | Image entry with an original sample note and editable sample OCR text |
| `component-setup.png` | Settings' optional components section in the initial not-installed state (1.2.1) |
| `classic-panel.png` | Quick panel with soft exterior/card shadows and synthetic snippets (1.2.2) |
| `floating-panel.png` | The ordinary rounded panel with layered cards and proximity feedback (1.2.2 revision 4) |
| `floating-panel-dark.png` | The same perspective and layered cards using the dark palette (1.2.2 revision 4) |
| `floating-panel-fixed.png` | Fixed perspective with independent card feedback (1.2.2 revision 4) |
| `floating-panel-motion.gif` | The actual WPF panel and cards responding along a synthetic pointer route: 56 frames at 80 ms per frame (1.2.2 revision 4) |
| `selection-actions.png` | Combined Copy / Translate selection bubble (1.2.2) |
| `screenshot-text.png` | Transient screenshot text result before any explicit copy action (1.2.2) |
| `update-check.png` | Actual update dialog showing a simulated newer version and example installation path (1.2.2) |

To refresh the three 1.2.1 images, load the application theme and styles in an isolated STA WPF
process, create a temporary sample database, and instantiate `WorkbenchWindow`,
`ImageEntryWindow`, and `SettingsWindow`. Use the existing test seams for
component status and shortcut registration so no installation, service, global
hook, or user setting is changed. Lay out the actual controls and render them
with `RenderTargetBitmap` at twice their logical resolution. Review the PNGs and
remove the temporary database and build output afterward.

The sample note artwork and text were created for this repository and are
included under its [Apache License 2.0](../../LICENSE). The sample OCR field is
seeded for presentation; these images are not OCR accuracy test results.

The 1.2.2 images are reproducible through the existing isolated WPF regression
scenario. Set `KEESTASHDOCK_DOC_IMAGES` to an absolute output directory, build,
and run `DesktopWindowWorkflows_PreserveSettingsDraftsAndVisibility`. The panel
is drawn over a neutral fixture backdrop. The selection and OCR result views use
synthetic text; their actual content is placed in a neutral padded container and
rendered directly at 96 DPI without scaling a `VisualBrush`. The update dialog
uses an in-memory HTTP fixture: its newer version, release notes and displayed
`C:\Apps\KeeStashDock (example)` path are illustrative, not a published release
or the maintainer's real installation. Generating that preview does not contact
GitHub, download an update, or run an installer.

The same scenario generates `floating-panel-motion.gif` using the actual panel
and its shadow window. A programmed sequence supplies synthetic pointer positions
to the panel's input seam; a drawn pointer marks those positions in each frame.
The route visits outside edges and the cards, showing inward panel tilt and
independent card brightness/lift. Fixed mode retains a nonzero panel angle while
keeping the same card feedback.
The 56 frames play at 80 ms each and loop, for a 4.48-second cycle. The frames are
rendered directly from the live WPF controls with synthetic notes, not recorded
from the user's desktop. The scenario does not move the operating-system cursor.

These previews demonstrate appearance; they do not establish physical
mixed-DPI coverage or animation performance on every GPU.

Floating mode retains the application's ordinary rounded surfaces, palette,
fonts and spacing. Its whole-panel perspective movement follows the maintainer's
reference screenshots of a spatial game interface. The motion preview uses
the application's live WPF controls and synthetic pointer positions to demonstrate
that transformation. No game artwork, logo, screenshot or proprietary font is
bundled or traced.

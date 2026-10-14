# Documentation screenshots

These images show the actual KeeStashDock WPF views in English with the light
theme and synthetic sample data. They were rendered in memory on Windows for
versions 1.2.1 and 1.2.2 as identified below. Window title bars are omitted; no real desktop or clipboard
content was captured.

| File | View |
| --- | --- |
| `main-window.png` | Workbench with sample text, link, and image entries |
| `screenshot-entry.png` | Image entry with an original sample note and editable sample OCR text |
| `component-setup.png` | Settings' optional components section in the initial not-installed state (1.2.1) |
| `classic-panel.png` | Quick panel with soft exterior/card shadows and synthetic snippets (1.2.2) |
| `floating-panel.png` | The same panel with Floating enabled and a bounded first-card hover pose (1.2.2) |
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

These previews demonstrate appearance; they do not establish physical
mixed-DPI coverage or animation performance on every GPU.

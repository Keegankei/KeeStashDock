# Documentation screenshots

These images show the actual KeeStashDock WPF views in English with the light
theme and synthetic sample data. They were rendered in memory on Windows for
version 1.2.1. Window title bars are omitted; no real desktop or clipboard
content was captured.

| File | View |
| --- | --- |
| `main-window.png` | Workbench with sample text, link, and image entries |
| `screenshot-entry.png` | Image entry with an original sample note and editable sample OCR text |
| `component-setup.png` | Settings' optional components section in the initial not-installed state |

To refresh them, load the application theme and styles in an isolated STA WPF
process, create a temporary sample database, and instantiate `WorkbenchWindow`,
`ImageEntryWindow`, and `SettingsWindow`. Use the existing test seams for
component status and shortcut registration so no installation, service, global
hook, or user setting is changed. Lay out the actual controls and render them
with `RenderTargetBitmap` at twice their logical resolution. Review the PNGs and
remove the temporary database and build output afterward.

The sample note artwork and text were created for this repository and are
included under its [Apache License 2.0](../../LICENSE). The sample OCR field is
seeded for presentation; these images are not OCR accuracy test results.

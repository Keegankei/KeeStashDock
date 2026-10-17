# Documentation screenshots and GIFs

These previews use the actual **KeeStashDock 1.2.2 WPF views**, rendered on
Windows with an isolated sample database and English interface. The complete
set was refreshed for the final 1.2.2 documentation; it contains 14 PNGs and
two GIFs. No personal desktop, clipboard, collection, or installed application
settings are captured.

The Standard panel and Settings previews use fresh-install defaults. The theme
setting remains **System**, with a deterministic light host palette for clear
documentation; the dark image demonstrates an optional theme. Window dimensions
are adjusted for presentation. Spatial mode and the combined Copy / Translate
bubble demonstrate optional settings, rather than the default Standard mode and
Copy-only bubble. Native window title bars are omitted.

| File | View |
| --- | --- |
| `standard-panel-workflow.gif` | Standard panel: type a sample note, save it, pin and unpin its card, then remove the example |
| `main-window.png` | Workbench with sample text, link, and image entries, groups, and tags |
| `default-settings.png` | Fresh-install language, shortcut, and optional component settings in English, including Alt+Q and Alt+W |
| `behavior-settings.png` | Fresh-install behavior settings, including Copy-only selection, Google translation, text glow off, and bubble shadow on |
| `default-appearance.png` | Fresh-install appearance settings, including Standard mode and panel opacity |
| `backup-settings.png` | Data and About sections with automatic backups every 24 hours and the data path masked |
| `screenshot-entry.png` | Saved image entry with an original sample note and editable, prefilled example OCR text |
| `component-setup.png` | Optional OCR and offline translation components in their fresh-install, not-installed state |
| `classic-panel.png` | Standard quick panel with its default appearance, real soft shadows, and synthetic snippets |
| `floating-panel.png` | Optional Spatial mode with perspective, layered cards, a thin side layer, and proximity feedback |
| `floating-panel-dark.png` | Optional Spatial mode using the dark palette |
| `floating-panel-fixed.png` | Optional Spatial mode with fixed perspective and independent card feedback |
| `floating-panel-motion.gif` | Spatial card feedback followed by drag inspection, marked by a simulated pointer |
| `selection-actions.png` | Optional combined Copy / Translate selection bubble |
| `screenshot-text.png` | Temporary screenshot text result before an explicit copy action |
| `update-check.png` | Update dialog with a simulated newer release and a masked installation path |

## Privacy treatment

All notes, links, images, and paths come from isolated fixtures. No account
credentials, usernames, real installation paths, or personal content are used.
The update dialog replaces its displayed path with `[hidden for privacy]` before
rendering. The Data section additionally covers the entire sample path line with
an opaque block labelled `Data: [path hidden for privacy]`; the original path
pixels are absent from the exported PNG. These privacy edits do not alter the
application's real settings or I/O paths.

The sample note artwork and text were created for this repository and are
included under its [Apache License 2.0](../../LICENSE). Recognized text is
prefilled for presentation; these images are not OCR accuracy results. No cloud
translation, component download, or real update is needed to generate the
previews. The release shown in the update dialog is an in-memory HTTP fixture,
not an announcement of an available release.

## Reproduction

On Windows, set `KEESTASHDOCK_DOC_IMAGES` to an absolute temporary output directory,
build the solution, and run the existing regression scenario
`DesktopWindowWorkflows_PreserveSettingsDraftsAndVisibility`. Remove the
environment variable afterward. Review all generated images and GIF frames
before copying them into this directory.

The scenario loads the application's theme and styles in its isolated STA WPF
test host. `DocumentationPreviewScenarios` instantiates the actual workbench,
image-entry, and Settings views using a temporary database and fresh settings.
Component actions and shortcut registration use existing test seams; the
preview does not install components, register global shortcuts, or change the
user's startup registration. Settings sections are cropped from their real
controls. The remaining previews use the corresponding panel, selection,
screenshot-result, and update test scenarios.

Views are rendered with `RenderTargetBitmap` at 96 DPI. The quick panel is drawn
over a neutral backdrop so its actual shadow is visible. The selection and
screenshot-result content is placed in a padded container without scaling a
`VisualBrush`. All data and settings remain inside the disposable fixture.

`StandardPreviewRecorder` drives the real quick-input binding and save, pin,
unpin, and Trash commands. It records 40 frames at 200 ms each, forming an
8-second loop. The distributed GIF merges consecutive identical frames into
17 encoded frames while preserving their total display time. It does not invoke
Copy or alter the system clipboard. The transient note and draft are cleaned up
after recording.

`PerspectivePreviewRecorder` supplies a programmed pointer route to the panel's
input seam. A drawn white pointer demonstrates nearby-card brightness and lift;
an orange pointer marks held drag inspection. Releasing preserves the angle.
Its 56 frames use an 80 ms delay, forming a 4.48-second loop. The recorder does
not move the operating-system cursor. These are scripted demonstrations of the
actual controls, not recordings of the user's desktop or frame-rate benchmarks.

The optional [GIF optimizer](../../tools/documentation/optimize_gifs.py) requires
Pillow and accepts separate source and destination directories. Substitute your
render and output folders in this example:

```powershell
python tools/documentation/optimize_gifs.py "artifacts/readme-render" "artifacts/readme-optimized"
```

It rejects in-place replacement and existing output files, then verifies that
the RGB pixels and display time of each visible state are unchanged. The checked-in
Standard and Spatial GIFs are 346,686 and 4,023,388 bytes respectively, with
8-second and 4.48-second loops. Their optimized playback was checked against
the original renders.

## Visual scope

The previews demonstrate appearance and selected workflows. They do not
establish physical mixed-DPI coverage or animation performance on every GPU.
See the [release acceptance record](../releases/1.2.2.md) for verified application
coverage and remaining limits.

Spatial mode retains the application's rounded surfaces, palette, fonts, and
spacing. Its perspective design follows the maintainer's references for a
spatial game interface. No game artwork, logo, screenshot, or proprietary font
is bundled or traced.

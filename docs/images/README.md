# Documentation screenshots and GIFs

These previews use the actual **KeeStashDock 1.2.3 WPF views**, rendered on
Windows with an isolated sample database and English interface on **2026-10-19**.
The gallery contains **21 PNGs and two GIFs**. Application source matches the
published `v1.2.3` tag; documentation fixtures provide sample content and capture
the views without launching the user's normal application session. No personal
desktop, clipboard, collection, or installed application settings are captured.

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
| `default-settings.png` | General settings with English, startup launch off, Alt+Q and Alt+W |
| `translation-settings.png` | Default Copy-only selection, Google, Follow interface language, and both result-tool switches enabled |
| `default-appearance.png` | Fresh-install appearance settings, including Standard mode and panel opacity |
| `backup-settings.png` | Backup scheduling every 24 hours and the redacted backup folder |
| `backup-status.png` | Backup status, snapshot list, manual actions and export/import controls in the isolated sample |
| `about-settings.png` | Version 1.2.3, update entry point and redacted app-data location |
| [settings-dark.png](settings-dark.png) | Optional dark appearance of General settings |
| `screenshot-entry.png` | Saved image entry with an original sample note and editable, prefilled example OCR text |
| `component-setup.png` | Optional OCR and offline translation components in their fresh-install, not-installed state |
| `component-folders.png` | Scrolled component page showing the offline translation folder and folder actions; paths redacted |
| `classic-panel.png` | Standard quick panel with its default appearance, real soft shadows, and synthetic snippets |
| `floating-panel.png` | Optional Spatial mode with perspective, layered cards, a thin side layer, and proximity feedback |
| `floating-panel-dark.png` | Optional Spatial mode using the dark palette |
| `floating-panel-fixed.png` | Optional Spatial mode with fixed perspective and independent card feedback |
| `floating-panel-motion.gif` | Spatial card feedback followed by drag inspection, marked by a simulated pointer |
| `selection-copy-only.png` | Default Copy-only selection bubble |
| `selection-actions.png` | Optional combined Copy / Translate selection bubble |
| `screenshot-text.png` | Prefilled temporary OCR result with the 1.2.3 group and source-saving controls |
| `translation-result-tools.png` | Prefilled translation with target and save tools enabled by default; German is an illustrative temporary target |
| `translation-result-compact.png` | The same result with both optional tool panels hidden |
| `update-check.png` | Update dialog with a simulated newer release and a masked installation path |

## Privacy treatment

All notes, links, images, and paths come from isolated fixtures. No account
credentials, usernames, real installation paths, or personal content are used.
The update dialog replaces its displayed path with `[hidden for privacy]` before
rendering. Component, backup and app-data fields use `[Path hidden for privacy]`,
and the backup status location is redacted separately. The original path pixels
are absent from the exported PNGs, including the scrolled page captures. These
privacy edits do not alter the application's real settings or I/O paths.

The sample note artwork and text were created for this repository and are
included under its [Apache License 2.0](../../LICENSE). Recognized and translated
text is prefilled for presentation; these images are not OCR or translation
accuracy results. No cloud
translation, component download, or real update is needed to generate the
previews. The release shown in the update dialog is an in-memory HTTP fixture,
not an announcement of an available release.

## Reproduction

On Windows, run these commands from the repository root with the pinned .NET
SDK. Choose a fresh output directory; `publish/` is ignored by Git.

```powershell
dotnet build tests/KeeStashDock.Desktop.Tests/KeeStashDock.Desktop.Tests.csproj -c Release
$env:KEESTASHDOCK_DOC_IMAGES = Join-Path (Get-Location).Path 'publish/documentation-1.2.3/raw'
try {
    dotnet test --project tests/KeeStashDock.Desktop.Tests/KeeStashDock.Desktop.Tests.csproj -c Release --no-build --filter FullyQualifiedName~DesktopWindowWorkflows_PreserveSettingsDraftsAndVisibility
} finally {
    Remove-Item Env:KEESTASHDOCK_DOC_IMAGES -ErrorAction SilentlyContinue
}
```

Review every PNG and GIF timeline before copying the English assets into this
directory. The scenario also emits Chinese and Arabic layout checks, which are
not included in this English gallery. Keep only the files listed above; the
obsolete `behavior-settings.png` alias has been replaced by the actual
`translation-settings.png` page.

The scenario loads the application's theme and styles in its isolated STA WPF
test host. `DocumentationPreviewScenarios` instantiates the actual workbench,
image-entry, and Settings views using a temporary database and fresh settings.
Component actions and shortcut registration use existing test seams; the
preview does not install components, register global shortcuts, or change the
user's startup registration. Component folder resolution is scoped to the
disposable fixture so the controls retain their normal enabled state. Settings
pages and scrolled sections use their real controls. The remaining previews use the corresponding panel, selection,
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
Standard and Spatial GIFs are 348,924 and 4,004,152 bytes respectively, with
8-second and 4.48-second loops. Their optimized playback was checked against
the original renders.

## Visual scope

The previews demonstrate appearance and selected workflows. They do not
establish physical mixed-DPI coverage or animation performance on every GPU.
See the [release acceptance record](../releases/1.2.3.md) for verified application
coverage and remaining limits.

Spatial mode retains the application's rounded surfaces, palette, fonts, and
spacing. Its perspective design follows the maintainer's references for a
spatial game interface. No game artwork, logo, screenshot, or proprietary font
is bundled or traced.

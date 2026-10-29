# 1.3.0 interface previews

These seven PNGs render actual KeeStashDock 1.3.0 WPF controls with the English
interface, 100% text size, isolated settings and fictional sample content. They
were selected from the local theme-dialog and r5 layout checks performed on
2026-10-23 through 2026-10-25. All four selection/result previews were refreshed
on 2026-10-28 after removing bubble frosting, text glow and shadows, before
publication. The Settings background-category preview was refreshed on
2026-10-29. They do not capture the user's desktop, clipboard, collection or
credentials.

| File | View and deliberate settings |
| --- | --- |
| `workbench-light.png` | Template editor and collection, optional Wisteria Atelier Light |
| `theme-preview-light.png` | Wisteria Atelier Light preview with fixed Apply/Cancel footer |
| `settings-frosted-dark.png` | Scrolled Appearance page with Settings background category selected, optional Wisteria Dark, 60% Settings opacity and 8 px artwork blur |
| `selection-default.png` | Default bubble styling with optional combined Copy/Translate mode |
| `selection-wisteria-dark.png` | The same optional combined actions in Wisteria Dark |
| `translation-default.png` | Compact Default result, prefilled English/Chinese example text, language and save controls enabled |
| `translation-wisteria-dark.png` | The same prefilled result in Wisteria Dark |

The Chinese text in result previews is example content; interface labels remain
English. No translation or OCR provider was invoked. The Settings preview is
scrolled intentionally to show opacity and blur controls. The theme preview's
`v1.0.0` identifies the **theme package**, not the application version.

Fresh installs remain image-free and Copy-only, with Settings frosting off. These
previews are offscreen control renders, not proof of display/GPU performance,
physical DPI coverage or clean-machine acceptance. Text and controls stay sharp
while the Settings artwork layer is blurred. Native title bars are not captured.

## Reproduction

Use a fresh ignored artifact folder on Windows with the repository's .NET SDK:

```powershell
dotnet build tests/KeeStashDock.Desktop.Tests/KeeStashDock.Desktop.Tests.csproj -c Release
$env:KEESTASHDOCK_THEME_SOURCE = Join-Path (Get-Location).Path 'theme'
$env:KEESTASHDOCK_THEME_PREVIEWS = Join-Path (Get-Location).Path 'artifacts/docs-1.3.0/settings'
$env:KEESTASHDOCK_THEME_DIALOG_PREVIEWS = Join-Path (Get-Location).Path 'artifacts/docs-1.3.0/dialog'
$env:KEESTASHDOCK_BUBBLE_PREVIEWS = Join-Path (Get-Location).Path 'artifacts/docs-1.3.0/bubbles'
try {
    dotnet test --project tests/KeeStashDock.Desktop.Tests/KeeStashDock.Desktop.Tests.csproj -c Release --no-build --filter FullyQualifiedName~DesktopWindowWorkflows_PreserveSettingsDraftsAndVisibility
} finally {
    Remove-Item Env:KEESTASHDOCK_THEME_SOURCE, Env:KEESTASHDOCK_THEME_PREVIEWS, Env:KEESTASHDOCK_THEME_DIALOG_PREVIEWS, Env:KEESTASHDOCK_BUBBLE_PREVIEWS -ErrorAction SilentlyContinue
}
```

The opt-in fixtures use `RenderTargetBitmap` on isolated WPF controls and do not
show a native application window. Settings path fields are masked before
rendering. Review every image before replacing a documentation asset. Theme
picker contents can expand as new presets are added; these selected images
illustrate the named preset rather than the complete picker catalog.

| Documentation file | Fixture output |
| --- | --- |
| `workbench-light.png` | `v130-theme-workbench-light-100.png` |
| `theme-preview-light.png` | `wisteria-light-100.png` |
| `settings-frosted-dark.png` | `v130-theme-settings-opacity60-frost8-dark-100.png` |
| `selection-default.png` | `selection-default-100.png` |
| `selection-wisteria-dark.png` | `selection-wisteria-dark-100.png` |
| `translation-default.png` | `result-default-100.png` |
| `translation-wisteria-dark.png` | `result-wisteria-dark-100.png` |

Existing workflow GIFs remain in the parent folder and are explicitly labeled
1.2.3. No GIF has been relabeled as a new recording. See the
[release validation record](../../releases/1.3.0.md) for current test coverage.

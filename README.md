<h1 align="center">KeeStashDock</h1>

<p align="center">
  <img src="docs/assets/app-icon.png" width="112" alt="KeeStashDock application icon">
</p>

<p align="center">
  <strong>A portable Windows dock for snippets, screenshots, and text reuse.</strong><br>
  Capture what you need, organize it locally, and translate when you choose.
</p>

<p align="center">
  <a href="README.md" lang="en">English</a> ·
  <a href="README.zh-CN.md" lang="zh-Hans">简体中文</a> ·
  <a href="README.zh-TW.md" lang="zh-Hant">繁體中文</a> ·
  <a href="README.ja-JP.md" lang="ja">日本語</a> ·
  <a href="README.ko-KR.md" lang="ko">한국어</a> ·
  <a href="README.de-DE.md" lang="de">Deutsch</a> ·
  <a href="README.fr-FR.md" lang="fr">Français</a> ·
  <a href="README.es-ES.md" lang="es">Español</a> ·
  <a href="README.pt-BR.md" lang="pt-BR">Português (Brasil)</a>
</p>

<p align="center">
  <img alt="Version 1.3.0" src="docs/assets/badges/version.svg">
  <img alt=".NET 10" src="docs/assets/badges/dotnet.svg">
  <img alt="Windows 10 and 11" src="docs/assets/badges/windows.svg">
  <img alt="Apache-2.0 license" src="docs/assets/badges/license.svg">
</p>

**Version 1.3.0** · [Release notes](docs/releases/1.3.0-notes.md) · [Validation and release status](docs/releases/1.3.0.md)

**Release preparation:** this branch documents 1.3.0. Formal publication is pending; [Latest release](https://github.com/Keegankei/KeeStashDock/releases/latest) currently provides **1.2.3**. The validation record tracks the remaining release checks.

New previews show the **1.3.0 English interface** with isolated sample data. Older screenshots and GIFs remain labeled **1.2.3**; illustrated themes and combined bubble actions are optional.

[Get started](#get-started) · [Features](#features) · [Shortcuts](#shortcuts) · [Data and upgrades](#data-and-upgrades) · [Development](#development) · [License](#license)

<a id="get-started"></a>
## Get started

**[Download the latest published release](https://github.com/Keegankei/KeeStashDock/releases/latest)**

The 1.3.0 release will provide `KeeStashDock-1.3.0-win-x64.zip` and `KeeStashDock-1.3.0-win-x64.zip.sha256` under **Assets**. These filenames describe the planned release files, not an already published download.

1. Download the application ZIP under **Assets**, rather than GitHub's developer-oriented **Source code** archive.
2. Extract the complete package into a writable folder and run `KeeStashDock.exe`. Keep all accompanying folders together.
3. Press **Alt+Q** to show the quick panel. Type a snippet and press **Enter** to save it; click its card to copy it.
4. Open **Settings** from the panel or system tray to select a language, theme, and optional components.

The Windows x64 package includes .NET; a separate .NET installation is unnecessary. Windows 11 is the primary target, with best-effort Windows 10 22H2 compatibility. OCR and offline translation may need Microsoft's Visual C++ runtime, which the in-app component installer can install. See the [1.3.0 validation record](docs/releases/1.3.0.md) for tested coverage and outstanding checks.

<details>
<summary>Verify the downloaded ZIP</summary>

Run this in the download folder and compare the result with the accompanying `.sha256` file:

```powershell
Get-FileHash .\KeeStashDock-1.3.0-win-x64.zip -Algorithm SHA256
```

A checksum checks file integrity; it is not a publisher signature. The current package is unsigned.

</details>

**Fresh-install defaults · 1.3.0:** English; **Alt+Q** opens the panel and **Alt+W** starts a screenshot. OCR and offline translation are not preinstalled. Existing saved preferences are preserved on upgrade.

The selection bubble is enabled in **Copy only** mode with **Google** selected for translation, Default styling, text glow off and drop shadow on. Startup launch, automatic clipboard collection and direct paste are off. Edge auto-hide is on, with **2 s** collapse and **0.1 s** expansion delays. Appearance starts with the system theme, **Standard** mode, right-button **Drag to inspect**, automatic text colors and **90%** panel-controls opacity. There is no background image; overlay opacity is **20%** and frosting is off with a stored **4 px** radius. Scheduled backups are enabled every **24 hours**.

Text size starts at **100%**. Settings surface opacity is **100%**; Settings and bubble frosting are **off**. Wisteria Atelier is included for optional use and is not applied automatically.

<details>
<summary>Fresh-install settings · 1.2.3 English preview</summary>

<p align="center"><img src="docs/images/default-settings.png" width="720" alt="The 1.2.3 Settings page with English, Alt+Q and Alt+W defaults"></p>

</details>

<a id="features"></a>
## Features

### New in 1.3.0

| Area | Workflow |
| --- | --- |
| Quick-panel search | With the panel active, press **Ctrl+F**. Search titles and stored text, including existing OCR text, and combine group, tag and text/image/template filters. Results load in pages of 50. Use **Enter** to copy a selected result and **Esc** to return to collecting; the input draft is preserved. |
| Batch organization | Select workbench entries to move groups, add/remove tags, pin/unpin, move to Trash or restore. **Ctrl+A selects only loaded results.** Each batch commits together; conflicts leave every item unchanged. A move to Trash can be undone while its items remain unchanged. |
| Text templates | Explicitly mark a text entry as a template. Use `{{date}}`, `{{time}}` and named variables such as `{{name}}`; fill in values, preview, then copy. The original stays intact, and no scripts or expressions run. Group packages containing templates use format v3. |
| Complete themes | Preview and apply coordinated light/dark palettes and backgrounds across app panels. Import or export both variants and their assets as one theme ZIP. Themes are independent of Standard/Spatial mode; cancelling a preview restores the previous appearance. |
| Global text size | Adjust **Settings > Appearance > Text size** from **90% to 150%** for the quick panel, workbench, Settings and other app text. **100%** restores the original size. |
| Workbench appearance | Refreshed navigation, item list and editor surfaces follow the Settings design. Dark-mode list surfaces and backup rows use matching backgrounds and readable text. |
| Compact bubbles | Inline source/result copy actions and a tighter target-language/group/save layout. Default styling and Wisteria Atelier use the same controls with coordinated palettes. |
| Opacity and frosting | Adjust Settings surfaces independently from **10% to 100%**. Optional Settings frosting softens background artwork at **1–18 px**; optional bubble frosting uses Windows composition with an opaque fallback. Text stays sharp. |

Fresh installs still use English, the system theme, Standard mode and no background image. An upgrade does not automatically apply an illustrated theme or reset saved preferences.

**Settings:** use the category navigation for **General**, **Capture & translation**, **Appearance**, **Local components**, **Backup & data**, and **About**. Dependent options appear when relevant; hiding them preserves their saved values.

Under **Capture & translation**, independently show or hide the result's target-language selector and save-to-group controls. Both are on by default and appear after OCR or translation finishes. Turning them off keeps the result and copy actions available.

Choose the backup folder under **Backup & data**, and separate OCR and offline-translation folders under **Local components**. Changing a folder does **not move existing files**; install the component again if its new folder is empty. The active app-data folder is shown read-only under **About**; use export/import to move the library safely.

The **1.3.0** workbench, theme and bubble previews below are offscreen renders of actual WPF controls with fictional content. The remaining **1.2.3** screenshots and GIFs demonstrate existing workflows. The inspection GIF uses a simulated pointer, and the update preview uses a simulated release. Installation and data paths are masked; no personal desktop or clipboard content appears. [Image sources and reproduction](docs/images/README.md).

<p align="center"><img src="docs/images/standard-panel-workflow.gif" width="400" alt="Standard panel workflow in 1.2.3: type a snippet, save it, then pin and unpin its card"></p>

*1.2.3 · Standard mode · type, save, pin and unpin · English interface. Scripted interaction with actual controls and sample content.*

### 1. Capture, find, and organize

<p align="center"><img src="docs/images/1.3.0/workbench-light.png" width="900" alt="1.3.0 workbench with search, multi-selection and a template editor, using the optional Wisteria Atelier light theme"></p>

*1.3.0 · Workbench and text-template editor · Optional Wisteria Atelier light theme.*

Use the quick panel for short captures and the **workbench** for managing your collection. Open the workbench with the panel's expand button or from the tray.

| Tool | What you can do |
| --- | --- |
| Quick input and search | Save text with Enter; insert line breaks with Alt+Enter or Shift+Enter. Press Ctrl+F in the quick panel to search; both the panel and workbench support group, tag and type filters. |
| Drag into the dock | Drop text to save it, or drop files and folders to save their paths. Click a path card to open its target; right-click **Copy** to copy the path. File contents are not imported. |
| Groups, tags, and pinning | Keep related entries together, edit their title/text/tags, and pin frequently used content. |
| Expiry and Trash | Choose never, one day, seven days, or burn after read; expired and deleted entries move to Trash for recovery. Trash entries are permanently removed after 30 days by default. |
| Desktop sticky notes | Drag a text or path card out of the panel to keep it as a separate desktop note for the current session. Exiting the app closes these notes; the saved entries remain in your collection. Image cards remain image entries. |
| QR codes | Right-click a text card to show a QR code for up to 1,800 characters. |
| Text tools | Use the workbench's Transform menu for case conversion, whitespace cleanup, JSON formatting, URL encoding/decoding, and Base64. |
| Group import/export | Share `.ksdgrp` packages or load command presets for Git, Docker, shells, and other tools. Template packages use format 3 and need 1.3.0 or newer; formats 1 and 2 remain importable. |
| Clipboard history | Optionally collect copied **text** automatically; empty and oversized text is ignored and duplicates are not added again. Off by default. |
| Direct paste | Optionally paste a copied text entry into the previous application when its target window can be safely confirmed. Off by default. |

### 2. Standard and Spatial panels

*The panel screenshots and motion GIF in this section show 1.2.3.*

Select **Settings > Appearance > Panel mode**. Both modes keep the same controls, typography, colors, and card layout.

<table>
  <tr><th>Standard</th><th>Spatial</th></tr>
  <tr>
    <td><img src="docs/images/classic-panel.png" width="290" alt="Standard panel with a flat view and soft shadows"></td>
    <td><img src="docs/images/floating-panel.png" width="290" alt="Spatial panel with perspective, layered cards, and a thin side edge"></td>
  </tr>
  <tr><td>A steady, flat presentation with soft shadows.</td><td>Perspective, independent card feedback, and visible depth between layers.</td></tr>
</table>

<p align="center"><img src="docs/images/floating-panel-motion.gif" width="400" alt="Spatial mode: card proximity feedback, then held drag inspection"></p>

*The white pointer demonstrates nearby-card feedback; the orange pointer marks held drag inspection. This is an illustrative animation, not a frame-rate benchmark.*

- **Fixed perspective:** keep a steady tilted view while nearby cards respond to the pointer.
- **Drag to inspect:** hold the **right mouse button** on a card body or panel background and drag. Release to keep the angle. A right-click without dragging still opens a card's menu. Settings also supports the middle button or a custom button/modifier combination.
- **Compact bar:** collapse the panel to six actions: minimize, expand/collapse, lock, screenshot, workbench, and Settings. Expanding restores the previous panel size.
- **Hide at desktop edge:** enabled by default; drag the panel to one of the four screen edges, then move away. A small handle remains; hover over or click it to restore the panel. Locking keeps it visible.

Resize an expanded panel by dragging its edges or corners in either mode; its size is restored on restart. The compact six-button bar stays fixed in size.

Light, dark, and system themes are available, with custom backgrounds, opacity, and frosting controls. System high-contrast and reduced-animation preferences provide static fallbacks. [Dark preview](docs/images/floating-panel-dark.png) · [Fixed-view preview](docs/images/floating-panel-fixed.png).

Settings also offers panel text palettes, a custom text color, and automatic contrast, plus independent selection-bubble style, glow, shadow, and opacity controls.

<details>
<summary>Default appearance settings</summary>

<p align="center"><img src="docs/images/default-appearance.png" width="720" alt="Appearance settings with Standard mode and fresh-install defaults"></p>

</details>

#### Complete appearance themes

Open **Settings > Appearance > Complete themes**, select a preset, then choose **Preview and apply**. Switch between Light, Dark or System, and choose whether selection bubbles and result panels follow the theme. **Cancel** restores the previous appearance.

| Order | Preset | Appearance |
| --- | --- | --- |
| 1 | **Sunlit Gallery** | Person-free cream-and-sage architecture, window shadows and warm daylight; matching moonlit night backgrounds. |
| 2 | **Twilight Study** | Person-free olive-and-walnut reading room, focused golden light; quiet night backgrounds with restrained warm accents. |
| 3 | **Wisteria Atelier** | Original anime pianist and conservatory, coordinated pearl/lavender and deep-violet palettes. |
| 4 | **Wisteria Reverie** | An anime pianist in a luminous lavender conservatory, with coordinated portrait/landscape day and night backgrounds. |

Each preset includes portrait and landscape artwork plus complete light/dark colors. **System** follows Windows app appearance and switches the corresponding artwork; it is not a time-of-day scheduler. All four themes are optional; fresh installs remain image-free. [Theme guide and artwork previews](docs/THEMES.md).

<p align="center"><img src="docs/images/1.3.0/theme-preview-light.png" width="680" alt="1.3.0 Wisteria Atelier theme preview with appearance choices, sample controls and fixed Apply and Cancel buttons"></p>

Use **Import theme** to load a theme ZIP or **Export current appearance** to share both light/dark variants, images and appearance settings. Personal background images require explicit inclusion. Themes do not contain entries, API keys, hotkeys or component installations. Theme files live in `theme/` beside the executable; updates preserve imported themes in `theme/custom/`.

**Settings surface opacity** adjusts cards, navigation and inputs without fading text. **Frosted settings background** blurs theme artwork only; **Frosted bubble background** is a separate option under **Bubbles & notifications**. Both effects start off. No-artwork Settings views, high contrast, or unavailable bubble composition use readable opaque surfaces as applicable.

<p align="center"><img src="docs/images/1.3.0/settings-frosted-dark.png" width="760" alt="1.3.0 Appearance settings with optional Wisteria dark theme, 60 percent Settings surface opacity and 8px artwork frosting"></p>

*1.3.0 · Optional Wisteria dark theme, 60% Settings surfaces and 8 px background blur. This is not the fresh-install appearance or a demonstration of native desktop blur.*

### 3. Screenshots and local text recognition

*The screenshot-workflow images in this section show 1.2.3.*

<p align="center"><img src="docs/images/screenshot-entry.png" width="720" alt="A saved screenshot entry with its original image and editable recognized text"></p>

Press **Alt+W** or a screenshot button and drag to select a region. Saved screenshots become image entries in the selected group. Open one to preview, copy, save as PNG, or **Recognize text**. You can edit and search the recognized text while retaining the original image.

A toolbar after selection offers three separate actions:

| Action | Result |
| --- | --- |
| Save screenshot | Save an image entry. Enter selects this action. |
| Extract text | Run local OCR and show temporary text without saving an entry or automatically copying it. |
| Extract and translate | Run local OCR, then send the recognized text to your chosen translation provider. The screenshot itself is not uploaded. |

<p align="center"><img src="docs/images/screenshot-text.png" width="480" alt="Temporary OCR result with explicit text-copy controls in version 1.2.3"></p>

Drag again to reselect; **Esc** or right-click cancels. Temporary results support explicit copying, retaining the source text after translation failure, and retrying translation. OCR is a recognition step; it does not translate automatically. [Screenshot workflows](docs/SCREENSHOTS.md) · [OCR support and limits](docs/LOCAL_OCR.md).

Temporary OCR and translation results offer **Save source**, **Save translation**, and **Save both**, plus a group selector initially using the current group. Save both creates one text entry with labeled source and translation sections. Saving is explicit, does not change the clipboard, and does not also save the screenshot. Translation failure still permits saving the source; save failure retains the result for retry. Duplicate text reuses the existing entry and places it in the selected group.

### 4. Selection actions and translation

<table>
  <tr><th>1.3.0 · Default styling</th><th>1.3.0 · Wisteria dark styling</th></tr>
  <tr>
    <td><img src="docs/images/1.3.0/selection-default.png" width="345" alt="1.3.0 Default selection bubble with optional Copy and Translate actions"></td>
    <td><img src="docs/images/1.3.0/selection-wisteria-dark.png" width="345" alt="1.3.0 Wisteria dark selection bubble with optional Copy and Translate actions"></td>
  </tr>
</table>

*Both 1.3.0 previews enable the optional Copy and translate mode. Fresh installs still use Copy only.*

The selection bubble is enabled by default in **Copy only** mode; configure it in **Settings > Capture & translation**, then select text in another application. **Copy** captures the selection into the dock's current group; **Translate** shows a translation without adding an entry.

Choose **Copy and translate** to keep both buttons visible. After copying, you can still translate the same selection before the bubble closes. Translation failures preserve the source text.

Choose a translation target independently of the interface language in Settings. **Follow interface language** remains the default on fresh installs; upgrades preserve your choice. Selection translation, screenshot translation and Test translation use the same preference. The result window shows the effective target and lets you translate the retained source into a different language for that result only, without changing Settings. Available targets depend on the provider or installed offline models; unsupported pairs produce an error without switching provider or uploading offline text.

<table>
  <tr><th>1.3.0 · Default styling</th><th>1.3.0 · Wisteria dark styling</th></tr>
  <tr>
    <td><img src="docs/images/1.3.0/translation-default.png" width="420" alt="1.3.0 compact Default translation result with copy, target language and save actions"></td>
    <td><img src="docs/images/1.3.0/translation-wisteria-dark.png" width="420" alt="1.3.0 compact Wisteria dark translation result with the same actions"></td>
  </tr>
</table>

*1.3.0 · Prefilled example text in actual result controls; no OCR or translation service was run. The target and save controls are enabled in both views and can be hidden independently in Settings. Native bubble frosting is not captured in these offscreen previews.*

| Provider | Setup and availability |
| --- | --- |
| Bing (China), Google, Lingva | No key required. Availability is not guaranteed. New installations default to Google; existing preferences are kept. |
| Baidu, Alibaba Cloud, DeepL | Supply your own credentials. Free quotas and charges depend on the provider and your account. Alibaba Cloud needs an activated Machine Translation service and `alimt:TranslateGeneral` permission. |
| Local offline | Install the optional local engine; it uses a loopback service without an account or API fee. |

Cloud translation sends only the requested text to the selected provider, with no automatic provider switch. OCR does not upload images. Provider credentials are stored **in plain text** in local `settings.json`; masked fields do not encrypt them.

<details>
<summary>Default selection and translation settings</summary>

<p align="center"><img src="docs/images/translation-settings.png" width="720" alt="1.2.3 Capture and translation settings with Copy only, result controls, Google and an independent translation target"></p>

</details>

### 5. Install optional components inside the app

<p align="center"><img src="docs/images/component-setup.png" width="720" alt="Optional components settings with OCR and offline translation install and repair buttons"></p>

*The 1.2.3 component settings show the fresh-install state: OCR and offline translation are not installed. Use these in-app buttons to install them; no terminal command is required.*

<details>
<summary>Component installation folders</summary>

<p align="center"><img src="docs/images/component-folders.png" width="720" alt="English 1.2.3 component folder controls; paths are masked."></p>

*English 1.2.3 component folder controls; paths are masked.*

</details>

1. Open **Settings > Local components**.
2. For OCR, choose **Install / repair** under **Text recognition (Chinese / English)** before first use. The standard package does not preinstall OCR or offline translation engines or models; explicit setup downloads and verifies the required files and checks the native runtime.
3. For local translation, choose **Install / repair** under **Offline translation (Chinese / English)**. Keep Settings open during the initial download; allow approximately **1 GB of free disk space** and **2 GB of available memory**.
4. Follow Microsoft's installer if prompted. If setup reports **Restart required**, restart Windows and retry. Cancelling app setup does not undo a Windows runtime installation already started.
5. Select **Local offline (Argos / LibreTranslate)** as the translation engine. Keep `http://127.0.0.1:5000`, then use **Test translation**. The service starts when needed; **Start service** and **Stop service** are also available.

Initial setup needs internet access to the relevant NuGet, GitHub, Python, package, model, and Microsoft download hosts. A failed download can be retried; valid cached files are reused. Once installed, OCR and local translation work offline. The translation service remains running after the app closes until stopped.

**Language coverage:** OCR supports printed English and Simplified Chinese. The optional downloaded translation models support **English ↔ Simplified Chinese**. The app's **20 interface languages** do not imply 20 OCR or offline translation languages. Missing models never trigger a switch to cloud translation. [Offline setup and troubleshooting](docs/OFFLINE_TRANSLATION.md).

### 6. Check for updates

<p align="center"><img src="docs/images/update-check.png" width="720" alt="Update dialog displaying a simulated newer release with the installation path masked"></p>

*The release shown is simulated, and the installation path is masked. This image does not announce an available release.*

Use **Check for updates** in the tray menu or **Settings > About**. A check does not download or install anything automatically. Downloading verifies SHA-256 and package contents; **Restart and upgrade** requires a separate confirmation. The updater replaces managed application files at the existing path, preserves user data and installed components, and retains recovery information if startup fails.

The organized package has three root files: `KeeStashDock.exe`, `StartHere.md`, and `update-manifest.json`. Guides are in `Docs`, notices in `Licenses`; languages and optional components retain dedicated folders. Keep the complete package together. [Updates and recovery](docs/UPDATING.md).

<a id="shortcuts"></a>
## Shortcuts

If a panel or screenshot shortcut cannot be registered at startup, a notification names it and offers Settings; the status remains visible there. Tray and button access still works. A failed shortcut change preserves the previously working binding.

| Action | Default shortcut or gesture |
| --- | --- |
| Show/hide quick panel | **Alt+Q**; configurable |
| Search in the quick panel | **Ctrl+F** while the panel is active; **Esc** returns to capture |
| Select workbench results | **Ctrl+A** while the list has focus; selects loaded rows only |
| Save typed text | **Enter** |
| Insert line break | **Alt+Enter** or **Shift+Enter** |
| Screenshot | **Alt+W**; independently configurable |
| Save selected screenshot region | **Enter** in the capture toolbar |
| Cancel screenshot | **Esc** or right-click |
| Inspect Spatial panel | Hold **right mouse button** and drag; configurable |
| Copy/translate from the selection bubble | **Ctrl+Alt+Shift+C** / **Ctrl+Alt+Shift+T**, only while the bubble is visible and the shortcuts are available |

<a id="data-and-upgrades"></a>
## Data, backup, and upgrades

**1.3.0 database compatibility:** the application upgrades the library to **schema 5**. Before changing an older database, it creates and verifies a consistent safety backup including committed WAL data; backup failure stops migration. **1.2.3 and earlier cannot open schema 5.** Restoring older application files does not downgrade the library. Keep both the active data folder and the pre-upgrade backup; if reverting, restore a separate compatible backup copy instead of overwriting newer data.

Theme files live beside the executable in `theme/`: `builtin/` contains app-managed resources and `custom/` contains imported user themes. Updates preserve `theme/custom/`. **Database backups do not include themes.** Use the theme export action in Settings to move or back up the complete light/dark appearance and its images separately.

Settings shows the last successful backup, latest check, next scheduled check, current status, backup location and snapshot count. Use **Back up now**, open the folder, or restore a listed snapshot through the guarded restore flow. Intervals include **15 / 30 / 60 minutes**, 6 / 12 / 24 / 48 hours and 7 days; the default remains **24 hours**. Existing custom intervals and retention choices are preserved. With seven snapshots at 15-minute intervals, the oldest-to-newest span is only **90 minutes**; choose retention deliberately and review the storage estimate. Every scheduled attempt creates a complete snapshot even when the database is unchanged; short intervals rotate retained history faster. These backups cover the entry database, including stored images, **not all settings, components or backgrounds**. Automatic retention does not remove manual exports or recovery copies.

<details>
<summary>Backup settings · every 24 hours by default</summary>

<p align="center"><img src="docs/images/backup-settings.png" width="720" alt="Data settings with automatic backups enabled every 24 hours and masked paths"></p>

<p align="center"><img src="docs/images/backup-status.png" width="720" alt="Backup status, snapshots and manual actions with isolated sample data. Paths are masked; the default interval remains 24 hours."></p>

*Backup status, snapshots and manual actions with isolated sample data. Paths are masked; the default interval remains 24 hours.*

</details>

<details>
<summary>About · application version and data location</summary>

<p align="center"><img src="docs/images/about-settings.png" width="720" alt="1.2.3 About page with application information and the data path masked"></p>

</details>

Entries, images, recognized text, and backups are stored locally. Portable data normally lives in `Data` beside the executable. If that folder is not writable, the app uses `%AppData%\KeeStashDock` and reports the fallback in Settings. Components installed through the app normally use `%LOCALAPPDATA%\KeeStashDock`, separately from your collection.

Use **Settings > Backup & data** to export/import a backup and configure scheduled backups. The first upgrade from **1.2.1 is manual**:

1. Export a backup, then exit the app from its tray menu.
2. Extract the new ZIP into a separate folder. While the app is closed, copy the **entire active data folder** into the new installation's `Data` folder. If Settings reports an `%AppData%` fallback, use that folder as the source.
3. Keep the original installation and backup. Start the new version and confirm your entries before removing old copies. Keep a pre-upgrade backup for rollback; older database schemas are not interchangeable.
4. If **Start with Windows** was enabled and you moved to a new folder, turn it off and on again in the new app's Settings. This registers the new executable path; otherwise Windows may launch the old copy at the next sign-in.

From **1.2.2**, use **Check for updates**, or follow the manual steps above. **1.3.0 candidate builds (including r1–r5)** require manual replacement because they share the final numeric version. Preserve the complete active data folder and backups; exit the old process before starting the new copy.

Do not attach personal `Data`, credentials, private backups, or logs to an issue. See [support and sanitized reports](SUPPORT.md), [upgrade recovery](docs/UPDATING.md), and the [security policy](SECURITY.md).

<a id="development"></a>
## Development and documentation

Build on Windows with the SDK selected by [`global.json`](global.json) (10.0.401 or a compatible 10.0.4xx patch):

```powershell
dotnet restore KeeStashDock.sln
dotnet build KeeStashDock.sln --configuration Release --no-restore --disable-build-servers
dotnet test --solution KeeStashDock.sln --configuration Release --no-build
```

English is the reference for repository documentation. Localized READMEs share the same feature structure; interface translations live in `Locales/*.json`. Translation corrections are welcome through the [contribution guide](CONTRIBUTING.md).

| Documentation | Contents |
| --- | --- |
| [Changelog](CHANGELOG.md) | Version history |
| [Architecture](docs/ARCHITECTURE.md) | Projects, responsibilities, and storage |
| [Localization](docs/LOCALIZATION.md) | Interface and README translation conventions |
| [Release process](docs/RELEASING.md) | Build, package, and acceptance checks |
| [Support](SUPPORT.md) · [Code of Conduct](CODE_OF_CONDUCT.md) | Questions, reports, and community participation |
| [Third-party notices](THIRD_PARTY_NOTICES.md) | Dependency, runtime, and model licenses |

<a id="license"></a>
## License

Copyright © 2026 KeeganKei. Licensed under [Apache-2.0](LICENSE); see [NOTICE](NOTICE) for attribution. Dependencies, optional runtimes, and models retain their own licenses in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). The project license does not grant trademark rights.

The application icon and bundled theme artwork are AI-generated. For artwork attribution and licensing, see [icon](docs/assets/README.md), [Sunlit Gallery](theme/builtin/sunlit-gallery/SOURCE.md), [Twilight Study](theme/builtin/twilight-study/SOURCE.md), [Wisteria Atelier](theme/builtin/wisteria-atelier/SOURCE.md), [Wisteria Reverie](theme/builtin/wisteria-reverie/SOURCE.md).

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
  <img alt="Published version 1.2.1" src="docs/assets/badges/version.svg">
  <img alt=".NET 10" src="docs/assets/badges/dotnet.svg">
  <img alt="Windows 10 and 11" src="docs/assets/badges/windows.svg">
  <img alt="Apache-2.0 license" src="docs/assets/badges/license.svg">
</p>

> **Release status:** the published version is **1.2.1**. This branch prepares **1.2.2**. Sections marked **1.2.2 candidate** describe features awaiting release; the download links still provide 1.2.1. See the [candidate acceptance record](docs/releases/1.2.2.md).

[Get started](#get-started) · [Features](#features) · [Shortcuts](#shortcuts) · [Data and upgrades](#data-and-upgrades) · [Development](#development) · [License](#license)

<a id="get-started"></a>
## Get started

**[Latest release](https://github.com/Keegankei/KeeStashDock/releases/latest)** · **[Windows x64 ZIP — 1.2.1](https://github.com/Keegankei/KeeStashDock/releases/download/v1.2.1/KeeStashDock-1.2.1-win-x64.zip)** · [SHA-256 checksum](https://github.com/Keegankei/KeeStashDock/releases/download/v1.2.1/KeeStashDock-1.2.1-win-x64.zip.sha256)

1. Download the application ZIP under **Assets**, rather than GitHub's developer-oriented **Source code** archive.
2. Extract the complete package into a writable folder and run `KeeStashDock.exe`. Keep all accompanying folders together.
3. Press **Alt+Q** to show the quick panel. Type a snippet and press **Enter** to save it; click its card to copy it.
4. Open **Settings** from the panel or system tray to select a language, theme, and optional components.

The Windows x64 package includes .NET; a separate .NET installation is unnecessary. Windows 11 is the primary target, with best-effort Windows 10 22H2 compatibility. OCR and offline translation may need Microsoft's Visual C++ runtime, which the in-app component installer can install. See the [1.2.1 acceptance record](docs/releases/1.2.1.md) for tested coverage.

<details>
<summary>Verify the downloaded ZIP</summary>

Run this in the download folder and compare the result with the accompanying `.sha256` file:

```powershell
Get-FileHash .\KeeStashDock-1.2.1-win-x64.zip -Algorithm SHA256
```

A checksum checks file integrity; it is not a publisher signature. The current package is unsigned.

</details>

<a id="features"></a>
## Features

The previews below show actual application controls with synthetic sample content. The inspection GIF is generated from those controls using a simulated pointer. The update preview uses a simulated release; no personal desktop or clipboard content appears. [Image sources and reproduction](docs/images/README.md).

### 1. Capture, find, and organize

<p align="center"><img src="docs/images/main-window.png" width="900" alt="Workbench with search, groups, pinned snippets, and sample image entries"></p>

Use the quick panel for short captures and the **workbench** for managing your collection. Open the workbench with the panel's expand button or from the tray.

| Tool | What you can do |
| --- | --- |
| Quick input and search | Save text with Enter; insert line breaks with Alt+Enter or Shift+Enter; search your entries as you type. |
| Drag into the dock | Drop text to save it, or drop files and folders to save their paths. Click a path card to open its target; right-click **Copy** to copy the path. File contents are not imported. |
| Groups, tags, and pinning | Keep related entries together, edit their title/text/tags, and pin frequently used content. |
| Expiry and Trash | Choose never, one day, seven days, or burn after read; expired and deleted entries move to Trash for recovery. Trash entries are permanently removed after 30 days by default. |
| Desktop sticky notes | Drag a text or path card out of the panel to keep it as a separate desktop note for the current session. Exiting the app closes these notes; the saved entries remain in your collection. Image cards remain image entries. |
| QR codes | Right-click a text card to show a QR code for up to 1,800 characters. |
| Text tools | Use the workbench's Transform menu for case conversion, whitespace cleanup, JSON formatting, URL encoding/decoding, and Base64. |
| Group import/export | Share `.ksdgrp` packages or load command presets for Git, Docker, shells, and other tools. Image packages require a compatible app version. |
| Clipboard history | Optionally collect copied **text** automatically; empty and oversized text is ignored and duplicates are not added again. Off by default. |
| Direct paste | Optionally paste a copied text entry into the previous application when its target window can be safely confirmed. Off by default. |

### 2. Standard and Spatial panels · 1.2.2 candidate

Select **Settings > Panel mode**. Both modes keep the same controls, typography, colors, and card layout.

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
- **Hide at desktop edge:** enable this option, drag the panel to one of the four screen edges, then move away. A small handle remains; hover over or click it to restore the panel. Locking keeps it visible.

Light, dark, and system themes are available, with custom backgrounds, opacity, and frosting controls. System high-contrast and reduced-animation preferences provide static fallbacks. [Dark preview](docs/images/floating-panel-dark.png) · [Fixed-view preview](docs/images/floating-panel-fixed.png) · [Measured performance and limits](docs/performance/1.2.2-floating.md).

Settings also offers panel text palettes, a custom text color, and automatic contrast, plus independent selection-bubble style, glow, shadow, and opacity controls.

### 3. Screenshots and local text recognition

<p align="center"><img src="docs/images/screenshot-entry.png" width="720" alt="A saved screenshot entry with its original image and editable recognized text"></p>

Press **Ctrl+Alt+S** or a screenshot button and drag to select a region. Saved screenshots become image entries in the selected group. Open one to preview, copy, save as PNG, or **Recognize text**. You can edit and search the recognized text while retaining the original image.

**1.2.2 candidate:** a toolbar after selection offers three separate actions:

| Action | Result |
| --- | --- |
| Save screenshot | Save an image entry. Enter selects this action. |
| Extract text | Run local OCR and show temporary text without saving an entry or automatically copying it. |
| Extract and translate | Run local OCR, then send the recognized text to your chosen translation provider. The screenshot itself is not uploaded. |

<p align="center"><img src="docs/images/screenshot-text.png" width="480" alt="Temporary OCR result with explicit text-copy controls in the 1.2.2 candidate"></p>

Drag again to reselect; **Esc** or right-click cancels. Temporary results support explicit copying, retaining the source text after translation failure, and retrying translation. OCR is a recognition step; it does not translate automatically. [Screenshot workflows](docs/SCREENSHOTS.md) · [OCR support and limits](docs/LOCAL_OCR.md).

### 4. Selection actions and translation

<p align="center"><img src="docs/images/selection-actions.png" width="480" alt="The 1.2.2 selection bubble with independent Copy and Translate buttons"></p>

Enable the selection bubble in **Settings > Behavior**, then select text in another application. **Copy** captures the selection into the dock's current group; **Translate** shows a translation without adding an entry.

**1.2.2 candidate:** choose **Copy and translate** to keep both buttons visible. After copying, you can still translate the same selection before the bubble closes. Translation failures preserve the source text.

The translation target follows the app's interface language. Available target languages depend on the selected provider or installed offline models.

| Provider | Setup and availability |
| --- | --- |
| Bing (China), Google, Lingva | No key required. Availability is not guaranteed. New installations default to Bing; existing preferences are kept. |
| Baidu, Alibaba Cloud, DeepL | Supply your own credentials. Free quotas and charges depend on the provider and your account. Alibaba Cloud needs an activated Machine Translation service and `alimt:TranslateGeneral` permission. |
| Local offline | Install the optional local engine; it uses a loopback service without an account or API fee. |

Cloud translation sends only the requested text to the selected provider, with no automatic provider switch. OCR does not upload images. Provider credentials are stored **in plain text** in local `settings.json`; masked fields do not encrypt them.

### 5. Install optional components inside the app

<p align="center"><img src="docs/images/component-setup.png" width="720" alt="Optional components settings with OCR and offline translation install and repair buttons"></p>

*Component controls shown in their 1.2.1 not-installed example state. Normal setup uses these in-app buttons; no terminal command is required.*

1. Open **Settings > Optional components**.
2. For OCR, choose **Install / repair** under **Text recognition (Chinese / English)** if a component is missing. The standard ZIP already includes the engine and English/Simplified Chinese models; setup also checks the native runtime.
3. For local translation, choose **Install / repair** under **Offline translation (Chinese / English)**. Keep Settings open during the initial download; allow approximately **1 GB of free disk space** and **2 GB of available memory**.
4. Follow Microsoft's installer if prompted. If setup reports **Restart required**, restart Windows and retry. Cancelling app setup does not undo a Windows runtime installation already started.
5. Select **Local offline (Argos / LibreTranslate)** as the translation engine. Keep `http://127.0.0.1:5000`, then use **Test translation**. The service starts when needed; **Start service** and **Stop service** are also available.

Initial setup needs internet access to the relevant GitHub, Python, package, model, and Microsoft download hosts. A failed download can be retried; valid cached files are reused. Once installed, OCR and local translation work offline. The translation service remains running after the app closes until stopped.

**Language coverage:** OCR supports printed English and Simplified Chinese. Included translation models support **English ↔ Simplified Chinese**. The app's **20 interface languages** do not imply 20 OCR or offline translation languages. Missing models never trigger a switch to cloud translation. [Offline setup and troubleshooting](docs/OFFLINE_TRANSLATION.md).

### 6. Check for updates · 1.2.2 candidate

<p align="center"><img src="docs/images/update-check.png" width="720" alt="Update dialog displaying a simulated newer release and an example installation path"></p>

*The version and path shown are illustrative; this image does not announce an available release.*

Use **Check for updates** in the tray menu or **Settings > About**. A check does not download or install anything automatically. Downloading verifies SHA-256 and package contents; **Restart and upgrade** requires a separate confirmation. The updater replaces managed application files at the existing path, preserves user data and installed components, and retains recovery information if startup fails.

The organized candidate package has three root files: `KeeStashDock.exe`, `StartHere.md`, and `update-manifest.json`. Guides are in `Docs`, notices in `Licenses`; languages and optional components retain dedicated folders. Keep the complete package together. [Updates and recovery](docs/UPDATING.md).

<a id="shortcuts"></a>
## Shortcuts

| Action | Default shortcut or gesture |
| --- | --- |
| Show/hide quick panel | **Alt+Q**; configurable |
| Save typed text | **Enter** |
| Insert line break | **Alt+Enter** or **Shift+Enter** |
| Screenshot | **Ctrl+Alt+S**; independently configurable |
| Save selected screenshot region | **Enter** in the 1.2.2 capture toolbar |
| Cancel screenshot | **Esc** or right-click |
| Inspect Spatial panel | Hold **right mouse button** and drag; configurable in 1.2.2 |
| Copy/translate from the selection bubble | **Ctrl+Alt+Shift+C** / **Ctrl+Alt+Shift+T** in 1.2.2, only while the bubble is visible and the shortcuts are available |

<a id="data-and-upgrades"></a>
## Data, backup, and upgrades

Entries, images, recognized text, and backups are stored locally. Portable data normally lives in `Data` beside the executable. If that folder is not writable, the app uses `%AppData%\KeeStashDock` and reports the fallback in Settings. Components installed through the app normally use `%LOCALAPPDATA%\KeeStashDock`, separately from your collection.

Use **Settings > Data** to export/import a backup and configure scheduled backups. The first upgrade from **1.2.1 is manual**:

1. Export a backup, then exit the app from its tray menu.
2. Extract the new ZIP into a separate folder. While the app is closed, copy the **entire active data folder** into the new installation's `Data` folder. If Settings reports an `%AppData%` fallback, use that folder as the source.
3. Keep the original installation and backup. Start the new version and confirm your entries before removing old copies. Keep a pre-upgrade backup for rollback; older database schemas are not interchangeable.

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

The application icon was AI-generated under the maintainer's direction. Its design and provenance are recorded in [docs/assets/README.md](docs/assets/README.md).

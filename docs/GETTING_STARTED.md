# KeeStashDock 1.2.2

Windows x64 portable package. Extract the entire ZIP into a writable folder and
run `KeeStashDock.exe`. The .NET runtime is bundled in the executable. Extract the
complete package; copying only the executable omits languages, OCR, and setup tools.

```text
KeeStashDock.exe         Application entry point
StartHere.md            This guide
update-manifest.json    Managed application files for verified upgrades
Docs/                   Screenshot and upgrade guides
Licenses/               Project license, notices, and dependency licenses
Locales/                Editable interface language packs
x64/                    Independent OCR engine and its native libraries
OcrModels/              OCR models, setup, and model license
NativeRuntime/          Optional Microsoft prerequisite setup
OfflineTranslation/     Optional local translation setup and service scripts
Data/                   Your library and settings; created on first use
```

The .NET runtime manages its own native-library extraction cache automatically;
no manual environment setup is needed. Keep the application entry point and
`update-manifest.json` at the root of this folder.

## First use

- A fresh installation starts in **English**. Choose another interface language
  in Settings. An upgrade that keeps existing data also keeps its saved language.
- Press **Alt+Q** to show or hide the quick panel. The tray menu opens the workbench
  and Settings. Launching the executable again brings back the panel.
- Type a text entry and press **Enter** to save it. **Alt+Enter** or **Shift+Enter**
  inserts a new line. Click an entry to copy it.
- Use **Ctrl+Alt+S** or a screenshot button, select a region, then choose **Save screenshot**, **Extract text**, or **Extract and translate**. **Enter** saves; **Esc** or right-click cancels. Extracting text alone creates no library entry and does not automatically copy text.
- Open a saved image entry and use **Recognize text** to add editable, searchable text. OCR runs locally. Translation uses only your configured provider when explicitly requested; source text remains available after failure.
- In Settings, choose **Standard** or **Spatial** panel mode. Spatial keeps the same controls with perspective, layered cards and a thin panel edge. Choose **Fixed** or **Drag to inspect**. Hold the right mouse button on a card body or panel background and drag to adjust the view; release to keep the angle. Settings can select the middle button or a custom button/modifier combination. Windows accessibility preferences can disable animation.
- Enable **Hide at desktop edge** to tuck the panel into a small handle when dragged to an edge. Hover over or click the handle to restore it. Ordinary collapse keeps only the six toolbar buttons.
- Select **Copy and translate** for two independent selection-bubble buttons. Copy saves the selection to the dock; Translate uses the same source text without adding an entry.

## Optional local components

Open **Settings** and scroll to **Optional components**. Click **Install / repair**
next to **Text recognition (Chinese / English)** or
**Offline translation (Chinese / English)**. Setup runs inside the app with
progress and cancellation; no terminal command is needed.
The standard package includes English and Simplified Chinese OCR models. Use
repair if a required file or native runtime is missing.

Offline translation requires an initial internet download of its runtime and
English/Chinese models. After setup, choose **Local offline (Argos / LibreTranslate)** under
**Translation engine**. The installed service starts automatically when needed;
**Start service** and **Stop service** are also available under **Optional components**. It translates locally once the needed model is
installed. Setup may request Windows administrator permission to install the
Microsoft Visual C++ prerequisite, and Windows may request a restart.

Cloud translation providers have their own availability, credentials, quotas,
and billing terms. Their use sends selected text to that provider. Offline
translation and OCR do not fall back to a cloud provider on failure.

## Data and upgrades

Your library and settings are under `Data` beside the program when the folder is
writable; otherwise they are under `%AppData%\KeeStashDock`. Back up the active
data folder before upgrading, close KeeStashDock, extract the new package into a
fresh folder, and copy the complete old data folder to the new `Data` folder.
Keep the old backup separately. Never overwrite a live database.

From 1.2.2 onward, use **Check for updates** in the tray menu or **Settings > About**. The app checks official GitHub Releases only on request. Download and verify first, then confirm **Restart and upgrade**. Same-path upgrades preserve user data and installed components, keep a rollback copy, and check successful startup. See `Docs/Updating.md` for recovery and first-upgrade instructions.

This release retains database schema v4. Older programs that only understand
v1-v3 cannot open the upgraded database. Images are included in database backups.

## Help and license

- Documentation and translated guides: https://github.com/Keegankei/KeeStashDock
- Downloads: https://github.com/Keegankei/KeeStashDock/releases/latest
- Report a problem: https://github.com/Keegankei/KeeStashDock/issues
- Screenshot details: `Docs/Screenshots.md`
- Local setup details: `OcrModels/README.md` and `OfflineTranslation/README.md`

KeeStashDock is licensed under Apache-2.0. See `Licenses/LICENSE`,
`Licenses/NOTICE`, and `Licenses/THIRD_PARTY_NOTICES.md`; dependency licenses are
in the corresponding `Licenses` subfolders.

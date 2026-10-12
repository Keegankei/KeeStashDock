# KeeStashDock 1.2.1

Windows x64 portable package. Extract the entire ZIP into a writable folder and
run `KeeStashDock.exe`. The .NET runtime is included. Keep all files and folders
beside the executable.

## First use

- Press **Alt+Q** to show or hide the quick panel. The tray menu opens the workbench
  and Settings. Launching the executable again brings back the panel.
- Type a text entry and press **Enter** to save it. **Alt+Enter** or **Shift+Enter**
  inserts a new line. Click an entry to copy it.
- Use **Ctrl+Alt+S** or a screenshot button to save a selected screen region as an
  image entry. **Esc** cancels. Open the entry to view, copy, or save its image.
- Use **Recognize text** to add editable, searchable text. OCR runs locally and
  does not translate automatically. Select recognized text to use the existing
  translation bubble after enabling the selection popup in Settings.

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

This release upgrades databases to schema v4. Older programs that only understand
v1-v3 cannot open the upgraded database. Images are included in database backups.

## Help and license

- Documentation and translated guides: https://github.com/Keegankei/KeeStashDock
- Downloads: https://github.com/Keegankei/KeeStashDock/releases/latest
- Report a problem: https://github.com/Keegankei/KeeStashDock/issues
- Screenshot details: `Screenshots.md`
- Local setup details: `OcrModels/README.md` and `OfflineTranslation/README.md`

KeeStashDock is licensed under Apache-2.0. See `LICENSE`, `NOTICE`, and
`THIRD_PARTY_NOTICES.md`; dependency licenses are in `Licenses`.

# Release Process

This checklist describes a reproducible manual release. It intentionally avoids publishing secrets or assuming a signing infrastructure that is not yet configured.

## 1. Prepare

1. Confirm that the intended version is set in `Directory.Build.props`.
2. Update `CHANGELOG.md`, the English README, every translated README, and `docs/assets/badges/version.svg`. Prepare `docs/releases/<version>-notes.md` for the GitHub Release, with upgrade steps and known limits. Until publication, identify prospective download links as pending.
3. Verify that `LICENSE`, `NOTICE`, README license references, and `PackageLicenseExpression` consistently identify Apache-2.0.
4. Verify package versions in `Directory.Packages.props`, refresh `THIRD_PARTY_NOTICES.md`, and review Dependabot changes.
5. Run localization key and placeholder checks through the test suite.
6. Review user-visible help text for every feature changed since the previous release.
7. Confirm that no `Data` directory, database, backup, log, API key, `*.user` file, or machine-specific path is tracked.

## 2. Validate

On Windows with the SDK selected by `global.json`:

```powershell
dotnet restore KeeStashDock.sln
dotnet build KeeStashDock.sln --configuration Release --no-restore
dotnet test --solution KeeStashDock.sln --configuration Release --no-build
python -B tools/offline-translation/test_runtime.py
powershell -NoProfile -ExecutionPolicy Bypass -File tools/offline-translation/test_process.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/native-runtime/test_setup.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ocr/test_setup.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-Clean.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-PublishSafety.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-SingleFileBundle.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-UpdateManifest.ps1
dotnet restore src/KeeStashDock.Desktop/KeeStashDock.Desktop.csproj --runtime win-x64 -p:PublishProfile=FolderProfile
dotnet publish src/KeeStashDock.Desktop/KeeStashDock.Desktop.csproj `
  --configuration Release `
  --no-restore `
  -p:PublishProfile=FolderProfile `
  --output artifacts/publish
powershell -NoProfile -ExecutionPolicy Bypass -File tools/New-UpdateManifest.ps1 -Path artifacts/publish
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Publish.ps1 -Path artifacts/publish
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-PackagedHost.ps1 -Path artifacts/publish
```

Use the same publish profile during restore and publish so native runtime and
single-file analyzer dependencies agree. The profile bundles the .NET/WPF and
SQLite runtimes into the root executable while keeping editable languages and
component setup tools external. Standard packages exclude optional OCR engines,
OCR models, and offline-translation runtimes/models even if local caches exist. `Test-PackagedHost`
launches an isolated verification mode that checks the actual packaged process,
SQLite, projected WPF text, languages and the expected missing-component state
without opening the normal application or creating user data. Verify real OCR
separately after explicit installation into an isolated component directory. It also verifies that the executable
and application base directory remain at the package root.

Run the Windows CI workflow for the release commit. Record local automation, maintainer acceptance, and any environment-specific checks not completed in `docs/releases/<version>.md`. Use the following checklist for hands-on acceptance; synthetic fixtures alone do not establish clean-machine or physical-device coverage:

- First launch and portable-data creation; compare every fresh-install default with `docs/GETTING_STARTED.md`, then verify an upgrade preserves saved preferences
- `%AppData%` fallback from a non-writable application folder
- Tray lifecycle and single-instance activation; launching again must keep an already visible panel open
- Move the panel, exit and restart with its saved screen position; the first tray/hotkey summon must restore the position without an exception, and resizing/moving must still persist after another restart
- Alt+Q and Alt+W registration, remapping, disable, and conflict handling
- Screenshot cancellation, negative monitor coordinates, mixed display scales, and capture from both panel and workbench
- Image preview/copy/save, local OCR success/cancel/timeout, editable text, and the shared translation bubble
- Image deduplication, expiry/burn-after-read, Trash restore/purge, and image group/database round trips
- Unsaved image text on shutdown, settings restart/import, and concurrent workbench edits
- Capture, search, edit, copy, direct paste, pin, expiry, Trash, and restore
- Alt+Enter and Shift+Enter at the cursor and over a selection, plain Enter capture, and CJK IME commit behavior
- Repeated card clicks and keyboard reactivation of the quick panel, including changing the target application while the panel stays open
- Clipboard history opt-in and immediate disable behavior; app-owned copies must not recapture burn-after-read entries or duplicate OCR text
- Translation-result copy failures, and repeated image-window open/close without retaining original image buffers
- Selection capture and every translation engine that can be safely tested
- Bing China translation, independent Baidu/DeepL credentials, categorized failures, and cancellation when a translation card is dismissed
- Alibaba Cloud credentials and service activation errors, provider switching without credential cross-contamination, and cancellation of Settings tests
- Settings one-click OCR and offline translation setup/repair, progress, cancellation, retry, and a read-only application folder
- Native runtime probe/skip, valid Microsoft installer signature, UAC acceptance/cancellation, and restart-required handling on a clean test machine
- App setup cancellation/timeout after UAC approval: the UI must explain that a privileged Windows runtime installation may continue; after exit code 3010, restart Windows before retrying setup
- Published offline setup files, first-time English/Chinese model installation, start/stop, actual translation with network unavailable, and missing-model/service errors
- Local endpoint validation (reject remote URLs and redirects), and no cloud request after a local failure
- Backup creation, export errors, import safety backup, and restart after a slow shutdown
- Import an open WAL database and a copy left after unexpected exit; verify committed content survives
- Search inside Trash, restore from another window, and confirm stale actions cannot permanently delete live entries
- Change retention in another window before copying; confirm burn-after-read uses the current saved policy
- Group import/export and presets
- Empty/duplicate text edit rejection, optional empty OCR text, draft preservation, and unchanged tags after a failed save
- Duplicate group creation/rename, numeric suffix suggestions, and imports with whitespace/case name conflicts
- Upgrade a copy of a v1/v2 database containing duplicate group names; verify group IDs, memberships, and snippet content remain intact
- Theme switching, custom appearance, high DPI, and Arabic right-to-left layout
- Standard/Spatial modes, hollow shadows, proximity/press/drag feedback, reduced-animation and high-contrast fallback
- Screenshot Save / Extract / Extract and translate, transient text, source-copy after failure, cancellation and reselection; confirm the selection bubble resumes after save/cancel/error and stays disabled when configured off
- Selection Copy / Translate / Both, Copy then Translate on the same source, history on/off and stale async results
- Update check/download cancellation, same-path managed replacement, preserved custom/component/data files, denied elevation, interrupted helper and startup-health rollback
- Manual migration to a new folder with Start with Windows previously enabled: disable and re-enable it in the new app, then verify the startup entry points to the new executable

The current build upgrades databases to schema v4 on startup. Back up existing user data before an upgrade. Older binaries that support only v1-v3 cannot open a v4 database; testing an older binary requires a separate copy of a pre-upgrade backup. Version 2 group exports preserve image assets; version 1 text imports remain supported.

Before each release, inspect resolved dependency versions and known advisories:

```powershell
dotnet list src/KeeStashDock.Desktop/KeeStashDock.Desktop.csproj package --include-transitive
dotnet list src/KeeStashDock.Desktop/KeeStashDock.Desktop.csproj package --include-transitive --vulnerable
```

Refresh the bundled dependency licenses when package or self-contained runtime
versions change. An advisory check covers only its queried feeds and does not
replace the native OCR/runtime review.

## Acceptance records

Keep versioned results in [docs/releases](releases/1.2.2.md). Each record identifies
the release tag, build/test results, package checks, maintainer feedback, and
remaining platform limitations. Do not convert broad user feedback into a claim
that every hardware or provider combination was tested.

## 3. Package

1. Publish into a fresh output directory. Never archive an existing user installation. Keep `KeeStashDock.exe`, `StartHere.md`, and `update-manifest.json` at the root. Keep `Docs`, `Licenses`, `Locales`, `OcrModels`, `OfflineTranslation`, and `NativeRuntime` beside them. Project `LICENSE`, `NOTICE`, and `THIRD_PARTY_NOTICES.md` are under `Licenses`. The user's `Data` folder is created on first use and is never shipped.
2. If a trusted code-signing process is configured, sign the executable before generating the final ownership manifest or archive. Any binary change after manifest generation requires regenerating and verifying the manifest. For an unsigned release, state that fact in the release notes; a checksum is an integrity check, not a signature.
3. Generate the final manifest and run `tools/Verify-Publish.ps1` and `tools/Test-PackagedHost.ps1` as shown above. The package verifier checks the embedded runtime, component setup files, absence of preinstalled optional engines/models, documentation and notices, and rejects accidental private state, including database WAL/journal files, rotated logs, environment files, and signing keys. It checks names without opening personal files and refuses symbolic links or junctions. The host check then exercises the real runtime and native libraries.
4. Archive the complete directory contents as `KeeStashDock-<version>-win-x64.zip`, with the executable and manifest directly at the ZIP root, without an extra containing folder. Save the ZIP outside the published directory. Standard packages include OCR and offline translation setup scripts and attribution, but no optional engine, model, or runtime binaries. Do not copy installed user components or development model caches into the release. Installed components retain their upstream licenses and are preserved by upgrades.
5. Generate `KeeStashDock-<version>-win-x64.zip.sha256` from the final ZIP. Its single line must contain the 64-character SHA-256, two spaces, and the exact ZIP filename. Extract that ZIP into another fresh directory and repeat package and host verification against the extracted contents. Do not modify the archive after hashing.
6. Run available security scans and record their scope and outcome. Do not describe an unperformed scan as passed.

## 4. Publish

1. Push the reviewed release commit and wait for its required CI checks. Follow the repository's branch policy; use a pull request when required.
2. Create a signed or annotated Git tag matching the version, for example `v1.2.2`.
3. Create a GitHub Release from the tag.
4. Attach the archive and checksum.
5. Use `docs/releases/<version>-notes.md` as the release description and confirm it matches the final changelog and acceptance record.
6. State supported Windows architecture, data migration notes, known issues, and license terms.

## 5. After release

- Verify the uploaded checksum and start the downloaded artifact.
- Mark the release as the latest stable release.
- Confirm the changelog has the release version and date, then remove the temporary publication-preparation notice from all README languages only after the release assets are available.
- Leave repository visibility changes to the owner when publication is staged in a private repository.
- Monitor security reports, crash reports, and upgrade issues.

## Repository settings not stored in Git

Configure these in GitHub after the repository is published:

- Branch protection or rulesets requiring pull requests and the CI check
- Private vulnerability reporting and Dependabot alerts
- Secret scanning and push protection where available
- Restricted workflow token permissions
- Signed commits or signed tags according to maintainer policy
- A `CODEOWNERS` file after reviewer usernames and ownership boundaries are known

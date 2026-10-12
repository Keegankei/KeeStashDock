# Release Process

This checklist describes a reproducible manual release. It intentionally avoids publishing secrets or assuming a signing infrastructure that is not yet configured.

## 1. Prepare

1. Confirm that the intended version is set in `Directory.Build.props`.
2. Update `CHANGELOG.md` and both README files.
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
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-Clean.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-PublishSafety.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ocr/Setup.ps1
dotnet restore src/KeeStashDock.Desktop/KeeStashDock.Desktop.csproj --runtime win-x64
dotnet publish src/KeeStashDock.Desktop/KeeStashDock.Desktop.csproj `
  --configuration Release `
  --no-restore `
  -p:PublishProfile=FolderProfile `
  --output artifacts/publish
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Publish.ps1 -Path artifacts/publish
```

A stable release is not ready until the Windows CI workflow is green and the same commit passes a clean-machine smoke test of:

- First launch and portable-data creation
- `%AppData%` fallback from a non-writable application folder
- Tray lifecycle and single-instance activation
- Alt+Q and Ctrl+Alt+S registration, remapping, disable, and conflict handling
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

The current build upgrades databases to schema v4 on startup. Back up existing user data before an upgrade. Older binaries that support only v1-v3 cannot open a v4 database; testing an older binary requires a separate copy of a pre-upgrade backup. Version 2 group exports preserve image assets; version 1 text imports remain supported.

Before each release, inspect resolved dependency versions and known advisories:

```powershell
dotnet list src/KeeStashDock.Desktop/KeeStashDock.Desktop.csproj package --include-transitive
dotnet list src/KeeStashDock.Desktop/KeeStashDock.Desktop.csproj package --include-transitive --vulnerable
```

Refresh the bundled dependency licenses when package or self-contained runtime
versions change. An advisory check covers only its queried feeds and does not
replace the native OCR/runtime review.

## 3. Package

1. Publish into a fresh output directory. Never archive an existing user installation.
2. Run `tools/Verify-Publish.ps1` as shown above; it rejects missing OCR/models/notices and accidental private state, including database WAL/journal files, rotated logs, environment files, and signing keys. It checks names without opening personal files and refuses symbolic links or junctions. WPF assets are embedded in the assembly.
3. Keep `Locales`, `OcrModels`, `x64`, `OfflineTranslation`, `NativeRuntime`, `Licenses`, `LICENSE`, `NOTICE`, and `THIRD_PARTY_NOTICES.md` with the executable. Standard packages include OCR models and offline translation setup scripts; they do not include the much larger optional translation runtime. If preinstalling that runtime, retain Python/wheel/model license files and exclude downloads, logs, process state, and bytecode caches.
4. Generate a SHA-256 checksum for the final archive.
5. Digitally sign the executable and archive when a trusted code-signing process is available.
6. Scan the release artifact with the organization's approved security tools.

## 4. Publish

1. Merge the release pull request after required CI checks pass.
2. Create a signed or annotated Git tag matching the version, for example `v1.1.2`.
3. Create a GitHub Release from the tag.
4. Attach the archive and checksum.
5. Paste the relevant changelog section into the release notes.
6. State supported Windows architecture, data migration notes, known issues, and license terms.

## 5. After release

- Verify the uploaded checksum and start the downloaded artifact.
- Mark the release as the latest stable release.
- Move completed changelog entries out of `Unreleased`.
- Monitor security reports, crash reports, and upgrade issues.

## Repository settings not stored in Git

Configure these in GitHub after the repository is published:

- Branch protection or rulesets requiring pull requests and the CI check
- Private vulnerability reporting and Dependabot alerts
- Secret scanning and push protection where available
- Restricted workflow token permissions
- Signed commits or signed tags according to maintainer policy
- A `CODEOWNERS` file after reviewer usernames and ownership boundaries are known

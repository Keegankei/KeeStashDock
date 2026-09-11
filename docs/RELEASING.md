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
dotnet restore src/KeeStashDock.Desktop/KeeStashDock.Desktop.csproj --runtime win-x64
dotnet publish src/KeeStashDock.Desktop/KeeStashDock.Desktop.csproj `
  --configuration Release `
  --no-restore `
  -p:PublishProfile=FolderProfile `
  --output artifacts/publish
```

A stable release is not ready until the Windows CI workflow is green and the same commit passes a clean-machine smoke test of:

- First launch and portable-data creation
- `%AppData%` fallback from a non-writable application folder
- Tray lifecycle and single-instance activation
- Alt+Q registration and conflict handling
- Capture, search, edit, copy, direct paste, pin, expiry, Trash, and restore
- Clipboard history opt-in and immediate disable behavior
- Selection capture and every translation engine that can be safely tested
- Backup creation, export, import safety backup, and restart
- Group import/export and presets
- Empty/duplicate edit rejection, draft preservation, and unchanged tags after a failed save
- Duplicate group creation/rename, numeric suffix suggestions, and imports with whitespace/case name conflicts
- Upgrade a copy of a v1/v2 database containing duplicate group names; verify group IDs, memberships, and snippet content remain intact
- Theme switching, custom appearance, high DPI, and Arabic right-to-left layout

The current build upgrades databases to schema v3 on startup. Back up existing user data before an upgrade. Older binaries that support only v1/v2 cannot open a v3 database; testing an older binary requires a separate copy of a pre-upgrade backup.

## 3. Package

1. Remove any test `Data` directory from the publish output.
2. Confirm that `Locales` and `Assets` required at runtime are present.
3. Confirm that `LICENSE`, `NOTICE`, and `THIRD_PARTY_NOTICES.md` are included in the distributable archive.
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

# Contributing to KeeStashDock

Thank you for improving KeeStashDock. This guide keeps changes reviewable, reproducible, and consistent with the repository's architecture and language policy.

## Before starting

1. Search existing issues and pull requests for related work.
2. Open an issue before a large feature, schema change, public API change, or user-visible behavioral redesign.
3. Keep pull requests focused. Unrelated cleanup should be submitted separately.
4. Do not include personal clipboard data, databases, logs, API keys, publish profiles, or machine-specific IDE files.

## Development setup

Use Windows and the .NET SDK selected by `global.json`.

```powershell
dotnet restore KeeStashDock.sln
dotnet build KeeStashDock.sln --configuration Release --no-restore
dotnet test --solution KeeStashDock.sln --configuration Release --no-build
```

A change to the WPF application should also pass a publish smoke check:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ocr/Setup.ps1
dotnet restore src/KeeStashDock.Desktop/KeeStashDock.Desktop.csproj --runtime win-x64
dotnet publish src/KeeStashDock.Desktop/KeeStashDock.Desktop.csproj `
  --configuration Release `
  --no-restore `
  -p:PublishProfile=FolderProfile `
  --output artifacts/publish
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Publish.ps1 -Path artifacts/publish
```

The optional translation helper has standard-library-only safety tests (Python 3.11 or later):

```powershell
python -B tools/offline-translation/test_runtime.py
powershell -NoProfile -ExecutionPolicy Bypass -File tools/offline-translation/test_process.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/native-runtime/test_setup.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-Clean.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-PublishSafety.ps1
```

OCR model setup is an explicit, hash-verified download. Plain source builds do not
need models; the complete release package and CI publish check do. Use a fresh
publish directory rather than a running installation or a directory with `Data`.

## Cleaning local outputs

Preview the cleanup before running it:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Clean.ps1 -WhatIf
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Clean.ps1
```

The default removes `src`/`tests` build intermediates and `TestResults`.
Use `-IncludeIdeState` for Visual Studio cache and `-IncludeBuildOutputs` for
old `artifacts`/`publish` outputs. The script skips `Data` directories and outputs
containing databases (including WAL/journal companions), settings, drafts, logs,
credentials, or a running program. It checks that deletion targets stay inside
this checkout and refuses symbolic links or junctions.
It does not remove downloaded OCR models or the optional offline runtime.

## Architecture rules

- `KeeStashDock.Domain` remains dependency-free.
- `KeeStashDock.Infrastructure` owns disk, SQLite, settings, logging, and backup concerns.
- `KeeStashDock.Core` owns use cases and domain-facing application logic.
- `KeeStashDock.Desktop` owns WPF, tray, clipboard, selection, Win32, theme, and localization integration.
- New business rules should not be implemented only in view code.
- Persisted enum members and applied database migrations must never be reordered or rewritten. Append compatible additions instead.

See `docs/ARCHITECTURE.md` for the complete dependency and data-flow model.

## Repository language policy

English is required for:

- Source identifiers and comments
- Commit messages and pull-request descriptions
- General Markdown documentation
- Build, CI, issue-template, and configuration text
- User-facing fallback strings outside localization files

The intentional exceptions are:

- `README.zh-CN.md`
- JSON language packs under `src/KeeStashDock.Desktop/Locales`

`en-US.json` is the canonical localization key set. A translation must preserve every key and every composite-format placeholder such as `{0}`. See `docs/LOCALIZATION.md`.

## Coding expectations

- Follow `.editorconfig` and existing C# conventions.
- Keep nullable reference types enabled and avoid suppressions without justification.
- Add or update tests for business rules and repository contracts.
- Prefer small pure methods for text processing and deterministic logic.
- Treat clipboard, selected text, translation keys, logs, databases, and backups as sensitive data.
- Do not add telemetry, network calls, or automatic uploads without explicit product approval and user consent.
- Use exact centrally managed package versions; do not add floating versions.

## Database changes

1. Add a new ordered migration in `MigrationRunner`.
2. Never edit a migration already shipped to users.
3. Preserve existing data and verify upgrades from the current release schema.
4. Update architecture documentation and tests.
5. Consider backup, rollback, and import compatibility.

## Localization changes

Any new user-visible key must first be added to `en-US.json`, then to every language file. Automated tests verify key and placeholder parity. Native-language review is still required for meaning, tone, layout length, and the in-application help page.

## Pull-request checklist

- The change has a clear user or maintenance purpose.
- Release build and tests pass locally.
- New behavior has test coverage where practical.
- No secrets, personal data, binaries, or `*.user` files are included.
- Documentation and `CHANGELOG.md` are updated when needed.
- All locale files retain matching keys and placeholders.
- Screenshots or a short recording are included for meaningful UI changes.
- The pull request explains risk, migration impact, and manual verification.

## Contribution licensing

By submitting a contribution, you confirm that you have the right to submit it. Unless you explicitly and conspicuously mark a submission as “Not a Contribution,” any contribution intentionally submitted for inclusion in KeeStashDock is licensed under the Apache License 2.0 in accordance with Section 5 of that license, without additional terms or conditions. No contributor license agreement is currently required.

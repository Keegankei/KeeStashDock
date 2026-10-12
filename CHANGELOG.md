# Changelog

All notable changes to KeeStashDock are documented in this file.

The format follows the principles of Keep a Changelog. The project uses semantic versioning for assembly and release numbers. The reviewed source identifies the current development version as 1.1.2, but no public release tag was supplied; the first public release therefore remains under `Unreleased` until its tag and release date are created.

## [Unreleased]

### Added

- Region screenshots saved as image entries, with preview, image copy/save, and editable searchable OCR text.
- Configurable screenshot shortcut (Ctrl+Alt+S by default), independent enable/disable, and registration conflict handling that preserves the existing shortcut.
- On-demand local English/Simplified Chinese OCR with explicit model setup, cancellation, and the existing selection action bubble for translation.

- Alibaba Cloud TranslateGeneral support with independent AccessKey credentials, signed requests, and service activation errors.
- Local offline translation with a loopback-only Argos / LibreTranslate-compatible service, optional isolated English/Chinese engine setup, and actionable model/service errors.
- A translation test in Settings using a fixed sample, with cancellation on configuration changes.
- Baidu official translation API support with user-provided App ID and secret key, and categorized translation failure messages.
- Windows WPF keyboard regression tests for Alt+Enter, Shift+Enter, selection replacement, undo, and IME handling.
- Opt-in clipboard history collection.
- Optional direct paste back to the previously focused application.
- Scheduled automatic backups with configurable interval and rolling retention.
- Bubble opacity control and simplified appearance defaults.
- DeepL translation support with a user-provided API key.
- Complete right-to-left mirroring for Arabic.
- Twenty interface language packs and an expanded in-application help guide.
- Separate group deletion actions for preserving or trashing contained snippets.
- Repository governance, automation, and test infrastructure.
- Apache License 2.0 terms, attribution notice, and project license metadata.

### Changed

- Schema v4 stores original PNGs and thumbnails with their entries so database backups, imports, Trash recovery, and permanent deletion retain consistent image data. Older text databases migrate forward.
- Version 2 group packages include images and recognized text; version 1 text packages remain readable.

- New installs default to Bing's China translation endpoint; existing provider choices and DeepL credentials are preserved.
- Aligned the build SDK with .NET SDK 10.0.401, allowing later 10.0.4xx patches.
- Group names now use trimmed, culture-independent case keys with a database uniqueness constraint. Schema v3 preserves existing group IDs and content while assigning deterministic suffixes to legacy duplicate or blank names.
- New default groups and imported-copy name suggestions choose an available numeric suffix.
- Updated architecture and release guidance to describe the current schema, import safeguards, and remaining validation work.
- Duplicate clipboard entries now refresh an existing live snippet instead of creating a second row.
- Group deletion with contents uses recoverable soft deletion.
- Replaced floating NuGet versions with centrally managed exact versions.
- Expanded the English and Simplified Chinese README files.
- Clarified source-language, licensing, support, security, and release policies.
- Relicensed KeeStashDock as an OSI-approved open-source project under Apache-2.0.

### Fixed

- Keep selection-popup changes from rewriting the Windows startup entry; only the startup checkbox changes it.
- Preserve the remembered direct-paste target across repeated quick-panel activations and track external foreground changes without relaxing identity or focus checks.
- Handle Alt+Enter's WPF system-key event to insert a newline at the cursor; retain Shift+Enter and plain Enter behavior.
- Replace the retired Bing token URL with the current China web session flow.
- Preserve the selected DeepL provider when opening Settings and keep Baidu and DeepL keys separate when switching engines.
- Cancel dismissed or superseded translations and prevent stale responses from replacing newer results.
- Reject empty edited content and edits that duplicate another live snippet, with localized messages that preserve the draft.
- Save snippet fields and tags in one transaction, including FTS changes, so failed saves cannot leave partial edits.
- Serialize capture deduplication and editing checks to prevent concurrent captures from inserting the same content twice.
- Handle group rename/import conflicts with localized feedback and dispatch category notifications after releasing the database connection.
- Preserve full-width comma test coverage using a Unicode escape compatible with the repository language policy.
- Replace obsolete MSTest data-test attributes and resolve nullable logger warnings.
- Retain unsaved workbench drafts across refreshes, selection changes, and failed saves.
- Validate and stage database imports with safety backups, atomic replacement, rollback, and restart protection.
- Preserve independent field updates, atomic usage counts, and UTC timestamps.
- Cancel direct paste when the target window, process, or focus changes.
- Upgrade the bundled SQLite runtime and add database regression coverage.

### Removed

- Quick-panel edge glow, its settings, and transparent geometry insets. Legacy settings fields are ignored; bubble text glow remains available.
- User-specific Visual Studio publish metadata from the source tree.

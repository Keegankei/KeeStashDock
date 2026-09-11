# Changelog

All notable changes to KeeStashDock are documented in this file.

The format follows the principles of Keep a Changelog. The project uses semantic versioning for assembly and release numbers. The reviewed source identifies the current development version as 1.1.2, but no public release tag was supplied; the first public release therefore remains under `Unreleased` until its tag and release date are created.

## [Unreleased]

### Added

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

- User-specific Visual Studio publish metadata from the source tree.

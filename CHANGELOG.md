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

- Duplicate clipboard entries now refresh an existing live snippet instead of creating a second row.
- Group deletion with contents uses recoverable soft deletion.
- Replaced floating NuGet versions with centrally managed exact versions.
- Expanded the English and Simplified Chinese README files.
- Clarified source-language, licensing, support, security, and release policies.
- Relicensed KeeStashDock as an OSI-approved open-source project under Apache-2.0.

### Removed

- User-specific Visual Studio publish metadata from the source tree.

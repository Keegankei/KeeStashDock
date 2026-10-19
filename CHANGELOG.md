# Changelog

All notable changes to KeeStashDock are documented in this file.

The format follows Keep a Changelog. Release numbers use semantic versioning.

## [Unreleased]

## [1.2.3] - Unreleased

### Added

- Independent translation target language, with Follow interface language as the compatible default. Selection, screenshot and Settings tests use the same target resolution. Result windows support a temporary target and retry with retained source text.
- Explicit saving of temporary OCR/translation source, translation, or both to a chosen group. Both produces one labeled text entry; duplicate content is reused. Save failures retain the result and do not write to the clipboard.
- Independent visibility switches for the temporary target selector and Save to group panel in translation results. Both remain enabled by default; hiding them preserves the current source, result and selected group.
- Visible backup state, last attempt/success, next scheduled check, snapshot list, Back up now, open-folder and guarded restore actions. Added 15-, 30- and 60-minute intervals alongside hourly/day options.
- Configurable backup, OCR and offline translation folders with open-folder and reset actions. Location changes preserve existing files; optional components are reused only when present at the selected location, otherwise installation remains an explicit action. Offline translation stops its previous managed service before changing folders.
- Persistent panel/screenshot shortcut status and one actionable notification per startup failure episode. Failed remapping retains the previous working shortcut.

### Changed

- Moved backups from hourly housekeeping to an independent minute-based scheduler, with one overdue attempt on startup/resume, maintenance deferral and serialized snapshots. UTC status survives restarts; incomplete or failed snapshots are not published or used to prune retention.
- Preserved existing backup preferences by migrating legacy hours to minutes. Automatic retention removes only owned automatic snapshots after a successful full database snapshot; manual and recovery files remain available.
- Redesigned Settings with category navigation, clearer page headings, grouped controls, switches and themed focus/hover states. Dependent options collapse when inactive while shared translation, popup appearance, manual backup and panel-timing controls remain available.
- Kept full update-recovery snapshots in the active data folder's `Backups/BeforeUpdate-*` directory. Recovery copying excludes owned snapshots/status from a custom backup folder and known configured component payloads without excluding unrelated user files alongside them.
- Updated all twenty interface languages, nine README guides, upgrade instructions and first-use documentation for 1.2.3. Existing media remains accurately labeled as 1.2.2 footage.

### Fixed

- Prevent an unhandled null-reference error when first opening the quick panel after restarting with a saved screen position. Native window hooks and direct-paste registration now use the window handle even before the visual tree is attached; shadow initialization uses the same safe lookup.

See the [release notes](docs/releases/1.2.3-notes.md) and
[acceptance record](docs/releases/1.2.3.md). Publication is pending; the stable
1.2.2 download links remain available until the new release is published.

## [1.2.2] - 2026-10-17

Published as [v1.2.2](https://github.com/Keegankei/KeeStashDock/releases/tag/v1.2.2).
Validation results and remaining coverage limits are tracked in the
[acceptance record](docs/releases/1.2.2.md).

### Added

- Standard and Spatial panel modes with the same controls, fonts, colors and layout. Spatial adds perspective, layered card outlines and shadows, a thin panel edge, and independent proximity, hover and press feedback.
- Fixed perspective and Drag to inspect. Hold the right mouse button and drag, then release to retain the angle. A right-click without dragging retains the card menu. Middle-button and custom button/modifier gestures are also supported.
- Auto-hide at any of the four desktop edges, with a small handle that restores the panel on hover or click. The compact bar retains all six actions and restores the previous expanded size.
- Screenshot actions to save an image, extract text, or extract and translate. OCR-only results stay in memory; recognized source text remains available after translation failures.
- A combined selection bubble with separate Copy and Translate buttons, reusable source text, temporary keyboard shortcuts, and clipboard-history deduplication.
- Explicit update checks in the tray and Settings, official GitHub release discovery, cancellable SHA-256-verified downloads, ownership manifests, same-path replacement, and startup-health rollback/recovery.
- Regression coverage for screenshot/listener recovery, panel presentation and gestures, selection sessions, release metadata, unsafe packages, managed-file preservation, and interrupted updates.

### Changed

- Named the panel presentations Standard / Spatial in all twenty interface languages without changing saved settings. Expanded the illustrated README into nine languages with native-language navigation.
- Finalized fresh-install defaults: English; Alt+Q for the panel; Alt+W for screenshots; Google translation; enabled Copy-only selection bubble with Default styling, text glow off and shadow on; edge auto-hide with 2 s / 0.1 s delays; system theme; Standard mode; right-button drag inspection; automatic text colors; 90% controls opacity; no background image; 20% overlay; frosting off with a stored 4 px radius. Startup launch, clipboard collection and direct paste are off; scheduled backups run every 24 hours. Upgrades retain saved preferences.
- Removed preinstalled OCR engines/models from standard packages. OCR and offline translation both use explicit in-app Install / repair downloads; installed and supported legacy portable components remain available and are preserved during upgrades.
- Organized the portable package around a root executable, StartHere guide and update manifest, with documentation, licenses, locales and component setup files in named folders. Portable Data remains in its existing location.
- Reduced repeated perspective hit testing, coordinate conversions and drawing allocations. Card feedback shares a bounded sampler and retained drawing resources; ordinary pointer movement does not rotate the panel. System high-contrast and reduced-animation settings have static fallbacks.
- Kept translation-provider settings available for screenshot translation when the selection bubble is disabled.
- Documented manual upgrades from 1.2.1 and earlier 1.2.2 candidates, including re-registering Start with Windows after moving to a new folder.

### Fixed

- Restore the selection watcher after screenshot completion or cancellation so the selection bubble continues working; keep its lifecycle state consistent while screenshot capture suspends it.
- Prevent the global selection-bubble dismissal handler from cancelling screenshot selection on the first left press.
- Defer torn-out sticky-note shadows until their initial layout completes, preventing notes from collapsing to narrow lines.
- Fall back to the official release page when the GitHub API is rate-limited or unreachable, with bounded checks, retry caching/backoff, and localized update errors.

See the [release notes](docs/releases/1.2.2-notes.md) and
[acceptance record](docs/releases/1.2.2.md) for validation results and remaining
visual, clean-machine, and platform coverage limits.

## [1.2.1] - 2026-10-12

First packaged release of the current feature set. Includes the fixes and validation from the earlier 1.1.2 development candidates.

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
- One-click installation/repair of local OCR and offline translation in Settings, with progress, cancellation, and engine start/stop controls.
- A hidden native-engine probe and verified Microsoft Visual C++ runtime setup when the prerequisite is missing, including administrator and restart handling. Cancellation stops the app setup worker; an already authorized Windows runtime installation may continue.
- Portable-package verification for OCR models/native binaries, localization, offline setup scripts, third-party notices, and accidental private state.
- Safe opt-in cleanup of generated build outputs and IDE caches, with CI checks that preserve user data and standalone databases.
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
- Added English, Simplified Chinese, Traditional Chinese, and Japanese README guides with application screenshots, download links, and in-app component setup instructions.
- Adopted a new AI-generated application icon: a continuous tricolor note-paper ribbon and a vintage quill, with its design provenance recorded in `docs/assets/README.md`.
- Clarified source-language, licensing, support, security, and release policies.
- Relicensed KeeStashDock as an OSI-approved open-source project under Apache-2.0.

### Fixed

- Keep the quick panel visible when the executable is launched again; repeated launches no longer toggle it closed or replace its direct-paste target.
- Ignore superseded visibility-animation callbacks so rapid hide/show requests cannot leave the quick panel hidden or transparent.
- Keep Trash searches scoped to deleted entries and check current entry state before restoring or permanently deleting from the workbench.
- Preserve unsaved editor drafts when filters hide an existing entry; synchronize image-window permissions and clean text after external edits, deletion, or restore.
- Import consistent database snapshots including committed WAL content, even when the source database is open or has uncheckpointed changes after an unexpected exit.
- Apply the current stored burn-after-read policy atomically when recording use, instead of trusting an older window model.
- Reject backup destinations that would overwrite the active database or its journal files, and report manual export failures in Settings.
- Wait for the previous process to exit before restarting the application, avoiding fixed-delay restarts that can be lost to single-instance detection.
- Fail promptly when OCR output exceeds its bound, and check the offline translation runtime independently of the OCR executable.
- Reject truncated, conflicting-length, and excessively nested local translation requests.
- Protect top-level Data directories, export packages, database sidecars, rotated logs, credentials, and private temporary files during cleanup and release verification.
- Reject corrupted local OCR model files by size and checksum, falling back to verified bundled models.
- Reuse an available local translation service and start the installed engine only after a connection failure.
- Finish component setup when its script exits even if the detached translation server keeps output pipes open.
- Preserve the Windows runtime installer's restart-required exit code instead of reporting a generic failure.
- Make complete group imports transactional, preserving existing groups, entries, images, and deduplicated memberships when an import fails.
- Mark imported image entries with the import source, and reject malformed persisted timestamps, field types, enum values, and out-of-range counters before replacing a database.
- Safely ignore background log writes after the logger closes during shutdown.
- Track the clipboard sequence of app-owned copies so clipboard history cannot recapture burn-after-read entries or turn copied OCR text into duplicate snippets.
- Release theme-event subscriptions when image windows close, allowing their original image buffers to be collected.
- Report translation-result clipboard failures instead of showing a false success message.
- Ignore stale/reused offline service process IDs without stopping unrelated processes, verify an existing service at the requested port, and invalidate the installation marker before a repair changes runtime files.
- Include verified OCR models in documented/CI release builds, run offline helper tests in CI, and keep development test scripts out of user packages.
- Ship dependency license texts and notices for managed/runtime dependencies alongside the existing native OCR notices.
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

[Unreleased]: https://github.com/Keegankei/KeeStashDock/compare/v1.2.2...HEAD
[1.2.2]: https://github.com/Keegankei/KeeStashDock/releases/tag/v1.2.2
[1.2.1]: https://github.com/Keegankei/KeeStashDock/releases/tag/v1.2.1

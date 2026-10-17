# Updates and recovery

[Simplified Chinese](UPDATING.zh-CN.md)

## Check, download, then restart

Starting with 1.2.2, choose **Check for updates** in the tray menu or **Settings > About**. The application contacts the official `Keegankei/KeeStashDock` GitHub Releases endpoint only on request. It ignores drafts and prereleases and compares numeric release versions.

If the API is rate limited or cannot be reached, the same check tries the repository's official `github.com` latest-release link. Only a redirect to that repository's stable numeric release tag is accepted. For a newer version, the app also checks its expected SHA-256 sidecar and archive headers before offering a download; it does not download the ZIP during the check. Release notes for this route are available through **Open release page**. This uses GitHub's [documented latest-release link](https://docs.github.com/en/repositories/releasing-projects-on-github/linking-to-releases), with no mirror or access token.

Checks have a 30-second overall deadline and remain cancellable. Within the open dialog, verified results are reused for 30 seconds, failed checks back off briefly, and API rate-limit reset/retry headers are respected. **Retry** remains available after a failure; it does not repeatedly submit requests while a backoff is active.

Review the available version, release notes, download size and installation folder. Choose **Download update** to stage the Windows x64 ZIP outside the installation. You can cancel during download. The app verifies the matching SHA-256 sidecar, every manifest file, ZIP paths and extraction limits before offering **Restart and upgrade**.

Closing the update window or checking again discards a downloaded stage if no restart-and-upgrade attempt has begun. Failed or cancelled downloads also attempt to remove their temporary stage. A file lock or denied permission can leave a temporary folder for later cleanup; these steps do not modify the installed application.

Restart is a separate confirmation. The app finishes outstanding editor decisions, stops its installed local translation engine, saves settings and makes a consistent database backup plus copies of other active user files. It then starts the staged helper, exits and waits for replacement. Only an exact process identity is used; the helper does not terminate unrelated processes by name. Windows may request administrator permission for a protected installation directory. The application itself restarts under its original, non-elevated user context.

A failed check does not mean the application is up to date. Retry or use **Open release page** when GitHub is unreachable or rate limited. No alternative mirror is selected automatically. SHA-256 detects a corrupt or mismatched package; it is not publisher signing. Windows SmartScreen may still show an unknown-publisher prompt for this unsigned application.

## What is preserved

The full portable package includes `update-manifest.json`. Keep it beside the executable. It records paths, lengths and hashes for shipped application files; it never owns your `Data` or `%AppData%` folders.

Organized packages bundle the .NET runtime into the same root `KeeStashDock.exe`, with guides under `Docs` and project/dependency licenses under `Licenses`. Languages and optional-component setup folders remain beside it; the standard package does not preinstall OCR or offline translation engines/models. Always extract the whole package. During an in-app upgrade from an older flat layout, unchanged loose libraries owned by the previous manifest can be removed safely; the executable path and data location stay the same.

- Existing libraries, settings, provider credentials, screenshots, drafts, logs and backups stay in their current storage location.
- Installed OCR native engines and models, including supported legacy portable assets, and offline runtime/model assets are preserved. Shipped setup scripts can be updated normally.
- Custom user files are preserved. An unowned collision, modified managed file or unsafe linked destination stops installation instead of silently overwriting it. Move a deliberately customized application file aside and use the documented manual procedure if necessary.
- An obsolete file is removed only when the previous manifest identifies it as an unchanged application file.
- The upgrade keeps the installation path and does not alter existing shortcuts or startup registration.

Version 1.2.2 retains database schema v4. Database rollback is not part of application-file rollback. Older programs limited to schemas v1-v3 cannot open a v4 database.

## First upgrade from 1.2.1

Version 1.2.1 has no update button or ownership manifest, so this first upgrade is manual:

1. Download the full application ZIP and its checksum from the [official release page](https://github.com/Keegankei/KeeStashDock/releases/latest). Verify the checksum if desired.
2. Close KeeStashDock, including its tray process. Back up the active data folder; Settings > About identifies its location. Stop the local translation engine from Settings before closing if it is installed.
3. Extract the complete new package into a fresh folder. Keep the previous program folder intact until acceptance.
4. If the old installation used portable data, copy its entire `Data` folder to the new folder. If it used `%AppData%`, avoid accidentally creating an empty portable library: copy the active fallback data into the new writable `Data` folder, or keep using a non-writable installation location that resolves to the same fallback. Never overwrite a live database.
5. Optional components installed under `%LocalAppData%\KeeStashDock` remain available. Copy an older portable `OfflineTranslation/runtime` directory separately only while its engine is stopped; do not replace the new setup scripts. The fresh package does not contain a legacy portable OCR engine or models. If these were not copied, use the in-app **Install / repair** action to install them in the per-user component directory. Use the same action if a component needs repair.
6. Start the new application and verify the library and settings. Update desktop/taskbar shortcuts to the new folder.
7. If **Start with Windows** was enabled, turn it off and on again in the new app's Settings. The existing startup entry may still point to the old executable; this step registers the new path and prevents the old copy from launching at the next Windows sign-in. Keep the backup and previous folder until satisfied.

For an existing 1.2.2 or newer installation, prefer the in-app flow to keep the original path. A folder without a valid ownership manifest always falls back to manual instructions; the updater does not guess which files it may delete.

## Replacing an earlier 1.2.2 candidate

The final 1.2.2 package must replace earlier 1.2.2 candidates manually: their
numeric version is also 1.2.2, so Check for updates does not offer the final
package as a newer version. Follow the manual steps above, including the startup
registration step after moving folders. Keep the previous folder and data backup
until the replacement is accepted, and use **Install / repair** if OCR components
are missing. Component-preservation rules added during 1.2.2 preparation do not
retroactively change an older candidate's updater.

## If installation or startup fails

Staging and recovery state live under `%LocalAppData%\KeeStashDock\Updates`. A transaction folder contains `transaction.json`, a backup of managed files and `RECOVERY.txt`. The data snapshot is stored in the active data folder's `Backups/BeforeUpdate-*` directory. These copies can contain private information; do not upload them with a public issue.

The helper keeps previous application files until the exact new process finishes its startup health check. A replacement failure or failed startup triggers rollback when possible. If a file lock, denied permission, quarantine or external modification prevents rollback, the transaction and backup remain available and the error identifies the recovery location.

After an interrupted update, launch the installed executable and follow its recovery prompt. If that executable cannot start, use the staged helper command recorded in `RECOVERY.txt`. Close other application instances first. The helper validates the journal and backup before restoring managed application files; do not manually copy a recovery folder over `Data`.

Once restart-and-upgrade preparation has been attempted, its staged package may be needed by the helper or a recovery transaction. It is retained even if helper startup fails or the update window is later closed. Keep this package and any recovery folders until the new version has been accepted; they are not treated as unused downloads.

You can archive or remove an old completed transaction and its matching staging folder after confirming its journal state is `Committed` or `RolledBack` and no update is running. Keep a separate data backup.

# Maintenance tools

Run these tools from the repository root. PowerShell scripts target Windows;
the Python helpers state their requirements in their source or linked guide.
Use the [contribution guide](../CONTRIBUTING.md) for development checks and the
[release process](../docs/RELEASING.md) for the complete build and packaging order.

| Area | Tools | Purpose |
| --- | --- | --- |
| Local cleanup | [Clean.ps1](Clean.ps1), [ArtifactPolicy.ps1](ArtifactPolicy.ps1) | Remove reviewed generated outputs while protecting personal data and running packages |
| Update ownership | [New-UpdateManifest.ps1](New-UpdateManifest.ps1), [Verify-UpdateManifest.ps1](Verify-UpdateManifest.ps1), [UpdateManifest.Common.ps1](UpdateManifest.Common.ps1) | Generate and verify the package's owned-file list, lengths, and SHA-256 hashes |
| Portable package checks | [Verify-Publish.ps1](Verify-Publish.ps1), [Test-PackagedHost.ps1](Test-PackagedHost.ps1), [SingleFileBundle.Common.ps1](SingleFileBundle.Common.ps1) | Check package layout, privacy, bundled runtime contents, and the packaged host's isolated verification mode |
| Maintenance regressions | [Test-Clean.ps1](Test-Clean.ps1), [Test-PublishSafety.ps1](Test-PublishSafety.ps1), [Test-SingleFileBundle.ps1](Test-SingleFileBundle.ps1), [Test-UpdateManifest.ps1](Test-UpdateManifest.ps1) | Exercise cleanup and release safeguards with disposable fixtures |
| Optional native runtime | [native-runtime/Setup.ps1](native-runtime/Setup.ps1), [native-runtime/test_setup.ps1](native-runtime/test_setup.ps1) | Check/install the required Microsoft runtime during explicit component setup, with separate decision tests |
| Optional OCR | [ocr/Setup.ps1](ocr/Setup.ps1), [ocr/test_setup.ps1](ocr/test_setup.ps1) | Install verified OCR assets and test setup behavior; see [local OCR](../docs/LOCAL_OCR.md) |
| Optional offline translation | [offline-translation](offline-translation) | Setup/start/stop helpers, the isolated translation server, and runtime/process safety tests; see [offline translation](../docs/OFFLINE_TRANSLATION.md) |
| Application icon | [Export-AppIcon.ps1](Export-AppIcon.ps1) | Convert the approved source artwork to the application's multi-resolution ICO; see [asset provenance](../docs/assets/README.md) |
| Documentation GIFs | [documentation/optimize_gifs.py](documentation/optimize_gifs.py) | Optimize captured GIFs with Pillow while checking unchanged playback pixels and timing; see [preview reproduction](../docs/images/README.md) |

## Cleanup scope

Preview cleanup with `-WhatIf` before executing it. The default scope is project
`bin`/`obj` directories under `src` and `tests`, plus root `TestResults`.
`-IncludeIdeState` also considers the Visual Studio `.vs` cache.
`-IncludeBuildOutputs` additionally considers only the disposable CI output at
`artifacts/publish`; it does not sweep other `artifacts` or `publish` directories.
Review all other outputs individually. Preserve formal release packages, user
data, installed optional components, and files needed for reproduction or recovery.
See [Cleaning local outputs](../CONTRIBUTING.md#cleaning-local-outputs) for the
command and safety boundaries.

## Optional components

Normal users install or repair components through **Settings > Local components**.
The setup scripts remain part of that in-app workflow; the `.cmd` wrappers also
support manual diagnostics. Explicit setup may download verified dependencies
and invoke Microsoft's runtime installer. The ordinary build and CI safety tests
do not require installed OCR or translation engines. Do not delete these helpers
or package local runtime/model caches as application source.

# Third-Party Notices

KeeStashDock itself is licensed under Apache-2.0. It depends on third-party software that remains subject to its own license terms. The following list covers the direct dependencies and resolved runtime dependency families reviewed on 2026-10-26 for release 1.3.0. Refresh this inventory against the resolved NuGet and self-contained runtime assets before each release.

## Runtime dependencies

| Component | Version | Purpose | License |
| --- | ---: | --- | --- |
| [CommunityToolkit.Mvvm](https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2) | 8.4.2 | MVVM source generators and helpers | MIT |
| [Microsoft.Data.Sqlite](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.10) | 10.0.10 | SQLite provider and native SQLite bundle | MIT |
| [Microsoft.Extensions.DependencyInjection](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/10.0.10) | 10.0.10 | Dependency-injection container | MIT |

## Resolved .NET and SQLite runtime components

| Component | Reviewed version | License / attribution |
| --- | --- | --- |
| Microsoft.Data.Sqlite.Core | 10.0.10 | MIT; .NET Foundation and Contributors, Microsoft Corporation |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.10 | MIT; .NET Foundation and Contributors |
| SQLitePCLRaw core / provider.e_sqlite3 / bundle_e_sqlite3 / lib.e_sqlite3 | 2.1.12 | Apache-2.0; Copyright 2014-2024 SourceGear, LLC |
| Native SQLite, distributed by SQLitePCLRaw | Resolved bundled native build | Public domain; [SQLite copyright](https://www.sqlite.org/copyright.html) |
| .NET and Windows Desktop runtime in the self-contained win-x64 package | 10.0.14 at release review; SDK-dependent | MIT with upstream third-party notices; .NET Foundation and Contributors |

Complete Community Toolkit, .NET, and SQLitePCLRaw license texts and upstream
notices are retained in `third_party/dotnet` and copied to `Licenses/dotnet`.
That directory's README records their exact package/tag provenance. The framework
runtime patch is selected by the SDK; review its actual version and refresh
notices if it changes. Development test dependencies do not ship with the app.

## Local OCR engine and models

The standard package does not preinstall OCR binaries or models. Explicit in-app
setup downloads and verifies the [TesseractOCR 5.5.2 archive](https://www.nuget.org/packages/TesseractOCR/5.5.2),
extracting only the required native files. The application launches its Windows
x64 CLI; the managed wrapper and its logging dependency are not application dependencies.
The package's CLI reports Tesseract 5.5.0 and Leptonica 1.85.0. The native engine
and codecs remain subject to their original licenses; complete texts and upstream
attributions are in `third_party/ocr`, copied to `Licenses/OCR` in build and publish
outputs. This software is based in part on the work of the Independent JPEG Group.

| Component | Optional installed version | License |
| --- | --- | --- |
| TesseractOCR wrapper/distribution | 5.5.2 | Apache-2.0; Copyright 2012-2021 Charles Weld, 2021-2023 Kees van Spelde |
| Tesseract native engine | 5.5.0 | Apache-2.0; contributors listed in the bundled AUTHORS file |
| Leptonica | 1.85.0 | BSD-2-Clause; Copyright 2001-2020 Leptonica |
| GIFLIB | 5.2.1 | MIT |
| libjpeg-turbo | 2.0.6 | IJG, BSD-3-Clause, and zlib licenses |
| libpng | 1.6.37 | PNG Reference Library License |
| LibTIFF | 4.3.0 | LibTIFF license |
| zlib | 1.2.11 | zlib license |
| Official tessdata_fast English / Simplified Chinese models | `87416418657359cb625c412a48b6e1d6d41c29bd` | Apache-2.0 |

The optional engine and model setup verifies pinned SHA-256 hashes. Native
license texts remain included under `Licenses/OCR`, and setup retains the upstream
model license alongside the installed models. Engine/model binaries are excluded
from the standard package; these upstream terms continue to apply when components
are downloaded or an existing portable installation is retained.

## QR encoder attribution

`src/KeeStashDock.Desktop/QrEncoder.cs` follows Project Nayuki's
[QR Code generator](https://www.nayuki.io/page/qr-code-generator-library),
licensed under MIT, Copyright 2026 Project Nayuki. KeeStashDock's adaptation
uses C# with UTF-8 byte mode and error correction level M. The upstream license
and provenance are retained in `third_party/qr` and shipped in `Licenses/QR`.

## Development dependency

| Component | Version | Purpose | License |
| --- | ---: | --- | --- |
| [MSTest.Sdk](https://www.nuget.org/packages/MSTest.Sdk/4.3.2) | 4.3.2 | Test framework, runner, and test tooling | MIT |

## Optional Microsoft native runtime prerequisite

Settings may download and install Microsoft's Visual C++ v14 x64 Redistributable
when the native engine probe fails. The app requires a valid Microsoft Corporation
Authenticode signature before launching it. This separate Microsoft component is
not relicensed under Apache-2.0 and its installer is not stored in this repository.
See Microsoft's [supported runtime downloads and license guidance](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist).

## Optional offline translation engine

The application ships the setup and local-service source. The following runtime and models are downloaded only by the optional offline setup, into a separate local directory. Their original archive license and notice files are retained during installation. No LibreTranslate server code or Argos Python library is embedded; the local runner implements a compatible HTTP subset and uses Argos-format model packages.

| Component | Version | License / attribution |
| --- | --- | --- |
| [CPython](https://www.python.org/downloads/release/python-31210/) | 3.12.10 x64 embeddable | Python Software Foundation license; bundled notices retained |
| [CTranslate2](https://github.com/OpenNMT/CTranslate2) | 4.8.2 | MIT |
| [SentencePiece](https://github.com/google/sentencepiece) | 0.2.2 | Apache-2.0 |
| [NumPy](https://numpy.org/) | 2.3.5 | BSD-3-Clause; see bundled third-party notices for native dependencies |
| [PyYAML](https://pyyaml.org/) | 6.0.3 | MIT |
| [py3langid](https://pypi.org/project/py3langid/0.2.2/) | 0.2.2 | BSD-3-Clause |
| [Argos English/Chinese model packages](https://github.com/argosopentech/argospm-index) | en-zh / zh-en 1.9 | Original OPUS-MT models: CC-BY-4.0, Jörg Tiedemann and Santhosh Thottingal, *OPUS-MT — Building open translation services for the World*, EAMT 2020. See each installed model's README for full attribution. |

Setup verifies pinned SHA-256 hashes for the runtime, wheels, and model archives. A distribution that includes a preinstalled offline runtime must retain its Python license, wheel metadata/license directories, and model README files.

## Release obligation

Before distributing a release, generate a complete inventory from the resolved restore and publish assets, verify every direct and transitive license, and include all notices and license texts required by those dependencies. This file is an engineering aid, not a substitute for release-specific legal review.

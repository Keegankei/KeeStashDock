# Third-Party Notices

KeeStashDock itself is licensed under Apache-2.0. It depends on third-party software that remains subject to its own license terms. The following list covers the direct dependencies declared by this repository at version 1.1.2. Transitive dependencies are resolved by NuGet and must be included in the release-time dependency and license review.

## Runtime dependencies

| Component | Version | Purpose | License |
| --- | ---: | --- | --- |
| [CommunityToolkit.Mvvm](https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2) | 8.4.2 | MVVM source generators and helpers | MIT |
| [Microsoft.Data.Sqlite](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.10) | 10.0.10 | SQLite provider and native SQLite bundle | MIT |
| [Microsoft.Extensions.DependencyInjection](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/10.0.10) | 10.0.10 | Dependency-injection container | MIT |

## Development dependency

| Component | Version | Purpose | License |
| --- | ---: | --- | --- |
| [MSTest.Sdk](https://www.nuget.org/packages/MSTest.Sdk/4.3.2) | 4.3.2 | Test framework, runner, and test tooling | MIT |

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

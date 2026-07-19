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

## Release obligation

Before distributing a release, generate a complete inventory from the resolved restore and publish assets, verify every direct and transitive license, and include all notices and license texts required by those dependencies. This file is an engineering aid, not a substitute for release-specific legal review.

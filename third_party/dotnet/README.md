# .NET and SQLite dependency notices

These unmodified license and notice files accompany the managed application and
the self-contained Windows runtime. They were collected from the pinned NuGet
packages or the exact upstream tag on 2026-10-12.

| File | Source / covered components |
| --- | --- |
| CommunityToolkit-LICENSE.md | `CommunityToolkit.Mvvm` 8.4.2 package, `License.md`; .NET Foundation and Contributors |
| CommunityToolkit-NOTICES.txt | Same package, `ThirdPartyNotices.txt` |
| NET-LICENSE.txt | `Microsoft.NETCore.App.Runtime.win-x64` 10.0.12 package, `LICENSE.TXT`; MIT, .NET Foundation and Contributors. The same license applies to Microsoft.Data.Sqlite, dependency injection/logging abstractions, and Windows Desktop runtime. |
| NET-NOTICES.txt | Same runtime package, `THIRD-PARTY-NOTICES.TXT`; also provided with Microsoft.Extensions packages |
| SQLitePCLRaw-LICENSE.txt | [SQLitePCL.raw v2.1.12](https://raw.githubusercontent.com/ericsink/SQLitePCL.raw/v2.1.12/LICENSE.TXT), Apache-2.0 |

SQLitePCLRaw `core`, `provider.e_sqlite3`, `bundle_e_sqlite3`, and
`lib.e_sqlite3` 2.1.12: Copyright 2014-2024 SourceGear, LLC, as recorded in the
packages' `.nuspec` files. The bundled SQLite engine is public domain; see
[SQLite copyright](https://www.sqlite.org/copyright.html).

The general .NET notices include upstream components used by different runtime
builds. Preserve the original file instead of deleting notices based on filename
guesses. Refresh these files when the resolved runtime or package versions change.
OCR dependencies are documented separately in `Licenses/OCR` in a published app
and `third_party/ocr` in the source tree.

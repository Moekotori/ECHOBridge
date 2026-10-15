# Third-party components

The application is AGPL-3.0-only. This inventory covers the standalone Bridge source and dependencies, not the complete ECHO desktop distribution. Existing first-party code is attributed in NOTICE and SOURCE_PROVENANCE.json.

| Component | Version/source | License / treatment |
| --- | --- | --- |
| Microsoft.Data.Sqlite | 10.0.11, https://github.com/dotnet/efcore | MIT; unmodified NuGet dependency |
| SQLitePCLRaw / bundle_e_sqlite3 | 2.1.12, https://github.com/ericsink/SQLitePCL.raw | Apache-2.0; unmodified NuGet dependencies; retain applicable package licenses/notices |
| SQLite native library | Resolved by the above pinned bundle, https://www.sqlite.org/copyright.html | Public domain; version and hash must be recorded from the actual release inventory |
| .NET / ASP.NET Core / Windows Desktop | .NET 10 framework and installed SDK runtime packs | MIT and upstream third-party notices; self-contained binaries are not included in this source release |
| opencc-js search dictionary source | Version pinned in package-lock.json, https://github.com/nk2028/opencc-js | MIT implementation, Apache-2.0 dictionary data; generated search data is redistributed with full upstream notices below |
| TypeScript / esbuild | Exact dev versions in package-lock.json | Apache-2.0 / MIT; optional schema regeneration tools, not runtime dependencies |
| xUnit / Microsoft.NET.Test.Sdk | Versions in Echo.Bridge.Tests.csproj | Apache-2.0 / MIT; development-only test packages |

The unmodified generated dictionary is in `core/Echo.Core/Search/Data/opencc-search.json.gz`. The full upstream MIT text and Apache dictionary notices are preserved in `OPENCC-LICENSE.txt` and `OPENCC-THIRD-PARTY.md` in that directory. Regeneration uses the retained `scripts/generate-core-search-data.mjs` and pinned opencc-js package. No fonts, original icon artwork or media assets are exported; Windows provides the generic native application icon and system fonts.

Before any signed binary release, collect license texts and third-party notices from the **exact resolved** .NET runtime/ASP.NET/WindowsDesktop packs and NuGet dependencies, record versions/source URLs/hashes, and audit the final archive. `scripts/package-windows.ps1` requires these notices and valid PE signatures; a successful development build alone does not satisfy release verification.

Canonical license sources: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT ; https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt ; https://github.com/dotnet/winforms/blob/main/LICENSE.TXT ; https://github.com/dotnet/wpf/blob/main/LICENSE.TXT ; https://github.com/ericsink/SQLitePCL.raw/blob/master/LICENSE.TXT . These upstream links do not replace the applicable license texts in a binary distribution.

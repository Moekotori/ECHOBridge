# Building ECHO Bridge

## Windows application

Use a Windows x64 machine and .NET 10 SDK. `core/global.json` records the SDK baseline; NuGet dependencies are pinned in the project files. Build from the repository root:

```powershell
dotnet publish core/Echo.Bridge.Windows/Echo.Bridge.Windows.csproj -c Release -r win-x64 --self-contained true -o misc/win-x64
```

Keep the entire output directory together. The app is native WinForms with shared Library Core and ASP.NET HTTP services. No separate .NET installation is required by the end user. `misc` is ignored and development builds are not public release artifacts.

## Focused regression

```powershell
dotnet test core/Echo.Bridge.Tests/Echo.Bridge.Tests.csproj -c Release --logger trx --results-directory misc/test-results
```

Tests use isolated temporary SQLite/media fixtures. Existing read-only library/query and HTTP tests cover later database writes becoming visible, original media Range requests, write/browser rejection and media capability revocation. Settings and automatic profile discovery are covered without writing the user's real configuration.

The optional diagnostic `--ui-smoke <repository/misc/evidence>` renders a synthetic window. Diagnostic paths must remain under the caller's `misc` directory. Diagnostics are not an alternative to an actual phone/device acceptance test.

## Shared schema and dictionary regeneration

The canonical SQL declarations and original migration compiler are retained under `src/main/database` and `scripts/library-migrations`. To keep a normal build independent of Node, the initial export embeds an unchanged compiled migration resource in `core/Echo.Core/Database/library-migrations.json`. This retains the exact ECHO schema digest and does not let Bridge migrate a user library.

If canonical SQL changes, install Node.js and run:

```powershell
npm ci
node scripts/generate-library-migrations.mjs --write-contract --output core/Echo.Core/Database/library-migrations.json
```

For search data regeneration, run `npm run search:regenerate` and retain the copied upstream license files. These are optional development tools; they do not enter the Windows application runtime.

## Distribution

从干净的对应源码提交发布完整 Windows 自包含目录，补齐包内 LICENSE、NOTICE、THIRD_PARTY_NOTICES.md、OpenCC 许可及 licenses/ 下的运行时许可，再执行：

```powershell
./scripts/package-windows.ps1 -Directory misc/prepared-bundle -Version 26.10.15.2 -OutputDirectory misc/release
```

脚本核对文件范围、私有数据、许可证、对应源码和每项 SHA-256，并生成完整便携 ZIP 与 release-manifest.json。它不处理或要求证书。

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

The default packaging path remains a signed release: it retains corresponding source and all resolved third-party notices and requires valid signatures for every PE file. The first-party application/SQLite binaries must also match the configured publisher and have a timestamp. Signing credentials belong in a local environment or CI secrets only.

After signing and collecting notices, set the publisher environment variable and run the guarded packaging command:

The packaging script requires PowerShell 7; end users only need to run the included EXE and do not need PowerShell or an SDK.

```powershell
$env:ECHO_WINDOWS_PUBLISHER_NAME = 'Your verified publisher'
./scripts/package-windows.ps1 -Directory misc/win-x64 -Version 26.10.15 -OutputDirectory misc/release
```

This command verifies and packages already signed files; it does not acquire a certificate or bypass signing. The archive is portable; an installer can be added in a later release.

For the explicitly authorized 26.10.15 **unsigned test** distribution, add `-UnsignedTest` and place `UNSIGNED-TEST-BUILD.txt` in the prepared directory. This mode keeps all completeness, license, private-data, clean-source and hash checks, but records that signature verification was deliberately skipped. Its archive filename includes `-unsigned-test`, and the public release must remain labeled as a test preview.

```powershell
./scripts/package-windows.ps1 -Directory misc/prepared-bundle -Version 26.10.15 -OutputDirectory misc/release -UnsignedTest -ApplicationSourceCommit 5ac2f9e1381a7f5b454899a4c1e1d64037f3d239
```

When only packaging/docs change, `ApplicationSourceCommit` may point to the unchanged application tag; the script verifies that `core` matches it and records the packaging commit separately. Do not move the existing source tag or substitute a binary compiled from different application code.

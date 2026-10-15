param(
    [Parameter(Mandatory=$true)][string]$Directory,
    [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?$')][string]$Version,
    [string]$OutputDirectory = 'misc/release',
    [switch]$UnsignedTest,
    [ValidatePattern('^[a-f0-9]{40}$')][string]$ApplicationSourceCommit
)
$ErrorActionPreference = 'Stop'
$publisher = $env:ECHO_WINDOWS_PUBLISHER_NAME
if (-not $UnsignedTest -and [string]::IsNullOrWhiteSpace($publisher)) { throw 'A verified Windows publisher is required for a signed release. Use -UnsignedTest only for an explicitly authorized and labeled test distribution.' }
$root = Split-Path -Parent $PSScriptRoot
$bundle = (Resolve-Path -LiteralPath $Directory).Path
$output = [IO.Path]::GetFullPath($OutputDirectory, $root)
$allowed = [IO.Path]::GetFullPath((Join-Path $root 'misc')) + [IO.Path]::DirectorySeparatorChar
if (-not $output.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package output must be under repository misc.' }
if (-not (Test-Path -LiteralPath (Join-Path $bundle 'ECHO Bridge.exe'))) { throw 'Bridge executable is missing.' }
$files = @(Get-ChildItem -LiteralPath $bundle -Recurse -File)
if ($files.Count -gt 2048 -or ($files | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })) { throw 'Invalid package file inventory.' }
if ($files | Where-Object { $_.Name -match '(?i)steam_api|steamworks|electron|ffmpeg|audio-host|scanner|\.pfx$|\.p12$|\.sqlite$|\.db$|^\.env' }) { throw 'Unexpected component or private data in package.' }
if (Test-Path -LiteralPath (Join-Path $bundle 'bridge-manifest.json')) { throw 'Development bundle marker must not enter a release.' }
if ($UnsignedTest -and -not (Test-Path -LiteralPath (Join-Path $bundle 'UNSIGNED-TEST-BUILD.txt'))) { throw 'Unsigned test distribution must carry its explicit warning file.' }
foreach ($name in @('LICENSE','NOTICE','THIRD_PARTY_NOTICES.md','OPENCC-LICENSE.txt','OPENCC-THIRD-PARTY.md','licenses/runtime-LICENSE.txt','licenses/runtime-ThirdPartyNotices.txt','licenses/aspnetcore-LICENSE.txt','licenses/aspnetcore-ThirdPartyNotices.txt','licenses/windowsdesktop-LICENSE.txt','licenses/windowsdesktop-ThirdPartyNotices.txt','licenses/sqlitepclraw-LICENSE.txt','licenses/microsoft-data-sqlite-LICENSE.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $bundle $name))) { throw "Required license file missing: $name" }
}
$pes = @($files | Where-Object { $_.Extension -in '.exe','.dll','.node' })
if ($pes.Count -gt 512) { throw 'PE inventory exceeds the signature budget.' }
foreach ($file in $(if ($UnsignedTest) { @() } else { $pes })) {
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
    if ($signature.Status -ne 'Valid') { throw "Invalid PE signature: $($file.Name)" }
    if ($file.Name -in 'ECHO Bridge.exe','ECHO Bridge.dll','Echo.Bridge.dll','Echo.Core.dll','e_sqlite3.dll') {
        $name = $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
        if ($name -ne $publisher -or -not $signature.TimeStamperCertificate -or $signature.SignerCertificate.PublicKey.Oid.Value -ne '1.2.840.113549.1.1.1') { throw "Publisher, RSA key or timestamp mismatch: $($file.Name)" }
    }
}
$commit = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40}$') { throw 'Cannot record source commit.' }
if (& git -C $root status --porcelain --untracked-files=normal) { throw 'Release must use a clean source snapshot.' }
if (-not $ApplicationSourceCommit) { $ApplicationSourceCommit = $commit }
& git -C $root diff --quiet $ApplicationSourceCommit -- core
if ($LASTEXITCODE -ne 0) { throw 'Application code differs from the stated corresponding source commit.' }
$inventory = @($files | ForEach-Object { @{ name=[IO.Path]::GetRelativePath($bundle,$_.FullName).Replace([IO.Path]::DirectorySeparatorChar,[char]'/'); size=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })
@{ version=$Version; applicationSourceCommit=$ApplicationSourceCommit; packagingCommit=$commit; packagedAtUtc=[DateTime]::UtcNow.ToString('o'); distribution=$(if ($UnsignedTest) {'unsigned-test'} else {'signed'}); signatureVerification=$(if ($UnsignedTest) {'skipped-explicit-unsigned-test'} else {'passed'}); source="https://github.com/Moekotori/ECHOBridge/tree/$ApplicationSourceCommit"; files=$inventory } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $bundle 'release-manifest.json') -Encoding utf8
New-Item -ItemType Directory -Path $output -Force | Out-Null
$archive = Join-Path $output "ECHO-Bridge-$Version-win-x64$(if ($UnsignedTest) {'-unsigned-test'}).zip"
if (Test-Path -LiteralPath $archive) { throw 'Refusing to replace an existing release archive.' }
Compress-Archive -LiteralPath @((Get-ChildItem -LiteralPath $bundle).FullName) -DestinationPath $archive -CompressionLevel Fastest
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath (Join-Path $output $(if ($UnsignedTest) {'SHA256SUMS-unsigned-test.txt'} else {'SHA256SUMS.txt'})) -Encoding ascii
Write-Output "Verified $(if ($UnsignedTest) {'unsigned test'} else {'signed'}) archive: $archive"

# Build the TTC Weighing installer (one Setup .exe).
#   powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 1.0.0
# Output: installer\out\TTC-Weighing-Setup-<Version>.exe
# Requires: .NET 8 SDK, Inno Setup 6 (https://jrsoftware.org/isdl.php). Downloads are cached in installer\.cache.
param(
    [string]$Version = '1.0.0',
    [string]$PostgresVersion = '16.15-5'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$here = $PSScriptRoot
$root = Split-Path $here -Parent
$cache = Join-Path $here '.cache'
$out = Join-Path $here 'out'
$appOut = Join-Path $out 'app'
$pgOut = Join-Path $out 'pgsql'
New-Item -ItemType Directory -Force -Path $cache, $out | Out-Null

function Get-Download([string]$url, [string]$file) {
    $path = Join-Path $cache $file
    if (Test-Path $path) { return $path }
    # Download to .part (resumable with -C -), rename only when complete.
    $part = "$path.part"
    & curl.exe -L --fail -C - -o $part $url
    if ($LASTEXITCODE -ne 0) { throw "Download failed ($LASTEXITCODE): $url" }
    Move-Item -Force $part $path
    return $path
}

# 1. Publish self-contained app (no .NET runtime needed on the station).
Write-Host '== Publish app'
if (Test-Path $appOut) { Remove-Item -Recurse -Force $appOut }
& dotnet publish (Join-Path $root 'src\TTC.Weighing.WinForms\TTC.Weighing.WinForms.csproj') `
    -c Release -r win-x64 --self-contained true -p:Version=$Version -o $appOut
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
# Never ship developer config (may hold credentials) or debug symbols.
Get-ChildItem $appOut -Recurse -Include 'appsettings.Development.json', 'appsettings.Local.json', '*.pdb' |
    Remove-Item -Force

# 2. PostgreSQL server binaries (bin, lib, share only; skip pgAdmin, StackBuilder, docs, headers).
Write-Host "== PostgreSQL $PostgresVersion"
$pgZipName = "postgresql-$PostgresVersion-windows-x64-binaries.zip"
$marker = Join-Path $pgOut '.source'
if (-not ((Test-Path $marker) -and ((Get-Content $marker) -eq $pgZipName))) {
    $pgZip = Get-Download "https://get.enterprisedb.com/postgresql/$pgZipName" $pgZipName
    if (Test-Path $pgOut) { Remove-Item -Recurse -Force $pgOut }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($pgZip)
    try {
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName -notmatch '^pgsql/(bin|lib|share)/' -or $entry.FullName.EndsWith('/')) { continue }
            $target = Join-Path $pgOut ($entry.FullName.Substring(6) -replace '/', '\')
            New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
        foreach ($name in 'server_license.txt', 'commandlinetools_3rd_party_licenses.txt') {
            $entry = $zip.GetEntry("pgsql/$name")
            if ($entry) { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $pgOut $name), $true) }
        }
    }
    finally { $zip.Dispose() }
    Set-Content -Path $marker -Value $pgZipName
}

# 3. VC++ 2015-2022 runtime required by PostgreSQL binaries.
Write-Host '== VC++ runtime'
$vc = Get-Download 'https://aka.ms/vs/17/release/vc_redist.x64.exe' 'vc_redist.x64.exe'
Copy-Item $vc (Join-Path $out 'vc_redist.x64.exe') -Force

# 4. Station settings bundled into the installer (FRM accounts, API URLs). Gitignored, never committed.
$bundle = Join-Path $here 'bundle-settings.json'
$bundleOut = Join-Path $out 'bundle-settings.json'
Remove-Item $bundleOut -ErrorAction SilentlyContinue
if (Test-Path $bundle) {
    Get-Content -Raw -Encoding UTF8 $bundle | ConvertFrom-Json | Out-Null   # fail early on invalid JSON
    Copy-Item $bundle $bundleOut
}
else {
    Write-Warning 'installer\bundle-settings.json not found: installer will have no FRM accounts.'
}

# 5. Compile installer.
Write-Host '== Inno Setup'
$iscc = @(
    (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found. Install from https://jrsoftware.org/isdl.php' }

& $iscc "/DAppVersion=$Version" (Join-Path $here 'TTCWeighing.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }

$setup = Join-Path $out "TTC-Weighing-Setup-$Version.exe"
Write-Host ("== Done: {0} ({1:N0} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))

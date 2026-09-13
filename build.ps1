#requires -Version 7.0
<#
.SYNOPSIS
    Builds, tests and packages Translator for Windows.

.DESCRIPTION
    Publishes a self-contained, framework-independent build of the Translator WPF app for one or
    both supported RIDs (win-x64, win-arm64), zips the publish folder into a portable archive, and
    — when Inno Setup's ISCC.exe is available — builds a per-user installer. Produces a
    SHA256SUMS.txt covering every file dropped in dist/.

    Publish layout: folder (not single-file). Measured on this repo (win-x64, .NET 10, WPF,
    self-contained, 2026-09):
      - folder publish:            ~157 MB, 248 files, steady-state cold start ~170-260 ms
      - zipped for distribution:   ~65 MB   (same as a single-file+compressed exe)
      - single-file + compression: ~65 MB exe, but every launch pays a ~140 ms decompression tax
        (this is a tray app relaunched on every sign-in via --autostart, so a permanent per-launch
        tax is worse than the folder having many files)
      - single-file, no compression: ~150 MB (barely smaller than the folder), first run pays a
        ~500 ms one-time native-library self-extraction cost, later runs match the folder
      - ReadyToRun: +~19% size (+30 MB) with no measurable startup win in this app (WPF's own JIT
        dominates only once the UI is actually exercised, which the current shell does not do yet)
    => folder-based publish, zipped for the portable artifact, no PublishSingleFile, no R2R.
    This also keeps ONNX Runtime's native DLLs (referenced by Translator.Offline) sitting next to
    the exe with no self-extraction cache to manage once the offline engine lands.

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER Runtime
    Target RID: win-x64, win-arm64, or all (both). Default: win-x64.

.PARAMETER Version
    Version string stamped into the build (assembly version / installer version / archive names).
    Defaults to the Version already set in Directory.Build.props.

.PARAMETER SkipTests
    Skip `dotnet test` before publishing.

.EXAMPLE
    ./build.ps1 -Runtime win-x64

.EXAMPLE
    ./build.ps1 -Runtime all -Version 1.2.0
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64', 'win-arm64', 'all')]
    [string]$Runtime = 'win-x64',

    [string]$Version,

    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$RepoRoot = $PSScriptRoot
$DistDir = Join-Path $RepoRoot 'dist'
$PublishRoot = Join-Path $DistDir 'publish'
$SlnPath = Join-Path $RepoRoot 'Translator.slnx'
$AppProject = Join-Path $RepoRoot 'src\Translator\Translator.csproj'

# ---------------------------------------------------------------------------------------------
# 1. Find a dotnet executable that actually has the SDK global.json asks for.
#    The PATH `dotnet` on this machine can be a stub with no SDKs installed at all, so prefer the
#    per-user install under %LOCALAPPDATA% when it has one.
# ---------------------------------------------------------------------------------------------
function Find-Dotnet {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'),
        'dotnet'
    )
    foreach ($candidate in $candidates) {
        $exe = if (Test-Path $candidate -PathType Leaf) { $candidate } else { (Get-Command $candidate -ErrorAction SilentlyContinue)?.Source }
        if (-not $exe) { continue }
        $sdks = & $exe --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and $sdks) {
            Write-Verbose "Using dotnet at '$exe' (SDKs: $($sdks -join '; '))"
            return $exe
        }
    }
    throw "No dotnet executable with an installed SDK was found (checked %LOCALAPPDATA%\Microsoft\dotnet and PATH). Install the .NET 10 SDK from https://dotnet.microsoft.com/download."
}

$Dotnet = Find-Dotnet
Write-Host "dotnet: $Dotnet" -ForegroundColor DarkGray
& $Dotnet --version | ForEach-Object { Write-Host "SDK: $_" -ForegroundColor DarkGray }

# ---------------------------------------------------------------------------------------------
# 2. Resolve version.
# ---------------------------------------------------------------------------------------------
if (-not $Version) {
    $propsPath = Join-Path $RepoRoot 'Directory.Build.props'
    $propsXml = [xml](Get-Content $propsPath -Raw)
    $Version = $propsXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { $Version = '0.0.0' }
}
Write-Host "Version: $Version" -ForegroundColor Cyan

$Runtimes = if ($Runtime -eq 'all') { @('win-x64', 'win-arm64') } else { @($Runtime) }

# ---------------------------------------------------------------------------------------------
# 3. Restore + test.
# ---------------------------------------------------------------------------------------------
Write-Host "==> Restoring $SlnPath" -ForegroundColor Cyan
& $Dotnet restore $SlnPath
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

if (-not $SkipTests) {
    Write-Host "==> Running tests ($Configuration)" -ForegroundColor Cyan
    & $Dotnet test $SlnPath -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }
}
else {
    Write-Host "==> Skipping tests (-SkipTests)" -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------------------------
# 4. Publish (folder, self-contained, per RID) + zip + (optional) installer.
# ---------------------------------------------------------------------------------------------
if (Test-Path $DistDir) { Remove-Item $DistDir -Recurse -Force }
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

$IsccPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if (-not $IsccPath) {
    $wellKnown = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )
    $IsccPath = $wellKnown | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if ($IsccPath) {
    Write-Host "Inno Setup found: $IsccPath" -ForegroundColor DarkGray
}
else {
    Write-Host "Inno Setup (ISCC.exe) not found — installer step will be skipped." -ForegroundColor Yellow
}

$producedFiles = @()

foreach ($rid in $Runtimes) {
    Write-Host "==> Publishing $rid ($Configuration, self-contained, folder)" -ForegroundColor Cyan
    $ridPublishDir = Join-Path $PublishRoot $rid
    & $Dotnet publish $AppProject `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -p:Version=$Version `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=false `
        -o $ridPublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid with exit code $LASTEXITCODE" }

    $sizeMb = [math]::Round(((Get-ChildItem $ridPublishDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB), 1)
    $fileCount = (Get-ChildItem $ridPublishDir -Recurse -File | Measure-Object).Count
    Write-Host "    publish folder: $sizeMb MB, $fileCount files" -ForegroundColor DarkGray

    $zipName = "Translator-$Version-$rid-portable.zip"
    $zipPath = Join-Path $DistDir $zipName
    Write-Host "==> Zipping -> $zipName" -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $ridPublishDir '*') -DestinationPath $zipPath -Force
    $zipSizeMb = [math]::Round(((Get-Item $zipPath).Length / 1MB), 1)
    Write-Host "    zip size: $zipSizeMb MB" -ForegroundColor DarkGray
    $producedFiles += $zipPath

    if ($IsccPath) {
        $arch = if ($rid -eq 'win-arm64') { 'arm64' } else { 'x64' }
        Write-Host "==> Building installer for $rid" -ForegroundColor Cyan
        $issPath = Join-Path $RepoRoot 'installer\Translator.iss'
        & $IsccPath `
            "/DAppVersion=$Version" `
            "/DAppArch=$arch" `
            "/DRid=$rid" `
            "/DSourceDir=$ridPublishDir" `
            "/DOutputDir=$DistDir" `
            $issPath
        if ($LASTEXITCODE -ne 0) { throw "ISCC.exe failed for $rid with exit code $LASTEXITCODE" }
        $setupPath = Join-Path $DistDir "TranslatorSetup-$Version-$rid.exe"
        if (Test-Path $setupPath) { $producedFiles += $setupPath }
    }
}

# ---------------------------------------------------------------------------------------------
# 5. Checksums.
# ---------------------------------------------------------------------------------------------
if ($producedFiles.Count -gt 0) {
    $sumsPath = Join-Path $DistDir 'SHA256SUMS.txt'
    Write-Host "==> Writing $sumsPath" -ForegroundColor Cyan
    $lines = foreach ($file in ($producedFiles | Sort-Object)) {
        $hash = (Get-FileHash -Path $file -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $([System.IO.Path]::GetFileName($file))"
    }
    Set-Content -Path $sumsPath -Value $lines -Encoding utf8NoBOM
}

Write-Host ""
Write-Host "==> Done. Contents of dist/:" -ForegroundColor Green
Get-ChildItem $DistDir -File | ForEach-Object {
    "{0,10:N1} MB  {1}" -f ($_.Length / 1MB), $_.Name
} | Write-Host

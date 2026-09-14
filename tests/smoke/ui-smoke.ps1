#requires -Version 7.2
<#
.SYNOPSIS
    UI smoke test of a published Translator build on the current machine.

.DESCRIPTION
    Runs in CI on Windows Server 2022 (Windows 10 generation), Server 2025 (Windows 11 generation)
    and Windows 11 ARM64 (.github/workflows/ui-smoke.yml), and works the same locally.

    With an isolated TRANSLATOR_DATA_DIR it:
      1. reports the OS, the display and whether the icon fonts contain every glyph the UI uses;
      2. publishes the app self-contained for -Runtime;
      3. for each theme and each window: writes settings.json, starts the app, runs --open <window>,
         captures the virtual screen, checks the process is alive and debug.log has no unhandled
         exceptions, then exits it with --quit and checks the tray icon was removed;
      4. runs --test-translate (network/service errors are warnings);
      5. publishes tools/OfflineCli for the same RID, downloads Russian and runs --test-offline,
         which proves ONNX Runtime works natively on this CPU;
      6. with -Installer: builds the Inno Setup installer, installs it silently, smoke-starts the
         installed app, uninstalls it silently while it runs and checks the HKCU Run value is gone.

    Screenshots, logs, environment.txt and summary.md are written to -OutputDir.
    Exit code 1 when any check failed.

.EXAMPLE
    ./tests/smoke/ui-smoke.ps1 -Runtime win-x64 -SkipOffline
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime,

    [string]$OutputDir = 'artifacts/smoke',

    [switch]$Installer,

    [switch]$SkipOffline,

    # Reuse the previous publish in artifacts/smoke-build (local iteration).
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Runtime) {
    $Runtime = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [Runtime.InteropServices.Architecture]::Arm64) { 'win-arm64' } else { 'win-x64' }
}
$OutputDir = [IO.Path]::GetFullPath($OutputDir, (Get-Location).Path)
$ShotsDir = Join-Path $OutputDir 'screenshots'
$BuildDir = Join-Path $RepoRoot "artifacts\smoke-build\$Runtime"
$PublishDir = Join-Path $BuildDir 'app'
$CliDir = Join-Path $BuildDir 'offline-cli'
$InstallerDir = Join-Path $BuildDir 'installer'
$TempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$DataDir = Join-Path $TempRoot "translator-smoke-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$AppExe = Join-Path $PublishDir 'Translator.exe'
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\Translator'
$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

$Themes = @('CalmGlass', 'NeonGlass', 'FrostGlass')
$Windows = @('panel', 'settings', 'history', 'offline', 'firstrun')

$Failures = [Collections.Generic.List[string]]::new()
$Warnings = [Collections.Generic.List[string]]::new()
$Notes = [Collections.Generic.List[string]]::new()
$SeenWarnings = [Collections.Generic.HashSet[string]]::new()

function Write-Step([string]$message) { Write-Host "==> $message" -ForegroundColor Cyan }

function Add-Failure([string]$message) {
    $Failures.Add($message)
    Write-Host "FAIL: $message" -ForegroundColor Red
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Host "::error::$(($message -split "`n")[0])" }
}

function Add-Warning([string]$message) {
    if (-not $SeenWarnings.Add($message)) { return }
    $Warnings.Add($message)
    Write-Host "WARN: $message" -ForegroundColor Yellow
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Host "::warning::$(($message -split "`n")[0])" }
}

function Add-Note([string]$message) {
    $Notes.Add($message)
    Write-Host $message
}

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class SmokeNative
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);

    /// <summary>Visible top-level windows of a process larger than a few pixels (the hidden 1x1 menu host doesn't count).</summary>
    public static List<string> VisibleWindows(int processId)
    {
        var result = new List<string>();
        EnumWindows((hwnd, _) =>
        {
            uint pid;
            RECT r;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == (uint)processId && IsWindowVisible(hwnd) && GetWindowRect(hwnd, out r) && r.Right - r.Left > 8 && r.Bottom - r.Top > 8)
            {
                var title = new StringBuilder(256);
                var className = new StringBuilder(256);
                GetWindowText(hwnd, title, title.Capacity);
                GetClassName(hwnd, className, className.Capacity);
                result.Add(string.Format("'{0}' at {1},{2} size {3}x{4} dpi {5}", title, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, GetDpiForWindow(hwnd)));
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@
# Physical pixels for the capture on scaled displays.
[void][SmokeNative]::SetProcessDpiAwarenessContext([IntPtr]::new(-4))

function Find-Dotnet {
    foreach ($candidate in @((Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'), 'dotnet')) {
        $exe = if (Test-Path $candidate -PathType Leaf) { $candidate } else { (Get-Command $candidate -ErrorAction SilentlyContinue)?.Source }
        if ($exe -and (& $exe --list-sdks 2>$null)) { return $exe }
    }
    throw 'No dotnet executable with an installed SDK was found.'
}

function Get-PeMachine([string]$path) {
    $buffer = [byte[]]::new(4096)
    $stream = [IO.File]::OpenRead($path)
    try { [void]$stream.Read($buffer, 0, $buffer.Length) } finally { $stream.Dispose() }
    $peOffset = [BitConverter]::ToInt32($buffer, 0x3C)
    switch ([BitConverter]::ToUInt16($buffer, $peOffset + 4)) {
        0x8664 { 'x64' }
        0xAA64 { 'ARM64' }
        0x014C { 'x86' }
        default { 'machine 0x{0:X4}' -f $_ }
    }
}

function Assert-Architecture([string]$path) {
    $expected = if ($Runtime -eq 'win-arm64') { 'ARM64' } else { 'x64' }
    if (-not (Test-Path $path)) { Add-Failure "Missing $path"; return }
    $machine = Get-PeMachine $path
    $name = Split-Path $path -Leaf
    if ($machine -eq $expected) { Add-Note "$name is $machine" } else { Add-Failure "$name is $machine, expected $expected" }
}

function Read-LogLines {
    $path = Join-Path $DataDir 'logs\debug.log'
    if (-not (Test-Path $path)) { return @() }
    $stream = [IO.FileStream]::new($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try { return @(([IO.StreamReader]::new($stream)).ReadToEnd() -split "`r?`n" | Where-Object { $_ }) } finally { $stream.Dispose() }
}

function Get-LogCount { return @(Read-LogLines).Count }

# Checks log lines written since $start; returns them.
function Test-LogSince([int]$start, [string]$context) {
    $lines = @(Read-LogLines)
    $new = @(if ($lines.Count -gt $start) { $lines[$start..($lines.Count - 1)] })
    for ($i = 0; $i -lt $new.Count; $i++) {
        $text = $new[$i] -replace '^\d{4}-\d{2}-\d{2} [\d:.]+ [+-]\d{2}:\d{2} ', ''
        if ($text -match 'Unhandled|Unobserved task exception') {
            $detail = $new[$i..([Math]::Min($i + 8, $new.Count - 1))] -join "`n"
            Add-Failure "${context}: exception in debug.log`n$detail"
        }
        elseif ($text -match 'failed|unavailable|timed out|error' -and $text -notmatch 'Canceled') {
            # Cancellations are expected: --quit closes windows while their background refreshes still run.
            Add-Warning "debug.log: $text"
        }
    }
    return $new
}

function Write-Settings([string]$theme) {
    New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
    [ordered]@{
        HasCompletedFirstRun   = $true
        AppTheme               = $theme
        AppLanguage            = 'En'
        PanelSizeMode          = 'Standard'
        SourceCode             = 'auto'
        TargetCode             = 'ru'
        OfflineUpdateLastCheck = (Get-Date).ToString('o')
    } | ConvertTo-Json | Set-Content -Path (Join-Path $DataDir 'settings.json') -Encoding utf8NoBOM

    # One entry, so the history list and its buttons are on the screenshot.
    $entry = [ordered]@{
        Id                 = [guid]::NewGuid().ToString()
        Date               = (Get-Date).ToString('o')
        SourceCode         = $null
        DetectedSourceCode = 'en'
        TargetCode         = 'ru'
        Engine             = 'Google'
        Input              = 'Hello world'
        Output             = [regex]::Unescape('Привет, мир')
    }
    ConvertTo-Json -InputObject @($entry) | Set-Content -Path (Join-Path $DataDir 'history.json') -Encoding utf8NoBOM
}

function Invoke-Captured([string]$file, [string[]]$arguments, [int]$timeoutSeconds = 120) {
    $info = [Diagnostics.ProcessStartInfo]::new($file)
    foreach ($argument in $arguments) { $info.ArgumentList.Add($argument) }
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $info.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    $process = [Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $timedOut = -not $process.WaitForExit($timeoutSeconds * 1000)
    if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
    [pscustomobject]@{
        ExitCode = if ($timedOut) { -1 } else { $process.ExitCode }
        TimedOut = $timedOut
        Out      = $stdout.Result.Trim()
        Err      = $stderr.Result.Trim()
    }
}

function Save-Screenshot([string]$name, [string]$context) {
    New-Item -ItemType Directory -Force -Path $ShotsDir | Out-Null
    $path = Join-Path $ShotsDir "$name.png"
    try {
        $left = [SmokeNative]::GetSystemMetrics(76)
        $top = [SmokeNative]::GetSystemMetrics(77)
        $bitmap = [Drawing.Bitmap]::new([SmokeNative]::GetSystemMetrics(78), [SmokeNative]::GetSystemMetrics(79))
        try {
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try { $graphics.CopyFromScreen($left, $top, 0, 0, $bitmap.Size) } finally { $graphics.Dispose() }
            $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $bitmap.Dispose() }
    }
    catch {
        Add-Failure "${context}: screenshot failed ($($_.Exception.Message))"
    }
}

function Start-App([string]$exe, [string]$context) {
    $logStart = Get-LogCount
    $process = Start-Process -FilePath $exe -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $started = $false
    while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        if (@(Read-LogLines | Select-Object -Skip $logStart) -match 'App: starting') { $started = $true; break }
        Start-Sleep -Milliseconds 200
    }
    if ($process.HasExited) {
        Add-Failure "${context}: app exited during startup with code $($process.ExitCode)"
        [void](Test-LogSince $logStart $context)
        return $null
    }
    if (-not $started) { Add-Failure "${context}: no 'App: starting' in debug.log within 60 s" }
    # Arguments forwarded now are handled right after AppController.Start.
    Start-Sleep -Milliseconds 800
    [pscustomobject]@{ Process = $process; LogStart = $logStart; Exe = $exe }
}

function Wait-VisibleWindows($process, [int]$timeoutSeconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    do {
        $windows = [SmokeNative]::VisibleWindows($process.Id)
        if ($windows.Count -gt 0 -or $process.HasExited) { return $windows }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    return $windows
}

function Stop-App($app, [string]$context) {
    if ($app.Process.HasExited) {
        Add-Failure "${context}: app is not running any more (exit code $($app.Process.ExitCode))"
        [void](Test-LogSince $app.LogStart $context)
        return
    }
    $quit = Start-Process -FilePath $app.Exe -ArgumentList '--quit' -PassThru -Wait
    if ($quit.ExitCode -ne 0) { Add-Failure "${context}: --quit exited with code $($quit.ExitCode)" }
    if (-not $app.Process.WaitForExit(20000)) {
        Add-Failure "${context}: app still running 20 s after --quit"
        $app.Process.Kill($true)
    }
    $lines = Test-LogSince $app.LogStart $context
    if (-not ($lines -match 'TrayIcon: removed')) { Add-Failure "${context}: no 'TrayIcon: removed' in debug.log after --quit" }
}

function Get-UsedGlyphs {
    $glyphs = [Collections.Generic.SortedDictionary[int, string]]::new()
    $icons = Get-Content (Join-Path $RepoRoot 'src\Translator\UI\Icons.cs') -Raw -Encoding utf8
    foreach ($match in [regex]::Matches($icons, 'const string (\w+) = "(?:\\u([0-9A-Fa-f]{4})|(.))"')) {
        $code = if ($match.Groups[2].Success) { [Convert]::ToInt32($match.Groups[2].Value, 16) } else { [int][char]$match.Groups[3].Value }
        $glyphs[$code] = "Icons.$($match.Groups[1].Value)"
    }
    foreach ($file in Get-ChildItem (Join-Path $RepoRoot 'src\Translator') -Recurse -Include *.xaml, *.cs) {
        foreach ($match in [regex]::Matches((Get-Content $file.FullName -Raw -Encoding utf8), '&#x([0-9A-Fa-f]{4});')) {
            $code = [Convert]::ToInt32($match.Groups[1].Value, 16)
            if ($code -ge 0xE000 -and $code -le 0xF8FF -and -not $glyphs.ContainsKey($code)) { $glyphs[$code] = $file.Name }
        }
    }
    return , $glyphs
}

function Get-GlyphTypefaceFor([string]$family) {
    $fontFamily = [Windows.Media.Fonts]::SystemFontFamilies | Where-Object { $_.Source -eq $family } | Select-Object -First 1
    if (-not $fontFamily) { return $null }
    $typeface = [Windows.Media.Typeface]::new($fontFamily, [Windows.FontStyles]::Normal, [Windows.FontWeights]::Normal, [Windows.FontStretches]::Normal)
    $glyphTypeface = $null
    if ($typeface.TryGetGlyphTypeface([ref]$glyphTypeface)) { return $glyphTypeface }
    return $null
}

function Test-Fonts {
    try { Add-Type -AssemblyName PresentationCore } catch { Add-Warning "Font check skipped: WPF is not available in this PowerShell ($($_.Exception.Message))"; return }

    $baseXaml = Get-Content (Join-Path $RepoRoot 'src\Translator\UI\Styles\Base.xaml') -Raw
    if ($baseXaml -notmatch '<FontFamily x:Key="IconFontFamily">([^<]+)</FontFamily>') { Add-Failure 'IconFontFamily not found in Base.xaml'; return }
    $chain = @($Matches[1].Split(',') | ForEach-Object { $_.Trim() })

    $fonts = @{}
    foreach ($family in @($chain + 'Segoe MDL2 Assets' + 'Segoe UI' + 'Segoe UI Variable Text' | Select-Object -Unique)) {
        $glyphTypeface = Get-GlyphTypefaceFor $family
        $fonts[$family] = $glyphTypeface
        $state = if ($glyphTypeface) { "installed, $($glyphTypeface.VersionStrings[[Globalization.CultureInfo]::GetCultureInfo('en-US')])" } else { 'not installed' }
        Add-Note "Font '$family': $state"
    }
    if (-not $fonts['Segoe UI']) { Add-Failure "Font 'Segoe UI' (UI text fallback) is not installed" }
    $mdl2 = $fonts['Segoe MDL2 Assets']
    if (-not $mdl2) { Add-Failure "Font 'Segoe MDL2 Assets' (Windows 10 icon font) is not installed" }

    $glyphs = Get-UsedGlyphs
    $renderedBy = @{}
    foreach ($glyph in $glyphs.GetEnumerator()) {
        $label = 'U+{0:X4} ({1})' -f $glyph.Key, $glyph.Value
        if ($mdl2 -and -not $mdl2.CharacterToGlyphMap.ContainsKey($glyph.Key)) {
            Add-Failure "Glyph $label is missing in Segoe MDL2 Assets $($mdl2.VersionStrings[[Globalization.CultureInfo]::GetCultureInfo('en-US')]) - Windows 10 has no other icon font"
        }
        $renderer = $chain | Where-Object { $fonts[$_] -and $fonts[$_].CharacterToGlyphMap.ContainsKey($glyph.Key) } | Select-Object -First 1
        if ($renderer) { $renderedBy[$renderer] = 1 + [int]$renderedBy[$renderer] }
        else { Add-Failure "Glyph $label is in none of the installed icon fonts ($($chain -join ', ')): it renders as a box" }
    }
    $byFont = ($renderedBy.GetEnumerator() | ForEach-Object { "$($_.Value) by $($_.Key)" }) -join ', '
    Add-Note "Icon glyphs used by the UI: $($glyphs.Count); rendered $byFont"
}

function Test-Environment {
    Write-Step 'Environment'
    $os = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    # ProductName still says "Windows 10" on Windows 11 clients; the build number is what matters.
    $generation = if ([int]$os.CurrentBuild -ge 22000) { 'Windows 11 generation' } else { 'Windows 10 generation' }
    Add-Note "OS: $($os.ProductName) $($os.DisplayVersion) build $($os.CurrentBuild).$($os.UBR) ($($os.InstallationType), $generation)"
    Add-Note "OS architecture: $([Runtime.InteropServices.RuntimeInformation]::OSArchitecture); runtime: $Runtime; logical CPUs: $([Environment]::ProcessorCount)"
    Add-Note ("Virtual screen: {0}x{1} at {2},{3}; monitors: {4}; system DPI: {5}" -f `
            [SmokeNative]::GetSystemMetrics(78), [SmokeNative]::GetSystemMetrics(79), [SmokeNative]::GetSystemMetrics(76), `
            [SmokeNative]::GetSystemMetrics(77), [SmokeNative]::GetSystemMetrics(80), [SmokeNative]::GetDpiForSystem())
    $personalize = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -ErrorAction SilentlyContinue
    Add-Note "Transparency effects: $(if ($null -eq $personalize.EnableTransparency) { 'not set' } else { $personalize.EnableTransparency }); build supports DWM system backdrop (22621+): $([int]$os.CurrentBuild -ge 22621)"
    Test-Fonts
}

function Publish-App($dotnet) {
    if ($SkipPublish -and (Test-Path $AppExe)) { Add-Note "Reusing $PublishDir"; return }
    Write-Step "Publishing Translator for $Runtime"
    & $dotnet publish (Join-Path $RepoRoot 'src\Translator\Translator.csproj') -c Release -r $Runtime --self-contained true `
        -p:PublishSingleFile=false -p:PublishReadyToRun=false -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
}

function Test-Windows {
    foreach ($theme in $Themes) {
        foreach ($window in $Windows) {
            $context = "$theme/$window"
            Write-Step "Window $context"
            Write-Settings $theme
            $app = Start-App $AppExe $context
            if (-not $app) { continue }
            $open = Start-Process -FilePath $AppExe -ArgumentList '--open', $window -PassThru -Wait
            if ($open.ExitCode -ne 0) { Add-Failure "${context}: --open exited with code $($open.ExitCode)" }
            $visible = Wait-VisibleWindows $app.Process 15
            if ($visible.Count -eq 0) { Add-Failure "${context}: no visible window after --open $window" }
            else { Add-Note "${context}: $($visible -join '; ')" }
            # Let the first frame and DWM effects settle before the capture.
            Start-Sleep -Milliseconds 1500
            Save-Screenshot "$theme-$window" $context
            Stop-App $app $context
        }
    }
}

function Test-OnlineTranslation {
    Write-Step 'Online translation (--test-translate)'
    $logStart = Get-LogCount
    $result = Invoke-Captured $AppExe @('--test-translate', 'Hello world') 90
    if ($result.ExitCode -eq 0 -and $result.Out) {
        if ($result.Out -match '\p{IsCyrillic}') { Add-Note "--test-translate 'Hello world' -> '$($result.Out)'" }
        else { Add-Warning "--test-translate returned '$($result.Out)' (expected Russian)" }
    }
    elseif ($result.ExitCode -eq 1 -and $result.Err -match '^Error:') {
        # The public endpoint throttles shared CI IPs (HTTP 429) and networks can be flaky.
        Add-Warning "--test-translate: service/network error: $($result.Err)"
    }
    else {
        Add-Failure "--test-translate failed (exit $($result.ExitCode), timed out: $($result.TimedOut)): $($result.Err)"
    }
    [void](Test-LogSince $logStart '--test-translate')
}

function Test-Offline($dotnet) {
    Write-Step "Offline engine (ONNX Runtime) on $Runtime"
    Assert-Architecture $AppExe
    Assert-Architecture (Join-Path $PublishDir 'onnxruntime.dll')
    if (-not ($SkipPublish -and (Test-Path (Join-Path $CliDir 'OfflineCli.exe')))) {
        & $dotnet publish (Join-Path $RepoRoot 'tools\OfflineCli\OfflineCli.csproj') -c Release -r $Runtime --self-contained true `
            -p:PublishSingleFile=false -o $CliDir
        if ($LASTEXITCODE -ne 0) { throw "OfflineCli publish failed with exit code $LASTEXITCODE" }
    }
    $cli = Join-Path $CliDir 'OfflineCli.exe'
    Assert-Architecture $cli

    $watch = [Diagnostics.Stopwatch]::StartNew()
    & $cli download ru
    if ($LASTEXITCODE -ne 0) { Add-Failure "OfflineCli download ru failed with exit code $LASTEXITCODE"; return }
    Add-Note "Downloaded 'ru' in $([int]$watch.Elapsed.TotalSeconds) s"

    $logStart = Get-LogCount
    $text = [regex]::Unescape('Привет, как дела?')
    $watch.Restart()
    $result = Invoke-Captured $AppExe @('--test-offline', $text, 'en') 300
    if ($result.ExitCode -eq 0 -and $result.Out -and $result.Out -notmatch '\p{IsCyrillic}') {
        Add-Note "--test-offline ru->en -> '$($result.Out)' in $([int]$watch.Elapsed.TotalMilliseconds) ms (incl. model load)"
    }
    else {
        Add-Failure "--test-offline failed (exit $($result.ExitCode), timed out: $($result.TimedOut)): out='$($result.Out)' err='$($result.Err)'"
    }
    [void](Test-LogSince $logStart '--test-offline')
}

function Find-Iscc {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    foreach ($path in @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $path) { return $path }
    }
    return $null
}

function Get-RunValue { return (Get-ItemProperty -Path $RunKey -ErrorAction SilentlyContinue).Translator }

function Test-Installer {
    Write-Step 'Installer (silent install, start, silent uninstall)'
    if (Test-Path (Join-Path $InstallDir 'Translator.exe')) { throw "Translator is already installed in $InstallDir; refusing to touch it" }
    $iscc = Find-Iscc
    if (-not $iscc -and (Get-Command choco -ErrorAction SilentlyContinue)) {
        choco install innosetup -y --no-progress | Out-Host
        $iscc = Find-Iscc
    }
    if (-not $iscc) { Add-Failure 'Inno Setup (ISCC.exe) not found'; return }

    $props = [xml](Get-Content (Join-Path $RepoRoot 'Directory.Build.props') -Raw)
    $version = @($props.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    $arch = if ($Runtime -eq 'win-arm64') { 'arm64' } else { 'x64' }
    & $iscc /Q "/DAppVersion=$version" "/DAppArch=$arch" "/DRid=$Runtime" "/DSourceDir=$PublishDir" "/DOutputDir=$InstallerDir" (Join-Path $RepoRoot 'installer\Translator.iss')
    if ($LASTEXITCODE -ne 0) { Add-Failure "ISCC failed with exit code $LASTEXITCODE"; return }
    $setup = Join-Path $InstallerDir "TranslatorSetup-$version-$Runtime.exe"

    $setupLog = Join-Path $OutputDir 'installer-setup.log'
    $install = Start-Process -FilePath $setup -ArgumentList "/VERYSILENT /CURRENTUSER /SUPPRESSMSGBOXES /NORESTART /TASKS=autostart /LOG=`"$setupLog`"" -PassThru -Wait
    $installedExe = Join-Path $InstallDir 'Translator.exe'
    if ($install.ExitCode -ne 0 -or -not (Test-Path $installedExe)) { Add-Failure "Silent install failed (exit $($install.ExitCode))"; return }
    Add-Note "Installed to $InstallDir"
    if (Get-RunValue) { Add-Note "HKCU Run value after install: $(Get-RunValue)" } else { Add-Failure 'HKCU Run value missing after installing with the autostart task' }

    Write-Settings 'CalmGlass'
    $app = Start-App $installedExe 'installed'
    if ($app) {
        $open = Start-Process -FilePath $installedExe -ArgumentList '--open', 'panel' -PassThru -Wait
        if ($open.ExitCode -ne 0) { Add-Failure "installed: --open panel exited with code $($open.ExitCode)" }
        if ((Wait-VisibleWindows $app.Process 15).Count -eq 0) { Add-Failure 'installed: no visible panel' }
        Start-Sleep -Milliseconds 1500
        Save-Screenshot 'installed-panel' 'installed'
        if ($app.Process.HasExited) { Add-Failure "installed: app exited (code $($app.Process.ExitCode))" }
    }

    # Uninstall while the app runs: the uninstaller must ask it to quit (--quit) before removing files.
    $uninstaller = Join-Path $InstallDir 'unins000.exe'
    $uninstallLog = Join-Path $OutputDir 'installer-uninstall.log'
    $uninstall = Start-Process -FilePath $uninstaller -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$uninstallLog`"" -PassThru -Wait
    # The first uninstaller phase relaunches itself from %TEMP%; wait for the real work to finish.
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while ((Test-Path $uninstaller) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
    if (Test-Path $uninstaller) { Add-Failure "Uninstall did not finish within 120 s (exit $($uninstall.ExitCode))" }

    if ($app) {
        if (-not $app.Process.WaitForExit(10000)) { Add-Failure 'installed: app still running after uninstall'; $app.Process.Kill($true) }
        $lines = Test-LogSince $app.LogStart 'installed'
        if ($lines -match 'TrayIcon: removed') { Add-Note 'Uninstaller quit the running app cleanly (--quit)' }
        else { Add-Failure 'installed: the app was not quit cleanly by the uninstaller (no "TrayIcon: removed")' }
    }
    if (Get-RunValue) { Add-Failure 'HKCU Run value still present after uninstall' } else { Add-Note 'HKCU Run value removed by uninstall' }
    if (Test-Path $installedExe) { Add-Failure "Translator.exe still present in $InstallDir after uninstall" }
}

function Write-Summary {
    $lines = [Collections.Generic.List[string]]::new()
    $status = if ($Failures.Count -eq 0) { 'passed' } else { 'FAILED' }
    $lines.Add("## UI smoke $status - $Runtime on $env:COMPUTERNAME")
    $lines.Add('')
    if ($Failures.Count -gt 0) { $lines.Add("### Failures ($($Failures.Count))"); $Failures | ForEach-Object { $lines.Add("- $($_ -replace "`n", "`n  ")") }; $lines.Add('') }
    if ($Warnings.Count -gt 0) { $lines.Add("### Warnings ($($Warnings.Count))"); $Warnings | ForEach-Object { $lines.Add("- $_") }; $lines.Add('') }
    $lines.Add('### Details')
    $Notes | ForEach-Object { $lines.Add("- $_") }
    Set-Content -Path (Join-Path $OutputDir 'summary.md') -Value $lines -Encoding utf8NoBOM
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $lines -Encoding utf8NoBOM }
}

if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutputDir, $DataDir | Out-Null
$env:TRANSLATOR_DATA_DIR = $DataDir

try {
    Test-Environment
    Set-Content -Path (Join-Path $OutputDir 'environment.txt') -Value $Notes -Encoding utf8NoBOM
    $dotnet = Find-Dotnet
    Publish-App $dotnet
    Test-Windows
    Test-OnlineTranslation
    if (-not $SkipOffline) { Test-Offline $dotnet }
    if ($Installer) { Test-Installer }
}
catch {
    Add-Failure "Smoke script error: $($_.Exception.Message)`n$($_.ScriptStackTrace)"
}
finally {
    Get-Process -Name Translator -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and ($_.Path.StartsWith($PublishDir, 'OrdinalIgnoreCase') -or $_.Path.StartsWith($InstallDir, 'OrdinalIgnoreCase')) } |
        ForEach-Object { Add-Failure "Leftover Translator process $($_.Id) from $($_.Path)"; $_.Kill($true) }
    Get-ChildItem (Join-Path $DataDir 'logs') -Filter 'debug.log*' -ErrorAction SilentlyContinue | Copy-Item -Destination $OutputDir
    Write-Summary
    Remove-Item $DataDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Failures: $($Failures.Count), warnings: $($Warnings.Count). Output: $OutputDir"
exit ([int]($Failures.Count -gt 0))

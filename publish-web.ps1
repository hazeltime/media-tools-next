<#
.SYNOPSIS
    Publish the Media Tools Next Blazor Server web app and (optionally) run it.

.DESCRIPTION
    Publishes the web app to a self-contained output directory,
    creates a thin MediaToolsNext.Web.exe launcher at the repository root,
    and optionally starts the server after publishing.

    By default this mirrors the style of publish-root-exe.ps1.

.PARAMETER Port
    HTTP port to listen on when -Run is specified. Defaults to 5069.

.PARAMETER HttpsPort
    HTTPS port to listen on when -Run is specified. Pass 0 to disable HTTPS.
    Defaults to 7222.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER Runtime
    RID for the published output. Defaults to win-x64.

.PARAMETER SelfContained
    Produce a self-contained publish (bundles the .NET runtime). Off by default.

.PARAMETER SkipRestore
    Pass --no-restore to dotnet publish.

.PARAMETER Run
    Start the published server immediately after publishing.

.PARAMETER NoBrowser
    When -Run is specified, suppress automatic browser launch.

.EXAMPLE
    .\publish-web.ps1
    .\publish-web.ps1 -Port 8443 -Run
    .\publish-web.ps1 -Configuration Debug -Run -NoBrowser
    .\publish-web.ps1 -SelfContained -Runtime win-x86
#>
param(
    [int]$Port      = 5069,
    [int]$HttpsPort = 7222,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$Runtime = "win-x64",

    [switch]$SelfContained,
    [switch]$SkipRestore,
    [switch]$Run,
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"

$RepoRoot      = Split-Path -Parent $MyInvocation.MyCommand.Path
$WebProject    = Join-Path $RepoRoot "src\MediaToolsNext.Web\MediaToolsNext.Web.csproj"
$OutputDir     = Join-Path $RepoRoot "publish-web"
$LauncherDir   = Join-Path $OutputDir ".launcher"
$RootExe       = Join-Path $RepoRoot "MediaToolsNext.Web.exe"
$StartedAt     = Get-Date

# ── helpers (same style as publish-root-exe.ps1) ───────────────────────────
function Write-Section {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Detail {
    param([string]$Message)
    Write-Host "    $Message" -ForegroundColor DarkGray
}

function Write-Success {
    param([string]$Message)
    Write-Host "  OK $Message" -ForegroundColor Green
}

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )
    Write-Section $Name
    $stepStarted = Get-Date
    & $Action
    $elapsed = (Get-Date) - $stepStarted
    Write-Success "$Name completed in $($elapsed.ToString('mm\:ss'))."
}

function Invoke-DotNet {
    param([string[]]$Arguments)
    Write-Detail "dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE."
    }
}

function Remove-DirectoryIfExists {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path) {
        Write-Detail "Removing $Path"
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Remove-FileIfExists {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path) {
        Write-Detail "Removing $Path"
        Remove-Item -LiteralPath $Path -Force
    }
}

function Stop-WebServerProcesses {
    param([string]$AppRoot, [string]$LauncherPath)

    if (-not (Test-Path -LiteralPath $AppRoot) -and -not (Test-Path -LiteralPath $LauncherPath)) {
        Write-Detail "No previous web publish output found."
        return
    }

    $normalizedRoot     = [System.IO.Path]::GetFullPath($AppRoot).TrimEnd('\') + '\'
    $normalizedLauncher = [System.IO.Path]::GetFullPath($LauncherPath)

    $processes = Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try {
            if ([string]::IsNullOrWhiteSpace($_.Path)) { return $false }
            $path = [System.IO.Path]::GetFullPath($_.Path)
            return $path.StartsWith($normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase) `
                -or [System.StringComparer]::OrdinalIgnoreCase.Equals($path, $normalizedLauncher)
        }
        catch { return $false }
    }

    if (-not $processes) {
        Write-Detail "No running published web server processes found."
        return
    }

    foreach ($process in $processes) {
        Write-Detail "Stopping $($process.ProcessName) ($($process.Id))"
        try {
            if ($process.MainWindowHandle -ne 0) {
                [void]$process.CloseMainWindow()
                if ($process.WaitForExit(3000)) { continue }
            }
            if (-not (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) { continue }
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
        }
        catch {
            Write-Warning "Could not stop $($process.ProcessName) ($($process.Id)): $($_.Exception.Message)"
        }
    }
}

# ── URL list ────────────────────────────────────────────────────────────────
$urls = @("http://localhost:$Port")
if ($HttpsPort -gt 0) {
    $urls += "https://localhost:$HttpsPort"
}
$urlString = $urls -join ";"

# ── banner ───────────────────────────────────────────────────────────────────
Write-Host "Media Tools Next  –  Web publisher" -ForegroundColor White
Write-Detail "Repository:    $RepoRoot"
Write-Detail "Configuration: $Configuration"
Write-Detail "Runtime:       $Runtime"
Write-Detail "SelfContained: $($SelfContained.IsPresent)"
Write-Detail "Output:        $OutputDir"
if ($Run) { Write-Detail "Listening on:  $urlString" }

# ── step 1: clean up ────────────────────────────────────────────────────────
Invoke-Step "Prepare web publish output" {
    Stop-WebServerProcesses -AppRoot $OutputDir -LauncherPath $RootExe
    Remove-DirectoryIfExists -Path $OutputDir
    Remove-FileIfExists -Path $RootExe
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# ── step 2: publish ──────────────────────────────────────────────────────────
Invoke-Step "Publish Blazor Server web app ($Runtime)" {
    $publishArgs = @(
        "publish", $WebProject,
        "-c", $Configuration,
        "-r", $Runtime,
        "--self-contained", $SelfContained.ToString().ToLowerInvariant(),
        "-p:DebugType=None",
        "-p:DebugSymbols=false",
        "-o", $OutputDir
    )
    if ($SkipRestore) { $publishArgs += "--no-restore" }
    Invoke-DotNet $publishArgs

    $serverExe = Join-Path $OutputDir "MediaToolsNext.Web.exe"
    if (-not (Test-Path -LiteralPath $serverExe)) {
        throw "Published executable was not found at: $serverExe"
    }
    Write-Detail "Published to $OutputDir"
}

# ── step 3: root-level launcher exe ─────────────────────────────────────────
Invoke-Step "Create repository-root web launcher" {
    New-Item -ItemType Directory -Path $LauncherDir -Force | Out-Null
    Push-Location $LauncherDir
    try {
        Invoke-DotNet @("new", "console", "--force")

        # The launcher exe reads ASPNETCORE_URLS from its own env so the port
        # baked in at publish time can be overridden simply by setting the env
        # variable before launching, or by passing --urls as an argument.
        @"
using System.Diagnostics;

var root    = AppContext.BaseDirectory;
var app     = Path.Combine(root, "publish-web", "MediaToolsNext.Web.exe");
var appDir  = Path.GetDirectoryName(app)!;

if (!File.Exists(app))
{
    Console.Error.WriteLine("Missing web server: " + app);
    Console.Error.WriteLine("Run publish-web.ps1 again from the repository root.");
    return 1;
}

// Forward any command-line arguments (e.g. --urls http://localhost:8080)
var psi = new ProcessStartInfo
{
    FileName         = app,
    WorkingDirectory = appDir,
    UseShellExecute  = false,
};

// Inherit caller's environment so ASPNETCORE_URLS / ASPNETCORE_HTTPS_PORT etc.
// flow through automatically.
foreach (var arg in args)
    psi.ArgumentList.Add(arg);

var proc = Process.Start(psi)!;
proc.WaitForExit();
return proc.ExitCode;
"@ | Set-Content -LiteralPath "Program.cs" -Encoding utf8

        Invoke-DotNet @(
            "publish", ".",
            "-c", "Release",
            "-r", $Runtime,
            "--self-contained", "false",
            "-p:PublishSingleFile=true",
            "-p:DebugType=None",
            "-p:DebugSymbols=false",
            "-o", (Join-Path $LauncherDir "out")
        )

        $builtLauncher = Join-Path $LauncherDir "out\.launcher.exe"
        Copy-Item -LiteralPath $builtLauncher -Destination $RootExe -Force
        Write-Detail "Created $RootExe"
    }
    finally {
        Pop-Location
    }
}

# ── summary ──────────────────────────────────────────────────────────────────
$elapsed = (Get-Date) - $StartedAt
Write-Section "Publish summary"
Write-Host "  Web app" -ForegroundColor Green
Write-Host "    Files:   $OutputDir"
Write-Host "    Launcher: $RootExe"
Write-Host ""
Write-Host "  Usage:" -ForegroundColor Yellow
Write-Host "    .\MediaToolsNext.Web.exe                              # default ports ($urlString)"
Write-Host "    .\MediaToolsNext.Web.exe --urls http://localhost:8080 # custom HTTP port"
Write-Host "    `$env:ASPNETCORE_URLS='http://localhost:9000'; .\MediaToolsNext.Web.exe"
Write-Success "Finished in $($elapsed.ToString('mm\:ss'))."

# ── optional: run immediately ─────────────────────────────────────────────
if ($Run) {
    Write-Section "Starting web server"
    Write-Detail "Listening on: $urlString"

    if (-not $NoBrowser) {
        $browserUrl = $urls[0]
        $null = Start-Job -ScriptBlock {
            param($u)
            Start-Sleep -Seconds 2
            Start-Process $u
        } -ArgumentList $browserUrl
    }

    $serverExe = Join-Path $OutputDir "MediaToolsNext.Web.exe"
    & $serverExe --urls $urlString
}

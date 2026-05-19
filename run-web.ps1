<#
.SYNOPSIS
    Run the Media Tools Next web app in development mode.

.DESCRIPTION
    Starts the Blazor Server web app, automatically finding a free port if the
    requested one is busy.  Supports hot-reload / watch mode so code and Razor
    changes are applied without restarting.

    Modes
    -----
    Default (dotnet run)
        Fast startup.  Code changes require a manual Ctrl+C + re-run.

    -Watch (dotnet watch)
        Full hot-reload.  C# code changes are recompiled and the app is
        restarted automatically.  Razor / CSS changes are pushed to the browser
        instantly without a restart.  This is the recommended mode for active
        development.

    -HotReload (dotnet run --hot-reload)
        Lighter variant – Razor / CSS changes refresh the browser without a full
        restart, but significant C# changes still require a restart.

.PARAMETER Port
    Preferred HTTP port.  If the port is busy the script scans upward until a
    free one is found (up to -PortSearchRange attempts).  Defaults to 5069.

.PARAMETER HttpsPort
    Preferred HTTPS port.  Same auto-scan logic applies.  Pass 0 to run HTTP
    only.  Defaults to 7222.

.PARAMETER PortSearchRange
    How many ports to try above the preferred value before giving up.
    Defaults to 20.

.PARAMETER Configuration
    Build configuration.  Defaults to Debug.

.PARAMETER Watch
    Use `dotnet watch` for full hot-reload.  Both C# and Razor changes are
    picked up automatically.  Recommended for active development.

.PARAMETER HotReload
    Use `dotnet run --hot-reload` – a lighter alternative to -Watch that
    supports Razor / CSS live-push but may require a manual restart for
    significant C# changes.  Ignored if -Watch is also specified.

.PARAMETER NoBrowser
    Suppress automatic browser launch.

.PARAMETER SkipRestore
    Pass --no-restore so the NuGet restore step is skipped.  Saves a few
    seconds if packages are already up-to-date.

.PARAMETER Verbose
    Show extra diagnostic output.

.EXAMPLE
    # Quickest start – default ports, auto-finds free port, opens browser
    .\run-web.ps1

    # Full hot-reload (recommended for development)
    .\run-web.ps1 -Watch

    # Lighter live-reload (Razor/CSS only, faster startup)
    .\run-web.ps1 -HotReload

    # Custom port
    .\run-web.ps1 -Port 8080

    # HTTP only, custom port, full hot-reload
    .\run-web.ps1 -Port 8080 -HttpsPort 0 -Watch

    # Headless CI / Release sanity check
    .\run-web.ps1 -Configuration Release -NoBrowser -SkipRestore
#>
param(
    [int]    $Port            = 5069,
    [int]    $HttpsPort       = 7222,
    [int]    $PortSearchRange = 20,

    [ValidateSet("Debug", "Release")]
    [string] $Configuration   = "Debug",

    [switch] $Watch,
    [switch] $HotReload,
    [switch] $NoBrowser,
    [switch] $SkipRestore,
    [switch] $Verbose
)

$ErrorActionPreference = "Stop"

$RepoRoot   = Split-Path -Parent $MyInvocation.MyCommand.Path
$WebProject = Join-Path $RepoRoot "src\MediaToolsNext.Web\MediaToolsNext.Web.csproj"

# ── helpers ──────────────────────────────────────────────────────────────────
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

function Write-Warn {
    param([string]$Message)
    Write-Host "  !! $Message" -ForegroundColor Yellow
}

# ── port availability check ──────────────────────────────────────────────────
function Test-PortFree {
    param([int]$TestPort)
    $listener = $null
    try {
        $listener = [System.Net.Sockets.TcpListener]::new(
            [System.Net.IPAddress]::Loopback, $TestPort)
        $listener.Start()
        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($listener) { $listener.Stop() }
    }
}

function Find-FreePort {
    param([int]$Preferred, [int]$Range, [string]$Label)

    if (Test-PortFree -TestPort $Preferred) {
        return $Preferred
    }

    Write-Warn "$Label port $Preferred is busy – scanning for a free port..."
    for ($p = $Preferred + 1; $p -le ($Preferred + $Range); $p++) {
        if (Test-PortFree -TestPort $p) {
            Write-Warn "  Using $Label port $p instead."
            return $p
        }
    }
    throw "No free $Label port found in range $Preferred–$($Preferred + $Range). Pass a different -$(if ($Label -eq 'HTTP') {'Port'} else {'HttpsPort'}) value."
}

# ── save current working directory ───────────────────────────────────────────
$env:INIT_CWD = $PWD.Path

# ── resolve free ports ────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Media Tools Next  –  Web runner" -ForegroundColor White

$resolvedHttp = Find-FreePort -Preferred $Port -Range $PortSearchRange -Label "HTTP"

$resolvedHttps = 0
if ($HttpsPort -gt 0) {
    $resolvedHttps = Find-FreePort -Preferred $HttpsPort -Range $PortSearchRange -Label "HTTPS"
}

# ── build URL string ─────────────────────────────────────────────────────────
$urls = @("http://localhost:$resolvedHttp")
if ($resolvedHttps -gt 0) {
    $urls += "https://localhost:$resolvedHttps"
}
$urlString  = $urls -join ";"
$browserUrl = $urls[0]    # always open HTTP for local dev

# ── determine run mode ───────────────────────────────────────────────────────
$modeLabel = "dotnet run"
if ($Watch)      { $modeLabel = "dotnet watch (full hot-reload)" }
elseif ($HotReload) { $modeLabel = "dotnet run --hot-reload (Razor/CSS live-push)" }

# ── banner ───────────────────────────────────────────────────────────────────
Write-Detail "Project:       $WebProject"
Write-Detail "Configuration: $Configuration"
Write-Detail "Mode:          $modeLabel"
Write-Detail "Listening on:  $urlString"
if ($NoBrowser)    { Write-Detail "Browser:       suppressed" }
if ($SkipRestore)  { Write-Detail "Restore:       skipped" }

# ── browser launch (background job, fires after 2 s) ─────────────────────────
if (-not $NoBrowser) {
    $null = Start-Job -ScriptBlock {
        param($u)
        Start-Sleep -Seconds 2
        Start-Process $u
    } -ArgumentList $browserUrl
}

# ── build dotnet argument list ────────────────────────────────────────────────
if ($Watch) {
    # dotnet watch run [options] -- [app-args]
    # Note: --urls is passed after -- so it reaches the app, not dotnet watch.
    $runArgs = @(
        "watch",
        "--project", $WebProject,
        "-c", $Configuration
    )
    if ($SkipRestore)  { $runArgs += "--no-restore" }
    if ($Verbose)      { $runArgs += "--verbose" }
    # Pass app arguments after --
    $runArgs += @("--", "--urls", $urlString)
}
elseif ($HotReload) {
    # dotnet run --hot-reload [options]
    $runArgs = @(
        "run",
        "--project",  $WebProject,
        "-c",         $Configuration,
        "--hot-reload",
        "--urls",     $urlString
    )
    if ($SkipRestore) { $runArgs += "--no-restore" }
}
else {
    # Plain dotnet run
    $runArgs = @(
        "run",
        "--project", $WebProject,
        "-c",        $Configuration,
        "--urls",    $urlString
    )
    if ($SkipRestore) { $runArgs += "--no-restore" }
}

# ── run ───────────────────────────────────────────────────────────────────────
Write-Section "Starting web app  (Ctrl+C to stop)"
Write-Detail "dotnet $($runArgs -join ' ')"
Write-Host ""

& dotnet @runArgs

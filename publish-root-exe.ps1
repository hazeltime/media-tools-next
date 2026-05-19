param(
    [ValidateSet("WindowsDesktop", "LinuxCli", "All")]
    [string]$Target = "WindowsDesktop",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [string]$LinuxRuntime = "linux-x64",

    [switch]$SelfContained,

    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$DesktopProject = Join-Path $RepoRoot "src\MediaToolsNext.Desktop\MediaToolsNext.Desktop.csproj"
$CliProject = Join-Path $RepoRoot "src\MediaToolsNext.Cli\MediaToolsNext.Cli.csproj"
$DesktopFramework = "net10.0-windows10.0.19041.0"
$WindowsRuntime = "win-x64"
$DesktopBuildDir = Join-Path $RepoRoot "src\MediaToolsNext.Desktop\bin\$Configuration\$DesktopFramework\$WindowsRuntime"
$WindowsOutputDir = Join-Path $RepoRoot "publish-root"
$LinuxOutputDir = Join-Path $RepoRoot "publish-linux-cli\$LinuxRuntime"
$LauncherDir = Join-Path $WindowsOutputDir ".launcher"
$RootExe = Join-Path $RepoRoot "MediaToolsNext.exe"
$StartedAt = Get-Date

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

function Stop-PublishedAppProcesses {
    param(
        [string]$AppRoot,
        [string]$LauncherPath
    )

    if (-not (Test-Path -LiteralPath $AppRoot) -and -not (Test-Path -LiteralPath $LauncherPath)) {
        Write-Detail "No previous Windows publish output found."
        return
    }

    $normalizedAppRoot = [System.IO.Path]::GetFullPath($AppRoot).TrimEnd('\') + '\'
    $normalizedLauncher = [System.IO.Path]::GetFullPath($LauncherPath)

    $processes = Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try {
            if ([string]::IsNullOrWhiteSpace($_.Path)) { return $false }
            $path = [System.IO.Path]::GetFullPath($_.Path)
            return $path.StartsWith($normalizedAppRoot, [System.StringComparison]::OrdinalIgnoreCase) `
                -or [System.StringComparer]::OrdinalIgnoreCase.Equals($path, $normalizedLauncher)
        }
        catch {
            return $false
        }
    }

    if (-not $processes) {
        Write-Detail "No running published app processes found."
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
            Write-Warning "Could not stop running published process $($process.ProcessName) ($($process.Id)): $($_.Exception.Message)"
        }
    }
}

function Publish-WindowsDesktop {
    Invoke-Step "Prepare Windows desktop output" {
        Stop-PublishedAppProcesses -AppRoot $WindowsOutputDir -LauncherPath $RootExe
        Remove-DirectoryIfExists -Path $WindowsOutputDir
        Remove-FileIfExists -Path $RootExe
        New-Item -ItemType Directory -Path $WindowsOutputDir -Force | Out-Null
    }

    Invoke-Step "Build Windows MAUI desktop app" {
        $args = @("build", $DesktopProject, "-f", $DesktopFramework, "-c", $Configuration)
        if ($SkipRestore) { $args += "--no-restore" }
        Invoke-DotNet $args
    }

    Invoke-Step "Copy Windows desktop app files" {
        $desktopExe = Join-Path $DesktopBuildDir "MediaToolsNext.Desktop.exe"
        if (-not (Test-Path -LiteralPath $desktopExe)) {
            throw "Build executable was not found: $desktopExe"
        }

        Copy-Item -Path (Join-Path $DesktopBuildDir "*") -Destination $WindowsOutputDir -Recurse -Force
        Write-Detail "Copied app payload to $WindowsOutputDir"
    }

    Invoke-Step "Create repository-root Windows launcher" {
        New-Item -ItemType Directory -Path $LauncherDir -Force | Out-Null
        Push-Location $LauncherDir
        try {
            Invoke-DotNet @("new", "console", "--force")
            @'
using System.Diagnostics;

var root = AppContext.BaseDirectory;
var app = Path.Combine(root, "publish-root", "MediaToolsNext.Desktop.exe");
if (!File.Exists(app))
{
    Console.Error.WriteLine("Missing app executable: " + app);
    Console.Error.WriteLine("Run publish-root-exe.ps1 again from the repository root.");
    return 1;
}

Process.Start(new ProcessStartInfo
{
    FileName = app,
    WorkingDirectory = Path.GetDirectoryName(app)!,
    UseShellExecute = true
});
return 0;
'@ | Set-Content -LiteralPath "Program.cs" -Encoding utf8

            Invoke-DotNet @(
                "publish", ".",
                "-c", "Release",
                "-r", $WindowsRuntime,
                "--self-contained", "false",
                "-p:PublishSingleFile=true",
                "-p:DebugType=None",
                "-p:DebugSymbols=false",
                "-o", (Join-Path $LauncherDir "out")
            )
            Copy-Item -LiteralPath (Join-Path $LauncherDir "out\.launcher.exe") -Destination $RootExe -Force
            Write-Detail "Created $RootExe"
        }
        finally {
            Pop-Location
        }
    }

    [pscustomobject]@{
        Target = "Windows desktop"
        Path = $WindowsOutputDir
        Launcher = $RootExe
        Run = ".\MediaToolsNext.exe"
    }
}

function Publish-LinuxCli {
    Invoke-Step "Prepare Linux CLI output" {
        Remove-DirectoryIfExists -Path $LinuxOutputDir
        New-Item -ItemType Directory -Path $LinuxOutputDir -Force | Out-Null
    }

    Invoke-Step "Publish Linux CLI ($LinuxRuntime)" {
        $args = @(
            "publish", $CliProject,
            "-c", $Configuration,
            "-r", $LinuxRuntime,
            "--self-contained", $SelfContained.ToString().ToLowerInvariant(),
            "-p:PublishSingleFile=true",
            "-p:DebugType=None",
            "-p:DebugSymbols=false",
            "-o", $LinuxOutputDir
        )
        if ($SkipRestore) { $args += "--no-restore" }
        Invoke-DotNet $args

        $linuxExecutable = Join-Path $LinuxOutputDir "MediaToolsNext.Cli"
        if (-not (Test-Path -LiteralPath $linuxExecutable)) {
            throw "Linux CLI executable was not found: $linuxExecutable"
        }
        Write-Detail "Created $linuxExecutable"
    }

    [pscustomobject]@{
        Target = "Ubuntu/Linux CLI"
        Path = $LinuxOutputDir
        Launcher = $null
        Run = "./MediaToolsNext.Cli <source> <target>"
    }
}

Write-Host "Media Tools Next publisher" -ForegroundColor White
Write-Detail "Repository: $RepoRoot"
Write-Detail "Target: $Target"
Write-Detail "Configuration: $Configuration"
if ($Target -in @("LinuxCli", "All")) {
    Write-Detail "Linux runtime: $LinuxRuntime"
    Write-Detail "Linux self-contained: $($SelfContained.IsPresent)"
}

$results = @()
if ($Target -in @("WindowsDesktop", "All")) {
    $results += ,(Publish-WindowsDesktop)
}
if ($Target -in @("LinuxCli", "All")) {
    $results += ,(Publish-LinuxCli)
}

$elapsedTotal = (Get-Date) - $StartedAt
Write-Section "Publish summary"
foreach ($result in $results) {
    Write-Host "  $($result.Target)" -ForegroundColor Green
    Write-Host "    Files: $($result.Path)"
    if ($result.Launcher) { Write-Host "    Launcher: $($result.Launcher)" }
    Write-Host "    Run: $($result.Run)"
}
Write-Success "Finished in $($elapsedTotal.ToString('mm\:ss'))."

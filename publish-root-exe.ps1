param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $RepoRoot "src\MediaToolsNext.Desktop\MediaToolsNext.Desktop.csproj"
$BuildDir = Join-Path $RepoRoot "src\MediaToolsNext.Desktop\bin\$Configuration\net10.0-windows10.0.19041.0\win-x64"
$OutputDir = Join-Path $RepoRoot "publish-root"
$LauncherDir = Join-Path $OutputDir ".launcher"
$RootExe = Join-Path $RepoRoot "MediaToolsNext.exe"

Remove-Item -LiteralPath $OutputDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $RootExe -Force -ErrorAction SilentlyContinue

dotnet build $Project -f net10.0-windows10.0.19041.0 -c $Configuration

if (-not (Test-Path -LiteralPath (Join-Path $BuildDir "MediaToolsNext.Desktop.exe"))) {
    throw "Build executable was not found in $BuildDir"
}

New-Item -ItemType Directory -Path $OutputDir | Out-Null
Copy-Item -Path (Join-Path $BuildDir "*") -Destination $OutputDir -Recurse -Force

New-Item -ItemType Directory -Path $LauncherDir | Out-Null
Push-Location $LauncherDir
try {
    dotnet new console --force | Out-Null
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
'@ | Set-Content -LiteralPath "Program.cs"

    dotnet publish . -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $LauncherDir "out") | Out-Null
    Copy-Item -LiteralPath (Join-Path $LauncherDir "out\.launcher.exe") -Destination $RootExe -Force
}
finally {
    Pop-Location
}

Write-Host "Created launcher: $RootExe"
Write-Host "Created app files: $OutputDir"

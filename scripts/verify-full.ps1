param(
    [switch]$Restore
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$noRestore = if ($Restore) { @() } else { @("--no-restore") }

function Invoke-Step {
    param(
        [string]$Name,
        [string[]]$Command
    )

    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
    Write-Host ($Command -join " ") -ForegroundColor DarkGray
    & $Command[0] @($Command | Select-Object -Skip 1)
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

Set-Location $repoRoot

Invoke-Step "Main tests" (@("dotnet", "test", "tests\MediaToolsNext.Tests\MediaToolsNext.Tests.csproj") + $noRestore)
Invoke-Step "Desktop tests" (@("dotnet", "test", "tests\MediaToolsNext.Desktop.Tests\MediaToolsNext.Desktop.Tests.csproj") + $noRestore)
Invoke-Step "Build CLI" (@("dotnet", "build", "src\MediaToolsNext.Cli\MediaToolsNext.Cli.csproj") + $noRestore)
Invoke-Step "Build infrastructure" (@("dotnet", "build", "src\MediaToolsNext.Infrastructure\MediaToolsNext.Infrastructure.csproj") + $noRestore)
Invoke-Step "Build Windows desktop" (@("dotnet", "build", "src\MediaToolsNext.Desktop\MediaToolsNext.Desktop.csproj", "-f", "net10.0-windows10.0.19041.0") + $noRestore)
Invoke-Step "Diff whitespace check" @("git", "diff", "--check")

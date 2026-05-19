param(
    [ValidateSet("core", "infra", "cli", "desktop", "desktop-tests", "tests")]
    [string]$Area = "tests",

    [string]$Filter = "",

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
}

Set-Location $repoRoot

switch ($Area) {
    "core" {
        $cmd = @("dotnet", "test", "tests\MediaToolsNext.Tests\MediaToolsNext.Tests.csproj") + $noRestore
        if ($Filter) { $cmd += @("--filter", $Filter) }
        Invoke-Step "Core-focused tests" $cmd
    }
    "infra" {
        $cmd = @("dotnet", "test", "tests\MediaToolsNext.Tests\MediaToolsNext.Tests.csproj") + $noRestore
        if ($Filter) { $cmd += @("--filter", $Filter) }
        Invoke-Step "Infrastructure-focused tests" $cmd
    }
    "cli" {
        Invoke-Step "Build CLI" (@("dotnet", "build", "src\MediaToolsNext.Cli\MediaToolsNext.Cli.csproj") + $noRestore)
        if ($Filter) {
            Invoke-Step "CLI-related tests" (@("dotnet", "test", "tests\MediaToolsNext.Tests\MediaToolsNext.Tests.csproj") + $noRestore + @("--filter", $Filter))
        }
    }
    "desktop" {
        Invoke-Step "Build Windows desktop" (@("dotnet", "build", "src\MediaToolsNext.Desktop\MediaToolsNext.Desktop.csproj", "-f", "net10.0-windows10.0.19041.0") + $noRestore)
    }
    "desktop-tests" {
        $cmd = @("dotnet", "test", "tests\MediaToolsNext.Desktop.Tests\MediaToolsNext.Desktop.Tests.csproj") + $noRestore
        if ($Filter) { $cmd += @("--filter", $Filter) }
        Invoke-Step "Desktop tests" $cmd
    }
    "tests" {
        $cmd = @("dotnet", "test", "tests\MediaToolsNext.Tests\MediaToolsNext.Tests.csproj") + $noRestore
        if ($Filter) { $cmd += @("--filter", $Filter) }
        Invoke-Step "Main tests" $cmd
    }
}

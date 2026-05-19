param(
    [string]$ArtifactDir = "C:\Users\behro\.gemini\antigravity\brain\8bd3e721-c42e-43df-86b5-e85d0494f235"
)

$ErrorActionPreference = "Stop"
$WebProj = "src\MediaToolsNext.Web\MediaToolsNext.Web.csproj"

Write-Host "Starting MediaToolsNext.Web on port 5069..."
# Pass --urls to guarantee it starts on the port our script expects
$proc = Start-Process -FilePath "dotnet" -ArgumentList "run --project $WebProj --urls http://localhost:5069" -PassThru -NoNewWindow

try {
    Write-Host "Waiting for the web app to start up (7 seconds)..."
    Start-Sleep -Seconds 7

    Write-Host "Running Playwright visual tests..."
    $env:ARTIFACT_DIR = $ArtifactDir
    Set-Location "tests\MediaToolsNext.VisualTests"
    
    # Run the tests
    npx playwright test visual.spec.ts
    $testExit = $LASTEXITCODE
    
    Set-Location "..\.."

    if ($testExit -ne 0) {
        Write-Error "Playwright tests failed with exit code $testExit."
    } else {
        Write-Host "Playwright tests passed successfully! Screenshots have been saved to $ArtifactDir."
    }
}
finally {
    Write-Host "Stopping web app (Process ID: $($proc.Id))..."
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
}

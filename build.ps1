# Builds everything (mod, API, example plugin) in Release and runs the Core unit tests.
# No game needed.
#   -SkipTests   build only

param([switch]$SkipTests)

$root = $PSScriptRoot

dotnet build (Join-Path $root "Callsign.slnx") -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host "Build FAILED" -ForegroundColor Red; exit 1 }

if (-not $SkipTests) {
    dotnet test (Join-Path $root "tests\Callsign.Core.Tests\Callsign.Core.Tests.csproj") -c Release --no-build -nologo -v q
    if ($LASTEXITCODE -ne 0) { Write-Host "Tests FAILED" -ForegroundColor Red; exit 1 }
}

Write-Host "Build OK" -ForegroundColor Green

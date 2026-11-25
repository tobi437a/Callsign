# Puts the mod files back for Story Mode. Reverse of go-online.ps1.

$game  = "C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V"
$stash = Join-Path (Split-Path $PSScriptRoot -Parent) "_installed"

if (Get-Process -Name GTA5, GTA5_Enhanced, PlayGTAV -ErrorAction SilentlyContinue) {
    Write-Host "GTA V is running. Close it fully first." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $stash)) {
    Write-Host "Nothing stashed at $stash - mods are probably already installed." -ForegroundColor Yellow
    exit 1
}

Get-ChildItem $stash -File | ForEach-Object {
    Move-Item -Path $_.FullName -Destination (Join-Path $game $_.Name) -Force
    Write-Host "restored  $($_.Name)"
}

foreach ($dir in "scripts", "Callsign") {
    $src = Join-Path $stash $dir
    if (Test-Path $src) {
        $dst = Join-Path $game $dir
        if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
        Move-Item -Path $src -Destination $dst -Force
        Write-Host "restored  $dir\"
    }
}

Write-Host ""
Write-Host "Story Mode mods reinstalled. Launch from Steam, choose Story Mode, press F7 to go on duty." -ForegroundColor Green

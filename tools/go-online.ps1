# Moves all mod files out of the GTA V folder so GTA Online is safe to launch.
# Reverse it with go-storymode.ps1 — nothing is deleted.

$game   = "C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V"
$stash  = Join-Path (Split-Path $PSScriptRoot -Parent) "_installed"

if (Get-Process -Name GTA5, GTA5_Enhanced, PlayGTAV -ErrorAction SilentlyContinue) {
    Write-Host "GTA V is running. Close it fully first." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $stash)) { New-Item -ItemType Directory -Path $stash | Out-Null }

$files = @(
    "dinput8.dll",
    "xinput1_4.dll",
    "ScriptHookV.dll",
    "ScriptHookVDotNet.asi",
    "ScriptHookVDotNet.ini",
    "ScriptHookVDotNet2.dll",
    "ScriptHookVDotNet3.dll",
    "args.txt",
    "NativeTrainer.asi"
)

foreach ($f in $files) {
    $src = Join-Path $game $f
    if (Test-Path $src) {
        Move-Item -Path $src -Destination (Join-Path $stash $f) -Force
        Write-Host "moved out  $f"
    }
}

# scripts\ (Callsign.dll + Callsign.API.dll) and Callsign\ (plugins, data, log, saves)
foreach ($dir in "scripts", "Callsign") {
    $src = Join-Path $game $dir
    if (Test-Path $src) {
        $dest = Join-Path $stash $dir
        if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
        Move-Item -Path $src -Destination $dest -Force
        Write-Host "moved out  $dir\"
    }
}

# logs are harmless but leave no trace of a hook
foreach ($log in @("ScriptHookV.log", "ScriptHookVDotNet.log", "asiloader.log")) {
    $p = Join-Path $game $log
    if (Test-Path $p) { Remove-Item $p -Force; Write-Host "deleted    $log" }
}

# leftover .asi / .dll that aren't part of stock GTA V
$stock = @("GFSDK_ShadowLib.win64.dll","GFSDK_TXAA.win64.dll","GFSDK_TXAA_AlphaResolve.win64.dll",
           "GPUPerfAPIDX11-x64.dll","NvPmApi.Core.win64.dll","XCurl.dll","bink2w64.dll",
           "d3dcompiler_46.dll","d3dcsx_46.dll","fvad.dll","libcurl.dll","libtox.dll",
           "opus.dll","opusenc.dll","steam_api64.dll")
$stray = Get-ChildItem $game -File | Where-Object {
    ($_.Extension -in ".asi", ".dll") -and ($stock -notcontains $_.Name)
}
if ($stray) {
    Write-Host ""
    Write-Host "Still present and NOT stock GTA V - check these before going Online:" -ForegroundColor Yellow
    $stray | ForEach-Object { Write-Host "  $($_.Name)" }
} else {
    Write-Host ""
    Write-Host "Game folder is clean. Safe to launch GTA Online." -ForegroundColor Green
}

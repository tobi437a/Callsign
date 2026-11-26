# Builds + tests, then installs into the game:
#   <GTA V>\scripts\     Callsign.dll, Callsign.API.dll   (SHVDN loads these)
#   <GTA V>\Callsign\   Data\ (from data\), Plugins\, Dev\ (dev bridge), Callsign.log
# Works while the game is running: press Insert in-game to reload.
#   -WithExamplePlugin   also install samples\ExamplePlugin into Callsign\Plugins\
#   -NoExamplePlugin     remove it again
#   -GamePath <dir>      GTA V folder (default: Steam's default location)

param(
    [switch]$WithExamplePlugin,
    [switch]$NoExamplePlugin,
    [string]$GamePath = "C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V"
)

$root   = $PSScriptRoot
$game   = $GamePath
$scripts = Join-Path $game "scripts"
$home_  = Join-Path $game "Callsign"
$bin    = Join-Path $root "src\Callsign\bin\Release\net48"

& (Join-Path $root "build.ps1")
if ($LASTEXITCODE -ne 0) { exit 1 }

if (-not (Test-Path (Join-Path $game "ScriptHookVDotNet3.dll"))) {
    Write-Host "SHVDN isn't installed in the game folder (mods moved out for Online?)." -ForegroundColor Yellow
    Write-Host "Run tools\go-storymode.ps1 first." -ForegroundColor Yellow
    exit 1
}

foreach ($f in "Callsign.dll", "Callsign.pdb", "Callsign.API.dll", "Callsign.API.pdb") {
    Copy-Item (Join-Path $bin $f) -Destination $scripts -Force
    Write-Host "deployed   scripts\$f"
}

# Dev\ turns on the dev bridge that tools\game.ps1 drives. Player installs don't have it.
New-Item -ItemType Directory -Force (Join-Path $home_ "Data"), (Join-Path $home_ "Plugins"), (Join-Path $home_ "Dev") | Out-Null
$data = Join-Path $root "data"
if (Get-ChildItem $data -File -Recurse -ErrorAction SilentlyContinue) {
    Copy-Item (Join-Path $data "*") -Destination (Join-Path $home_ "Data") -Recurse -Force
    Write-Host "deployed   data\ -> Callsign\Data\"
}

$example = Join-Path $home_ "Plugins\ExamplePlugin.dll"
if ($WithExamplePlugin) {
    Copy-Item (Join-Path $root "samples\ExamplePlugin\bin\Release\net48\ExamplePlugin.dll") -Destination $example -Force
    Write-Host "deployed   Callsign\Plugins\ExamplePlugin.dll"
}
if ($NoExamplePlugin -and (Test-Path $example)) {
    Remove-Item $example
    Write-Host "removed    Callsign\Plugins\ExamplePlugin.dll"
}

Write-Host "Done. In game, press Insert to reload scripts." -ForegroundColor Green

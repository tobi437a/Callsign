# Drives the running game for automated in-game checks (dev only).
#   game.ps1 reload                 focus GTA V, press Insert, wait until Callsign has loaded
#   game.ps1 cmd "duty on" "state"  run dev-bridge commands in order, print their results
#   game.ps1 run <scenario>         run tools\scenarios\<scenario>.txt (one command per line, # comments);
#                                   exits 1 if any command failed (e.g. an "expect" check)
#   game.ps1 shot [file.png]        screenshot of the game (brings it to the front for a moment)
#   game.ps1 log [lines]            tail Callsign.log
#   game.ps1 focus                  bring the game to the front
#   game.ps1 key Escape             focus the game and tap a key (System.Windows.Forms.Keys name)
# Works in the background (no focus stealing) if the game keeps running unfocused; add -Focus otherwise.
# Commands are read by Callsign's DevBridge, which only runs when <game>\Callsign\Dev\ exists
# (deploy.ps1 creates it). See src\Callsign\Dev\DevBridgeModule.cs for the command list.

param(
    [Parameter(Mandatory = $true, Position = 0)][ValidateSet("reload", "cmd", "run", "shot", "log", "focus", "key")][string]$Action,
    [Parameter(Position = 1, ValueFromRemainingArguments = $true)][string[]]$Rest,
    [string]$GamePath = "C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V",
    [int]$TimeoutSec = 60,
    # Bring the game to the front for reload/cmd/shot. Not needed when the game keeps running in the
    # background (GTA setting "Pause Game On Focus Loss" off); then nothing steals your focus.
    [switch]$Focus
)

$ErrorActionPreference = "Stop"
$home_   = Join-Path $GamePath "Callsign"
$devDir  = Join-Path $home_ "Dev"
$logFile = Join-Path $home_ "Callsign.log"

Add-Type -ReferencedAssemblies System.Drawing @"
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public static class GameWin {
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    const uint EXTENDED = 1, KEYUP = 2;

    public static bool Focus(IntPtr h) {
        if (GetForegroundWindow() == h) return true;
        if (IsIconic(h)) ShowWindow(h, 9);
        // Windows only lets the process that last had input take the foreground: a synthetic Alt counts.
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, KEYUP, UIntPtr.Zero);
        SetForegroundWindow(h);
        return GetForegroundWindow() == h;
    }

    public static bool IsFront(IntPtr h) { return GetForegroundWindow() == h; }

    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

    // A real key press; goes to whatever window is in front.
    public static void Tap(byte vk, bool extended) {
        byte scan = (byte)MapVirtualKey(vk, 0);
        uint ext = extended ? EXTENDED : 0;
        keybd_event(vk, scan, ext, UIntPtr.Zero);
        System.Threading.Thread.Sleep(80);
        keybd_event(vk, scan, ext | KEYUP, UIntPtr.Zero);
    }

    // A key press sent straight to the game's window: ScriptHookV reads window messages, so this
    // reaches SHVDN (e.g. Insert) without the game having focus.
    public static void Post(IntPtr h, byte vk, bool extended) {
        uint scan = MapVirtualKey(vk, 0);
        long down = 1 | ((long)scan << 16) | (extended ? (1L << 24) : 0);
        long up = down | (1L << 30) | (1L << 31);
        PostMessage(h, 0x0100, (IntPtr)vk, (IntPtr)down);   // WM_KEYDOWN
        System.Threading.Thread.Sleep(80);
        PostMessage(h, 0x0101, (IntPtr)vk, (IntPtr)up);     // WM_KEYUP
    }

    public static void Shot(IntPtr h, string path) {
        RECT r; GetWindowRect(h, out r);
        using (var bmp = new Bitmap(r.R - r.L, r.B - r.T))
        using (var g = Graphics.FromImage(bmp)) {
            g.CopyFromScreen(r.L, r.T, 0, 0, bmp.Size);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
"@

function Get-GameWindow {
    $p = Get-Process GTA5 -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $p) { Write-Host "GTA V isn't running." -ForegroundColor Red; exit 2 }
    return $p.MainWindowHandle
}

function Focus-Game($h) {
    for ($i = 0; $i -lt 10; $i++) {
        if ([GameWin]::Focus($h)) { Start-Sleep -Milliseconds 300; return }
        Start-Sleep -Milliseconds 200
    }
    Write-Host "Couldn't bring the game to the front." -ForegroundColor Red; exit 3
}

function Wait-Until([scriptblock]$cond, [string]$what) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (& $cond) { return }
        Start-Sleep -Milliseconds 200
    }
    Write-Host "Timed out waiting for $what." -ForegroundColor Red; exit 4
}

switch ($Action) {
    "focus" { Focus-Game (Get-GameWindow); "focused" }

    "key" {
        Add-Type -AssemblyName System.Windows.Forms
        $vk = [int][System.Windows.Forms.Keys]$Rest[0]
        Focus-Game (Get-GameWindow)
        [GameWin]::Tap([byte]$vk, $vk -in 0x21..0x2E)   # PageUp..Delete and the arrows are extended keys
        "pressed $($Rest[0])"
    }

    "reload" {
        $h = Get-GameWindow
        # FileLog starts a new file on load; its first line has the start time.
        $before = if (Test-Path $logFile) { Get-Content $logFile -TotalCount 1 } else { "" }
        if ($Focus) { Focus-Game $h; [GameWin]::Tap(0x2D, $true) }   # Insert
        else { [GameWin]::Post($h, 0x2D, $true) }
        # The plugin line is the last thing startup logs.
        Wait-Until { (Test-Path $logFile) -and (Get-Content $logFile -TotalCount 1) -ne $before -and
                     (Select-String -Path $logFile -Pattern "plugin\(s\) running" -Quiet) } "Callsign to load"
        Get-Content $logFile
    }

    { $_ -in "cmd", "run" } {
        if ($Action -eq "run") {
            $file = Join-Path $PSScriptRoot "scenarios\$($Rest[0]).txt"
            if (-not (Test-Path $file)) { Write-Host "No scenario $file" -ForegroundColor Red; exit 1 }
            $Rest = @(Get-Content $file | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith("#") })
        }
        if (-not (Test-Path $devDir)) { Write-Host "No $devDir (run deploy.ps1)." -ForegroundColor Red; exit 1 }
        if (-not $Rest) { Write-Host "Give at least one command." -ForegroundColor Red; exit 1 }
        $results = Join-Path $devDir "results.txt"
        $commands = Join-Path $devDir "commands.txt"
        Remove-Item $results -ErrorAction SilentlyContinue
        $batch = [guid]::NewGuid().ToString("N").Substring(0, 8)
        $lines = @()
        for ($i = 0; $i -lt $Rest.Count; $i++) { $lines += "$batch-$i $($Rest[$i])" }
        # Write to a temp name then rename, so the mod never reads half a file.
        $tmp = "$commands.tmp"
        Set-Content -Path $tmp -Value $lines -Encoding UTF8
        Move-Item $tmp $commands -Force
        if ($Focus) { Focus-Game (Get-GameWindow) }
        $last = "$batch-$($Rest.Count - 1) "
        Wait-Until { (Test-Path $results) -and (Select-String -Path $results -SimpleMatch $last -Quiet) } "the commands to finish"
        $out = Get-Content $results
        $out
        if ($out | Where-Object { $_ -match "^\S+ error " }) { Write-Host "FAILED" -ForegroundColor Red; exit 1 }
        Write-Host "OK" -ForegroundColor Green
    }

    "shot" {
        $h = Get-GameWindow
        $path = if ($Rest) { $Rest[0] } else { Join-Path $env:TEMP "callsign-shot.png" }
        # Always in front: a DirectX game window can't be captured from behind (PrintWindow gives a stale frame).
        Focus-Game $h
        [GameWin]::Shot($h, $path)
        $path
    }

    "log" {
        $n = if ($Rest) { [int]$Rest[0] } else { 40 }
        Get-Content $logFile -Tail $n
    }
}

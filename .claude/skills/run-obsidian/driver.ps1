<#
  Drives the Anvil editor (or the standalone Engine) on Windows: launch, find/click UI elements through
  UI Automation, click window-relative pixels, scroll, take window screenshots, quit.

  Run from the repo root with Windows PowerShell 5.1:
    .claude\skills\run-obsidian\driver.ps1 <action> [-Target "..."] [-Out file.png] [-App Anvil|Engine]

  Actions
    launch            start the built exe and wait for its window (Anvil: until the toolbar is up)
    find  -Target re  list named UI elements whose name matches the regex (type | name | screen rect)
    click -Target n   click the centre of the first element named exactly n (screen coordinates via UIA)
    clickxy -Target "x,y"        click at x,y relative to the window = pixel x,y in a `shot` image
    wheel   -Target "x,y,notches" scroll down `notches` at window-relative x,y (negative notches = up)
    key   -Target k     press and release a key, named as in System.Windows.Forms.Keys (Enter, Escape, Space, W, F1...)
    shot  [-Out p]    screenshot of the window (default .claude/skills/run-obsidian/shots/shot.png)
    quit              close the window (force-kill after 5 s)
#>
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet("launch", "find", "click", "clickxy", "wheel", "key", "shot", "quit")]
    [string]$Action,
    [string]$Target = "",
    [string]$Out = "",
    [ValidateSet("Anvil", "Engine")]
    [string]$App = "Anvil"
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$exe = if ($App -eq "Anvil") { Join-Path $repo "Editor\Anvil\bin\Debug\net10.0-windows\Anvil.exe" }
       else { Join-Path $repo "Engine\bin\Debug\net10.0-windows\Engine.exe" }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
if (-not ("ObsidianDriver.Win" -as [type])) {
Add-Type @"
using System; using System.Runtime.InteropServices;
namespace ObsidianDriver {
public static class Win {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, int d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}}
"@
}

function Get-AppProcess {
    Get-Process $App -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
}

function Get-Window {
    $p = Get-AppProcess
    if (-not $p) { throw "$App is not running (or has no window yet). Run: driver.ps1 launch -App $App" }
    [ObsidianDriver.Win]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 250
    $p.MainWindowHandle
}

function Get-Rect($h) {
    $r = New-Object ObsidianDriver.Win+RECT
    [ObsidianDriver.Win]::GetWindowRect($h, [ref]$r) | Out-Null
    $r
}

function Click-Screen([int]$x, [int]$y) {
    [ObsidianDriver.Win]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 100
    [ObsidianDriver.Win]::mouse_event(0x2, 0, 0, 0, [IntPtr]::Zero)   # left down
    [ObsidianDriver.Win]::mouse_event(0x4, 0, 0, 0, [IntPtr]::Zero)   # left up
}

# One element by exact name: a name-filtered UIA query (walking the whole tree takes many seconds).
function Find-Exact($h, [string]$name) {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

# UIA returns many duplicates of every Avalonia element; keep one per (type, name, rect).
function Find-Named($h, [scriptblock]$match) {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $seen = @{}
    foreach ($e in $all) {
        $c = $e.Current
        if (-not $c.Name -or -not (& $match $c.Name)) { continue }
        $key = "$($c.ControlType.ProgrammaticName)|$($c.Name)|$($c.BoundingRectangle)"
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true
        $e
    }
}

switch ($Action) {
    "launch" {
        if (Get-AppProcess) { "$App is already running"; break }
        if (-not (Test-Path $exe)) { throw "Not built: $exe" }
        Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) | Out-Null
        $deadline = (Get-Date).AddSeconds(90)
        while (-not (Get-AppProcess)) {
            if ((Get-Date) -gt $deadline) { throw "$App window did not appear within 90 s" }
            Start-Sleep -Milliseconds 500
        }
        if ($App -eq "Anvil") {
            # The window shows before the embedded engine and the toolbar are ready. Re-read the handle each
            # time: early on the process's main window can be the engine's MonoGame window, before it is
            # reparented into the editor.
            while (-not (Find-Exact (Get-AppProcess).MainWindowHandle "Pipeline")) {
                if ((Get-Date) -gt $deadline) { throw "Anvil toolbar not ready within 90 s" }
                Start-Sleep -Seconds 1
            }
            Start-Sleep -Seconds 3   # let the engine draw its first frames
        }
        "$App running (pid $((Get-AppProcess).Id))"
    }
    "find" {
        $h = Get-Window
        foreach ($e in Find-Named $h { param($n) $n -match $Target }) {
            $c = $e.Current
            "{0} | {1} | {2}" -f $c.ControlType.ProgrammaticName.Replace("ControlType.", ""), $c.Name, $c.BoundingRectangle
        }
    }
    "click" {
        $h = Get-Window
        $e = Find-Exact $h $Target
        if (-not $e) { throw "No element named '$Target'. Try: driver.ps1 find -Target '$Target'" }
        $r = $e.Current.BoundingRectangle
        Click-Screen ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
        "clicked '$Target'"
    }
    "clickxy" {
        $h = Get-Window
        $r = Get-Rect $h
        $xy = $Target.Split(",")
        Click-Screen ($r.L + [int]$xy[0]) ($r.T + [int]$xy[1])
        "clicked window pixel $Target"
    }
    "wheel" {
        $h = Get-Window
        $r = Get-Rect $h
        $xy = $Target.Split(",")
        [ObsidianDriver.Win]::SetCursorPos($r.L + [int]$xy[0], $r.T + [int]$xy[1]) | Out-Null
        Start-Sleep -Milliseconds 100
        $notches = [int]$xy[2]
        $delta = if ($notches -ge 0) { -120 } else { 120 }
        for ($i = 0; $i -lt [Math]::Abs($notches); $i++) {
            [ObsidianDriver.Win]::mouse_event(0x0800, 0, 0, $delta, [IntPtr]::Zero)
            Start-Sleep -Milliseconds 50
        }
        "scrolled $notches notch(es)"
    }
    "key" {
        Get-Window | Out-Null
        $vk = [byte][System.Windows.Forms.Keys]$Target
        [ObsidianDriver.Win]::keybd_event($vk, 0, 0, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 80
        [ObsidianDriver.Win]::keybd_event($vk, 0, 2, [IntPtr]::Zero)   # key up
        "pressed $Target"
    }
    "shot" {
        $h = Get-Window
        $r = Get-Rect $h
        if (-not $Out) { $Out = Join-Path $PSScriptRoot "shots\shot.png" }
        $dir = Split-Path $Out -Parent
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
        $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
        $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
        "saved $Out ($($r.R - $r.L)x$($r.B - $r.T))"
    }
    "quit" {
        $p = Get-AppProcess
        if (-not $p) { "$App is not running"; break }
        $p.CloseMainWindow() | Out-Null
        if ($p.WaitForExit(5000)) { "$App closed" }
        else { Stop-Process -Id $p.Id -Force; "$App did not close within 5 s (a save prompt?); killed it" }
    }
}

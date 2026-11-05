param(
    [string]$Action = "info",
    [int]$X = 0,
    [int]$Y = 0
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$cs = @"
using System;
using System.Runtime.InteropServices;
public class TSProbeWin32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP   = 0x0004;
}
"@
Add-Type -TypeDefinition $cs

function Get-GameWindow {
    $p = Get-Process -Name RimWorld -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $p) { throw "RimWorld is not running" }
    return $p
}

function Get-WindowRect($proc) {
    $r = New-Object TSProbeWin32+RECT
    [void][TSProbeWin32]::GetWindowRect($proc.MainWindowHandle, [ref]$r)
    return $r
}

function Focus-Game($proc) {
    [void][TSProbeWin32]::ShowWindow($proc.MainWindowHandle, 3)
    [void][TSProbeWin32]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Milliseconds 600
}

function Click-At([int]$sx, [int]$sy) {
    [void][TSProbeWin32]::SetCursorPos($sx, $sy)
    Start-Sleep -Milliseconds 150
    [TSProbeWin32]::mouse_event([TSProbeWin32]::LEFTDOWN, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 70
    [TSProbeWin32]::mouse_event([TSProbeWin32]::LEFTUP, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 300
}

$proc = Get-GameWindow
$rect = Get-WindowRect $proc

switch ($Action) {
    "info" {
        Write-Host ("RimWorld PID {0}" -f $proc.Id)
        Write-Host ("MainWindowHandle {0}" -f $proc.MainWindowHandle)
        Write-Host ("Window rect: L={0} T={1} R={2} B={3} ({4}x{5})" -f $rect.Left, $rect.Top, $rect.Right, $rect.Bottom, ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))
        Write-Host ("Responding: {0}  Title: {1}" -f $proc.Responding, $proc.MainWindowTitle)
    }
    "focus" {
        Focus-Game $proc
        Write-Host "Focused."
    }
    "shot" {
        $out = Join-Path (Split-Path -Parent $PSScriptRoot) ("_test-results\game-{0}.png" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
        $d = Split-Path -Parent $out
        if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
        Focus-Game $proc
        $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
        $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
        Write-Host "Saved: $out"
    }
    "click" {
        Focus-Game $proc
        $ax = $rect.Left + $X
        $ay = $rect.Top + $Y
        Write-Host ("Click screen ({0},{1})  rel ({2},{3})" -f $ax, $ay, $X, $Y)
        Click-At $ax $ay
    }
    default { Write-Host "Unknown action: $Action" }
}

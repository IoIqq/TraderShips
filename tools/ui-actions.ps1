param(
    [Parameter(Mandatory=$true)][string]$Action,
    [int]$X = 0,
    [int]$Y = 0,
    [string]$Keys = "",
    [int]$Delay = 400
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# 必须先声明 DPI 感知：宿主是 1.5 倍缩放（2560 物理 / 1707 逻辑）。
# 不声明的话 CopyFromScreen 拿到的是 1707x1067 的缩放图，而鼠标坐标是物理像素，
# 截图和点击就会错位 1.5 倍。TSAct 里已经带了 SetProcessDPIAware 的 P/Invoke，
# 这里在加载类型后立刻调用一次。


# 关键：先声明 DPI 感知，否则 CopyFromScreen 只拿到 1707x1067 的缩放后画面，
# 而鼠标坐标是物理像素，两者会错位 1.5 倍（2560/1707）。
$cs = @"
using System;
using System.Runtime.InteropServices;
public class TSAct {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, uint e);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, UIntPtr e);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@
Add-Type -TypeDefinition $cs
[TSAct]::SetProcessDPIAware() | Out-Null

$proc = Get-Process -Name RimWorld -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { throw "RimWorld not running" }
$r = New-Object TSAct+RECT
[void][TSAct]::GetWindowRect($proc.MainWindowHandle, [ref]$r)

function Focus { [void][TSAct]::ShowWindow($proc.MainWindowHandle, 3); [void][TSAct]::SetForegroundWindow($proc.MainWindowHandle); Start-Sleep -Milliseconds 500 }

function Shot([string]$tag) {
    Focus
    $out = Join-Path (Split-Path -Parent $PSScriptRoot) ("_test-results\{0}-{1}.png" -f $tag, (Get-Date -Format "HHmmss"))
    $d = Split-Path -Parent $out
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
    $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "SHOT $out"
}

function ClickRel([int]$rx, [int]$ry) {
    Focus
    [void][TSAct]::SetCursorPos($r.Left + $rx, $r.Top + $ry)
    Start-Sleep -Milliseconds 150
    [TSAct]::mouse_event(0x0002, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 80
    [TSAct]::mouse_event(0x0004, 0, 0, 0, 0)
    Start-Sleep -Milliseconds $Delay
}

function SendKeysStr([string]$s) {
    Focus
    [System.Windows.Forms.SendKeys]::SendWait($s)
    Start-Sleep -Milliseconds $Delay
}

switch ($Action) {
    "shot"   { Shot "shot" }
    "click"  { ClickRel $X $Y; Write-Host "clicked $X,$Y" }
    "clickshot" { ClickRel $X $Y; Shot "after" }
    "keys"   { SendKeysStr $Keys; Write-Host "keys $Keys" }
    "keyshot" { SendKeysStr $Keys; Shot "after" }
    default  { Write-Host "unknown: $Action" }
}

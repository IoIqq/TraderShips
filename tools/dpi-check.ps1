Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$b = [System.Windows.Forms.SystemInformation]::VirtualScreen
Write-Host ("virtual: {0}x{1}" -f $b.Width, $b.Height)
$s = [System.Windows.Forms.Screen]::PrimaryScreen
Write-Host ("primary bounds: {0}x{1}" -f $s.Bounds.Width, $s.Bounds.Height)
Write-Host ("working area: {0}x{1}" -f $s.WorkingArea.Width, $s.WorkingArea.Height)
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class DPICheck {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr h, int t, out uint x, out uint y);
}
"@
[DPICheck]::SetProcessDPIAware() | Out-Null
$b2 = [System.Windows.Forms.SystemInformation]::VirtualScreen
Write-Host ("after DPIAware: {0}x{1}" -f $b2.Width, $b2.Height)
Write-Host ("cursor: {0}" -f [System.Windows.Forms.Cursor]::Position)

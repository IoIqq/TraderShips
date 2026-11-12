# 列出游戏目录里的 BCL 程序集及其版本，用来和 mod 的 AssemblyRef 对照。
$dir = 'H:\steam\steamapps\common\RimWorld'
$want = @('System.Private.CoreLib', 'System.Runtime', 'System.Collections', 'System.Collections.Generic', 'System.Linq', 'netstandard', 'System.Text.Encoding')
Get-ChildItem $dir -Filter '*.dll' | ForEach-Object {
    try {
        $n = [Reflection.AssemblyName]::GetAssemblyName($_.FullName)
        if ($want -contains $n.Name) {
            '{0,-28} {1,-24} {2}' -f $n.Name, $n.Version, $_.Name
        }
    } catch { }
}
'--- Environment ---'
[System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription

# TraderShips 1.7 build without the .NET 10 SDK.
# The host only has SDK 9.0.306 (no net10.0 targeting pack), but the .NET 10 *
# runtime* is installed, so we drive Roslyn csc.dll directly against the 10.0.7
# reference assemblies plus RimWorld's own assemblies.
#
# Usage:  powershell -File tools\build.ps1
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    python tools/make-rsp.py
    if ($LASTEXITCODE -ne 0) { throw 'make-rsp.py failed' }

    $csc = 'C:\Program Files\dotnet\sdk\9.0.306\Roslyn\bincore\csc.dll'
    if (-not (Test-Path $csc)) { throw "csc not found: $csc" }

    & dotnet $csc '@tools\build17.rsp'
    if ($LASTEXITCODE -ne 0) { throw "compile failed (exit $LASTEXITCODE)" }

    $dll = Join-Path $repo '1.7\Assemblies\TraderShips.dll'
    $md5 = (Get-FileHash $dll -Algorithm MD5).Hash.ToLower()
    Write-Host "OK  $dll"
    Write-Host "    $((Get-Item $dll).Length) bytes  md5 $md5"
}
finally {
    Pop-Location
}

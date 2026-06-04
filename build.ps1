# Compile DeviceOrder en un seul .exe (sans SDK : on utilise csc.exe de .NET Framework, deja
# present sur tout Windows). Le .exe produit ne necessite aucun runtime a installer.

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) { throw "csc.exe introuvable (.NET Framework 4.x manquant ?)" }

$out = Join-Path $PSScriptRoot 'DeviceOrder.exe'

& $csc `
    /nologo `
    /target:winexe `
    /platform:x64 `
    /codepage:65001 `
    /out:$out `
    /win32manifest:app.manifest `
    /win32icon:DeviceOrder.ico `
    /resource:logo.svg `
    /resource:app.ico `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.Web.Extensions.dll `
    Program.cs

if ($LASTEXITCODE -ne 0) { throw "Echec de compilation (code $LASTEXITCODE)" }
Write-Host "OK -> $out" -ForegroundColor Green

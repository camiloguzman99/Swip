<#
.SYNOPSIS
    Desinstalador de Swip: detiene y elimina el servicio, borra el arranque automático,
    cierra el gato y elimina los archivos instalados. Se auto-eleva a administrador.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File uninstall.ps1
#>
param(
    [string]$ServiceName = "SwipService",
    [string]$InstallRoot = "$env:ProgramFiles\Swip"
)

$ErrorActionPreference = "Stop"

$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs `
        -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    return
}

Write-Host "==> Cerrando el gato (si está abierto)..." -ForegroundColor Cyan
Get-Process -Name "Swip" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "==> Quitando el arranque automático..." -ForegroundColor Cyan
$shortcut = Join-Path ([Environment]::GetFolderPath('Startup')) "Swip.lnk"
if (Test-Path $shortcut) { Remove-Item $shortcut -Force }

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    Write-Host "==> Deteniendo y eliminando el servicio..." -ForegroundColor Cyan
    if ($svc.Status -ne "Stopped") { Stop-Service $ServiceName -Force }
    sc.exe delete $ServiceName | Out-Null
}

if (Test-Path $InstallRoot) {
    Write-Host "==> Eliminando archivos de $InstallRoot..." -ForegroundColor Cyan
    Remove-Item $InstallRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Swip desinstalado." -ForegroundColor Green

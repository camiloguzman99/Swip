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

# Primero el servicio: su vigilante relanza el gato cada 30 s y bloquearía los archivos.
Write-Host "==> Deteniendo el servicio (su vigilante relanza el gato)..." -ForegroundColor Cyan
$pre = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($pre -and $pre.Status -ne "Stopped") {
    Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
    try { $pre.WaitForStatus("Stopped", "00:00:20") } catch { }
}

Write-Host "==> Cerrando el gato (si está abierto)..." -ForegroundColor Cyan
Get-Process -Name "Swip" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600

Write-Host "==> Quitando el arranque automático..." -ForegroundColor Cyan
foreach ($sc in @(
    (Join-Path ([Environment]::GetFolderPath('CommonStartup')) "Swip.lnk"),
    (Join-Path ([Environment]::GetFolderPath('Startup')) "Swip.lnk"))) {
    if (Test-Path $sc) { Remove-Item $sc -Force -ErrorAction SilentlyContinue }
}

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

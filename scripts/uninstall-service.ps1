#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Detiene y elimina el servicio Swip.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\uninstall-service.ps1
#>
param(
    [string]$ServiceName = "SwipService"
)

$ErrorActionPreference = "Stop"

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
    Write-Host "El servicio '$ServiceName' no está instalado." -ForegroundColor Yellow
    return
}

if ($svc.Status -ne "Stopped") {
    Write-Host "==> Deteniendo el servicio..." -ForegroundColor Cyan
    Stop-Service $ServiceName -Force
}

Write-Host "==> Eliminando el servicio..." -ForegroundColor Cyan
sc.exe delete $ServiceName | Out-Host
Write-Host "==> Servicio eliminado." -ForegroundColor Green

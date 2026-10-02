#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Compila e instala el servicio Swip como servicio de Windows ejecutándose como SYSTEM.
.DESCRIPTION
    El servicio debe correr como LocalSystem para poder cambiar de sesión sin contraseña
    (WTSConnectSession) y leer las apps con ventana de la otra sesión. Ejecuta este script
    una sola vez, como administrador, desde la raíz del repositorio.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\install-service.ps1
#>
param(
    [string]$ServiceName = "SwipService",
    [string]$InstallDir  = "$env:ProgramFiles\Swip"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "==> Publicando el servicio..." -ForegroundColor Cyan
dotnet publish "$repoRoot\src\Swip.Service\Swip.Service.csproj" -c Release -r win-x64 `
    --self-contained false -o "$InstallDir" | Out-Host

$exePath = Join-Path $InstallDir "Swip.Service.exe"
if (-not (Test-Path $exePath)) {
    throw "No se encontró el ejecutable publicado en $exePath"
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "==> El servicio ya existe; se detiene y se elimina para reinstalarlo." -ForegroundColor Yellow
    if ($existing.Status -ne "Stopped") { Stop-Service $ServiceName -Force }
    sc.exe delete $ServiceName | Out-Host
    Start-Sleep -Seconds 1
}

Write-Host "==> Creando el servicio como LocalSystem..." -ForegroundColor Cyan
New-Service -Name $ServiceName `
    -BinaryPathName "`"$exePath`"" `
    -DisplayName "Swip - Cambio rápido de usuario" `
    -Description "Cambia entre sesiones y lee las apps con ventana de la otra sesión para la app Swip." `
    -StartupType Automatic | Out-Null

Start-Service $ServiceName
Write-Host "==> Servicio instalado y en ejecución." -ForegroundColor Green
Get-Service $ServiceName | Format-List Name, Status, StartType

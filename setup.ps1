<#
.SYNOPSIS
    Reinstala/repara Swip desde los binarios YA COMPILADOS del release (sin SDK ni repo).
.DESCRIPTION
    Pensado para ejecutarse desde la carpeta extraída del zip del release (donde están las
    carpetas Service\ y App\). RECREA el servicio de Windows desde cero (soluciona el caso en
    que una versión vieja del servicio quedó corriendo porque el .exe estaba en uso), configura
    el arranque automático para todos los usuarios y los permisos de la carpeta compartida.
    Se auto-eleva a administrador.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File setup.ps1
#>
param(
    [string]$ServiceName = "SwipService",
    [string]$InstallRoot = "$env:ProgramFiles\Swip"
)

$ErrorActionPreference = "Stop"

# --- Auto-elevación --------------------------------------------------------------
$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs `
        -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    return
}

function Pause-Err($m) {
    Write-Host ""; Write-Host "ERROR: $m" -ForegroundColor Red; Write-Host ""
    Read-Host "Pulsa Enter para cerrar"
}

try {
    $Host.UI.RawUI.WindowTitle = "Swip - Setup"
    $src = $PSScriptRoot
    $serviceSrc = Join-Path $src "Service"
    $appSrc     = Join-Path $src "App"
    if (-not (Test-Path $serviceSrc) -or -not (Test-Path $appSrc)) {
        throw "Ejecuta setup.ps1 desde la carpeta extraída del zip (debe contener Service\ y App\)."
    }

    Write-Host "==> Cerrando Swip..." -ForegroundColor Cyan
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne "Stopped") { Stop-Service $ServiceName -Force; try { $svc.WaitForStatus("Stopped","00:00:20") } catch {} }
    Get-Process -Name "Swip","Swip.Service" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 700

    Write-Host "==> Eliminando el servicio anterior (para recrearlo limpio)..." -ForegroundColor Cyan
    if ($svc) { sc.exe delete $ServiceName | Out-Null; Start-Sleep -Seconds 1 }

    Write-Host "==> Copiando binarios..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path (Join-Path $InstallRoot "Service") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $InstallRoot "App") | Out-Null
    Copy-Item (Join-Path $serviceSrc "*") (Join-Path $InstallRoot "Service") -Recurse -Force
    Copy-Item (Join-Path $appSrc "*")     (Join-Path $InstallRoot "App") -Recurse -Force
    $up = Join-Path $src "update.ps1"
    if (Test-Path $up) { Copy-Item $up (Join-Path $InstallRoot "update.ps1") -Force }

    $serviceExe = Join-Path $InstallRoot "Service\Swip.Service.exe"
    if (-not (Test-Path $serviceExe)) { throw "No se encontró $serviceExe" }

    Write-Host "==> Creando el servicio (LocalSystem, automático)..." -ForegroundColor Cyan
    New-Service -Name $ServiceName -BinaryPathName "`"$serviceExe`"" `
        -DisplayName "Swip - Cambio rápido de usuario" `
        -Description "Cambia entre sesiones y lee las apps con ventana de la otra sesión para la app Swip." `
        -StartupType Automatic | Out-Null
    Start-Service $ServiceName

    Write-Host "==> Arranque automático para todos los usuarios..." -ForegroundColor Cyan
    $appExe = Join-Path $InstallRoot "App\Swip.exe"
    $lnk = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('CommonStartup')) "Swip.lnk"))
    $lnk.TargetPath = $appExe; $lnk.WorkingDirectory = (Join-Path $InstallRoot "App"); $lnk.Description = "Swip"; $lnk.Save()
    $oldUser = Join-Path ([Environment]::GetFolderPath('Startup')) "Swip.lnk"
    if (Test-Path $oldUser) { Remove-Item $oldUser -Force -ErrorAction SilentlyContinue }

    Write-Host "==> Lanzando el gato..." -ForegroundColor Cyan
    Get-Process -Name "Swip" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Process $appExe

    Write-Host ""
    Write-Host "Swip reinstalado correctamente (servicio recreado desde cero)." -ForegroundColor Green
    Write-Host "Ahora abre el menú del gato en segundo plano y revisa: $env:ProgramData\Swip\service.log" -ForegroundColor Green
    Write-Host ""
    Read-Host "Pulsa Enter para cerrar"
}
catch {
    Pause-Err $_.Exception.Message
}

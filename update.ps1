<#
.SYNOPSIS
    Actualiza Swip a la última versión publicada, SIN necesidad de descargar el repo.
.DESCRIPTION
    Descarga el release "latest" desde GitHub (compilado automáticamente por GitHub Actions),
    detiene el gato y el servicio, reemplaza los archivos y vuelve a arrancar. Se auto-eleva.
    Si algo falla, deja la ventana abierta con el error.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File update.ps1
#>
param(
    [string]$Owner       = "camiloguzman99",
    [string]$Repo        = "Swip",
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

function Pause-OnError($message) {
    Write-Host ""
    Write-Host "ERROR al actualizar:" -ForegroundColor Red
    Write-Host $message -ForegroundColor Red
    Write-Host ""
    Read-Host "Pulsa Enter para cerrar"
}

try {
    $zipUrl  = "https://github.com/$Owner/$Repo/releases/download/latest/Swip-win-x64.zip"
    $tmp     = Join-Path $env:TEMP ("swip-update-" + [Guid]::NewGuid().ToString("N"))
    $zipPath = Join-Path $env:TEMP "Swip-win-x64.zip"

    Write-Host "==> Descargando la última versión..." -ForegroundColor Cyan
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing

    if (-not (Test-Path $zipPath) -or (Get-Item $zipPath).Length -lt 10000) {
        throw "La descarga falló o el archivo está incompleto ($zipUrl)."
    }

    Write-Host "==> Cerrando el gato..." -ForegroundColor Cyan
    Get-Process -Name "Swip" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 400

    Write-Host "==> Deteniendo el servicio..." -ForegroundColor Cyan
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne "Stopped") { Stop-Service $ServiceName -Force }

    Write-Host "==> Extrayendo..." -ForegroundColor Cyan
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    Expand-Archive -Path $zipPath -DestinationPath $tmp -Force

    $copied = $false
    foreach ($part in @("Service", "App")) {
        $src = Join-Path $tmp $part
        $dst = Join-Path $InstallRoot $part
        if (Test-Path $src) {
            New-Item -ItemType Directory -Force -Path $dst | Out-Null
            Copy-Item -Path (Join-Path $src "*") -Destination $dst -Recurse -Force
            $copied = $true
        }
    }
    if (-not $copied) {
        throw "El zip no contenía las carpetas Service/App esperadas."
    }

    # Mantener actualizado el propio update.ps1 junto a la instalación (si viniera en el zip).
    $zipUpdate = Join-Path $tmp "update.ps1"
    if (Test-Path $zipUpdate) {
        Copy-Item $zipUpdate (Join-Path $InstallRoot "update.ps1") -Force
    }

    Write-Host "==> Rearrancando el servicio..." -ForegroundColor Cyan
    if ($svc) { Start-Service $ServiceName }

    Write-Host "==> Lanzando el gato..." -ForegroundColor Cyan
    $appExe = Join-Path $InstallRoot "App\Swip.exe"
    if (Test-Path $appExe) { Start-Process $appExe } else { throw "No se encontró $appExe" }

    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue

    Write-Host ""
    Write-Host "Swip actualizado correctamente." -ForegroundColor Green
    Start-Sleep -Seconds 2
}
catch {
    Pause-OnError $_.Exception.Message
}

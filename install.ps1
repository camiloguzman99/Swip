<#
.SYNOPSIS
    Instalador todo-en-uno de Swip para Windows 11.
.DESCRIPTION
    Compila e instala el servicio (como SYSTEM), publica el gato, crea un acceso directo
    de arranque automático al iniciar sesión, y lanza la app. Se auto-eleva a administrador
    si hace falta (el servicio lo requiere).

    Ejecútalo con clic derecho > "Ejecutar con PowerShell", o desde una consola:
        powershell -ExecutionPolicy Bypass -File install.ps1
.NOTES
    Requiere el SDK de .NET 8: https://dotnet.microsoft.com/download/dotnet/8.0
#>
param(
    [string]$ServiceName = "SwipService",
    [string]$InstallRoot = "$env:ProgramFiles\Swip"
)

$ErrorActionPreference = "Stop"

# --- Auto-elevación a administrador ----------------------------------------------
$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Se requieren permisos de administrador; relanzando con UAC..." -ForegroundColor Yellow
    Start-Process powershell.exe -Verb RunAs `
        -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    return
}

$repoRoot    = $PSScriptRoot
$serviceDir  = Join-Path $InstallRoot "Service"
$appDir      = Join-Path $InstallRoot "App"
$serviceProj = Join-Path $repoRoot "src\Swip.Service\Swip.Service.csproj"
$appProj     = Join-Path $repoRoot "src\Swip.App\Swip.App.csproj"

Write-Host ""
Write-Host "  Swip - instalador" -ForegroundColor Cyan
Write-Host "  =================" -ForegroundColor Cyan
Write-Host ""

# --- Comprobar .NET SDK ----------------------------------------------------------
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: no se encontró 'dotnet'." -ForegroundColor Red
    Write-Host "Instala el SDK de .NET 8 y vuelve a ejecutar:" -ForegroundColor Red
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Red
    return
}

# --- 1) Servicio -----------------------------------------------------------------
Write-Host "==> [1/4] Publicando el servicio..." -ForegroundColor Cyan
dotnet publish $serviceProj -c Release -r win-x64 --self-contained false -o $serviceDir | Out-Host
$serviceExe = Join-Path $serviceDir "Swip.Service.exe"
if (-not (Test-Path $serviceExe)) { throw "No se publicó el servicio en $serviceExe" }

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "    (el servicio ya existía; se reinstala)" -ForegroundColor DarkGray
    if ($existing.Status -ne "Stopped") { Stop-Service $ServiceName -Force }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

Write-Host "==> [2/4] Instalando el servicio como LocalSystem..." -ForegroundColor Cyan
New-Service -Name $ServiceName `
    -BinaryPathName "`"$serviceExe`"" `
    -DisplayName "Swip - Cambio rápido de usuario" `
    -Description "Cambia entre sesiones y lee las apps con ventana de la otra sesión para la app Swip." `
    -StartupType Automatic | Out-Null
Start-Service $ServiceName
Write-Host "    Servicio '$ServiceName' en ejecución." -ForegroundColor Green

# --- 2) App (el gato) ------------------------------------------------------------
Write-Host "==> [3/4] Publicando el gato..." -ForegroundColor Cyan
dotnet publish $appProj -c Release -r win-x64 --self-contained false -o $appDir | Out-Host
$appExe = Join-Path $appDir "Swip.exe"
if (-not (Test-Path $appExe)) { throw "No se publicó la app en $appExe" }

# --- Copiar el actualizador junto a la instalación -------------------------------
Copy-Item -Path (Join-Path $repoRoot "update.ps1") -Destination (Join-Path $InstallRoot "update.ps1") -Force

# --- 3) Arranque automático al iniciar sesión ------------------------------------
Write-Host "==> [4/4] Creando acceso directo de arranque..." -ForegroundColor Cyan
$startup  = [Environment]::GetFolderPath('Startup')
$shortcut = Join-Path $startup "Swip.lnk"
$shell    = New-Object -ComObject WScript.Shell
$lnk      = $shell.CreateShortcut($shortcut)
$lnk.TargetPath       = $appExe
$lnk.WorkingDirectory = $appDir
$lnk.Description       = "Swip - gatos de sesión"
$lnk.Save()
Write-Host "    Acceso directo creado en: $shortcut" -ForegroundColor Green

# --- Lanzar ----------------------------------------------------------------------
Write-Host ""
Write-Host "Instalación completa. Lanzando el gato..." -ForegroundColor Green
Start-Process $appExe

Write-Host ""
Write-Host "Listo:" -ForegroundColor Cyan
Write-Host "  - Servicio:  $serviceExe (automático, SYSTEM)"
Write-Host "  - App:       $appExe (arranca al iniciar sesión)"
Write-Host "  - Para desinstalar: uninstall.ps1"
Write-Host ""

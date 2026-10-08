<#
.SYNOPSIS
    Actualiza Swip a la última versión publicada, SIN necesidad de descargar el repo.
.DESCRIPTION
    1) Descarga el release "latest" y VERIFICA su SHA-256; 2) lo extrae; 3) solo entonces cierra el
    gato y detiene el servicio, reemplaza los archivos y lo vuelve a arrancar. Así el gato sigue
    funcionando durante la descarga y, si esta falla, no se toca nada.
    Se auto-eleva (UAC).

    Con -Silent no muestra pausas ni espera ninguna tecla: es lo que usa el botón "Actualizar" del
    gato, que lo lanza sin ventana. Todo queda en %ProgramData%\Swip\update.log y, si algo falla,
    se muestra un aviso.
.PARAMETER Silent
    Modo segundo plano: sin pausas "Pulsa Enter", con registro en update.log y aviso solo si falla.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File update.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -WindowStyle Hidden -File update.ps1 -Silent
#>
param(
    [string]$Owner       = "camiloguzman99",
    [string]$Repo        = "Swip",
    [string]$ServiceName = "SwipService",
    [string]$InstallRoot = "$env:ProgramFiles\Swip",
    [switch]$Silent
)

$ErrorActionPreference = "Stop"

# --- Auto-elevación --------------------------------------------------------------
$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $relaunch = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    if ($Silent) {
        Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList ($relaunch + " -Silent")
    }
    else {
        Start-Process powershell.exe -Verb RunAs -ArgumentList $relaunch
    }
    return
}

# --- Registro (modo silencioso): no hay consola que mirar ---------------------------
$logFile = Join-Path $env:ProgramData "Swip\update.log"
$transcribing = $false
if ($Silent) {
    try {
        New-Item -ItemType Directory -Force -Path (Split-Path $logFile) | Out-Null
        if ((Test-Path $logFile) -and ((Get-Item $logFile).Length -gt 200KB)) { Remove-Item $logFile -Force }
        Start-Transcript -Path $logFile -Append | Out-Null
        $transcribing = $true
    }
    catch { }
}

function Stop-Swip($serviceName) {
    # 1) Detener el servicio y esperar a que quede detenido (libera Swip.Service.exe).
    $svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne "Stopped") {
        Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
        try { $svc.WaitForStatus("Stopped", "00:00:20") } catch { }
    }
    # 2) Cerrar el gato y cualquier ayudante del servicio.
    Get-Process -Name "Swip", "Swip.Service" -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    # 3) Esperar a que Windows libere los archivos.
    for ($i = 0; $i -lt 10; $i++) {
        if (-not (Get-Process -Name "Swip", "Swip.Service" -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 300
    }
    Start-Sleep -Milliseconds 400
}

function Copy-WithRetry($src, $dst) {
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    for ($i = 1; $i -le 5; $i++) {
        try {
            Copy-Item -Path (Join-Path $src "*") -Destination $dst -Recurse -Force
            return
        }
        catch {
            if ($i -eq 5) { throw }
            Write-Host "   (archivo en uso, reintentando $i/5...)" -ForegroundColor DarkYellow
            Start-Sleep -Seconds 1
        }
    }
}

# El gato de ESTA sesión (el de otra sesión no cuenta: lo relanza el servicio).
function Get-MySwip {
    $mine = (Get-Process -Id $PID).SessionId
    Get-Process -Name "Swip" -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $mine }
}

function Show-Alert($text) {
    try { (New-Object -ComObject WScript.Shell).Popup($text, 0, "Swip", 16) | Out-Null } catch { }
}

$appExe  = Join-Path $InstallRoot "App\Swip.exe"
$failure = $null

try {
    try { $Host.UI.RawUI.WindowTitle = "Swip - Actualización" } catch { }
    Write-Host "============================================" -ForegroundColor Yellow
    Write-Host "   Swip - Actualizando a la ultima version" -ForegroundColor Yellow
    Write-Host "============================================" -ForegroundColor Yellow
    Write-Host ("   {0}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss")) -ForegroundColor DarkGray
    Write-Host ""

    $zipUrl  = "https://github.com/$Owner/$Repo/releases/download/latest/Swip-win-x64.zip"
    $shaUrl  = "$zipUrl.sha256"
    $tmp     = Join-Path $env:TEMP ("swip-update-" + [Guid]::NewGuid().ToString("N"))
    $zipPath = Join-Path $env:TEMP "Swip-win-x64.zip"
    $shaPath = Join-Path $env:TEMP "Swip-win-x64.zip.sha256"

    # --- 1) Descargar y VERIFICAR (el gato y el servicio siguen en marcha) ---------
    Write-Host "==> Descargando la última versión..." -ForegroundColor Cyan
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    foreach ($f in @($zipPath, $shaPath)) { if (Test-Path $f) { Remove-Item $f -Force } }

    # Reintentos: tras publicar una versión puede haber un 404 transitorio de GitHub, o un instante
    # en que el zip y su hash son de versiones distintas (se suben uno tras otro).
    # NUNCA se instala un zip cuyo SHA-256 no coincida con el publicado: protege de descargas
    # corruptas o incompletas y de un zip sustituido sin su hash.
    $downloaded = $false
    for ($i = 1; $i -le 6; $i++) {
        try {
            Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing
            Invoke-WebRequest -Uri $shaUrl -OutFile $shaPath -UseBasicParsing
            if ((Get-Item $zipPath).Length -lt 10000) { throw "el zip descargado es demasiado pequeño" }

            $expected = ((Get-Content $shaPath -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
            $actual   = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($expected -notmatch '^[0-9a-f]{64}$') { throw "el archivo .sha256 no es válido" }
            if ($actual -ne $expected) { throw "SHA-256 distinto (esperado $expected, obtenido $actual)" }

            Write-Host "   SHA-256 verificado: $actual" -ForegroundColor DarkGray
            $downloaded = $true
            break
        }
        catch {
            Write-Host "   (intento $i/6: $($_.Exception.Message))" -ForegroundColor DarkYellow
        }
        Start-Sleep -Seconds 5
    }
    if (-not $downloaded) {
        throw "No se pudo descargar y VERIFICAR la última versión tras varios intentos ($zipUrl). No se instaló nada."
    }

    # --- 2) Extraer y comprobar el contenido ANTES de parar nada -------------------
    Write-Host "==> Extrayendo..." -ForegroundColor Cyan
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    Expand-Archive -Path $zipPath -DestinationPath $tmp -Force
    if (-not (Test-Path (Join-Path $tmp "Service")) -and -not (Test-Path (Join-Path $tmp "App"))) {
        throw "El zip no contenía las carpetas Service/App esperadas. No se instaló nada."
    }

    # --- 3) Solo ahora: cerrar el gato y el servicio, y reemplazar ------------------
    Write-Host "==> Cerrando Swip (gato y servicio)..." -ForegroundColor Cyan
    Stop-Swip $ServiceName

    Write-Host "==> Reemplazando archivos..." -ForegroundColor Cyan
    foreach ($part in @("Service", "App")) {
        $src = Join-Path $tmp $part
        if (Test-Path $src) { Copy-WithRetry $src (Join-Path $InstallRoot $part) }
    }

    # Mantener actualizado el propio update.ps1 junto a la instalación.
    $zipUpdate = Join-Path $tmp "update.ps1"
    if (Test-Path $zipUpdate) { Copy-Item $zipUpdate (Join-Path $InstallRoot "update.ps1") -Force }

    # Asegurar el arranque automático para TODOS los usuarios (acceso directo común).
    Write-Host "==> Asegurando el arranque automático..." -ForegroundColor Cyan
    try {
        $common   = [Environment]::GetFolderPath('CommonStartup')
        $shortcut = Join-Path $common "Swip.lnk"
        $wsh = New-Object -ComObject WScript.Shell
        $lnk = $wsh.CreateShortcut($shortcut)
        $lnk.TargetPath = $appExe
        $lnk.WorkingDirectory = (Join-Path $InstallRoot "App")
        $lnk.Description = "Swip - gatos de sesión"
        $lnk.Save()
        $oldUser = Join-Path ([Environment]::GetFolderPath('Startup')) "Swip.lnk"
        if (Test-Path $oldUser) { Remove-Item $oldUser -Force -ErrorAction SilentlyContinue }
    } catch { Write-Host "   (no se pudo crear el acceso directo: $($_.Exception.Message))" -ForegroundColor DarkYellow }

    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $zipPath, $shaPath -Force -ErrorAction SilentlyContinue

    Write-Host ""
    Write-Host "Swip actualizado correctamente." -ForegroundColor Green
}
catch {
    $failure = $_.Exception.Message
    Write-Host ""
    Write-Host "ERROR al actualizar:" -ForegroundColor Red
    Write-Host $failure -ForegroundColor Red
}
finally {
    # Pase lo que pase, dejar el servicio y el gato en marcha. Se hace ANTES de avisar de un error:
    # un aviso que espera un clic no debe retrasar la vuelta del gato.
    Write-Host "==> Rearrancando el servicio..." -ForegroundColor Cyan
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne "Running") { Start-Service $ServiceName -ErrorAction SilentlyContinue }

    # El servicio relanza el gato en cada sesión (sin privilegios de administrador) a los pocos
    # segundos de arrancar. Solo si no aparece, se lanza desde aquí.
    for ($i = 0; $i -lt 20; $i++) {
        if (Get-MySwip) { break }
        Start-Sleep -Seconds 1
    }
    if (-not (Get-MySwip) -and (Test-Path $appExe)) {
        Write-Host "==> Lanzando el gato..." -ForegroundColor Cyan
        try { Start-Process $appExe }
        catch { Write-Host "   (no se pudo lanzar el gato: $($_.Exception.Message))" -ForegroundColor DarkYellow }
    }
}

if ($transcribing) { try { Stop-Transcript | Out-Null } catch { } }

if ($failure) {
    if ($Silent) {
        Show-Alert ("No se pudo actualizar Swip.`n`n" + $failure + "`n`nDetalles en: " + $logFile)
    }
    else {
        Write-Host ""
        Read-Host "Pulsa Enter para cerrar"
    }
    exit 1
}

if (-not $Silent) {
    Write-Host "El gato ya se relanzó con la nueva versión." -ForegroundColor Green
    Write-Host ""
    Read-Host "Pulsa Enter para cerrar"
}
exit 0

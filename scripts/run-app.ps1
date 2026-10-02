<#
.SYNOPSIS
    Compila y ejecuta el gato (Swip.App). No requiere administrador.
.DESCRIPTION
    El gato corre como tu usuario normal y se comunica con el servicio por el named pipe.
    Asegúrate de haber instalado antes el servicio con scripts\install-service.ps1.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\run-app.ps1
#>
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

dotnet run --project "$repoRoot\src\Swip.App\Swip.App.csproj" -c Release

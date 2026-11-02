param(
    [string]$ServiceName = "Extractarr"
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Please run this script in an elevated (Administrator) PowerShell prompt."
    exit 1
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "==> Stopping $ServiceName service..." -ForegroundColor Cyan
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }
    Write-Host "==> Removing $ServiceName service..." -ForegroundColor Cyan
    sc.exe delete $ServiceName | Out-Null
} else {
    Write-Host "Service '$ServiceName' is not installed." -ForegroundColor Yellow
}

Remove-NetFirewallRule -DisplayName "Extractarr Web UI" -ErrorAction SilentlyContinue
Write-Host "==> Extractarr Windows Service uninstalled." -ForegroundColor Green


param(
    [string]$ServiceName = "Extractarr",
    [string]$DisplayName = "Extractarr",
    [string]$Description = "Automated Archive Extraction Engine & Sonarr v5 Web UI for the *arr Ecosystem",
    [int]$Port = 8686,
    [string]$DataDir = ""
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Please run this script in an elevated (Administrator) PowerShell prompt."
    exit 1
}

$exePath = Join-Path $PSScriptRoot "Extractarr.Host.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "Could not find Extractarr.Host.exe in $PSScriptRoot"
    exit 1
}

if ([string]::IsNullOrWhiteSpace($DataDir)) {
    $DataDir = Join-Path $env:ProgramData "Extractarr"
}

New-Item -ItemType Directory -Force -Path $DataDir | Out-Null

$binArgs = "`"$exePath`" --data=`"$DataDir`" --port=$Port"

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "==> Stopping existing $ServiceName service..." -ForegroundColor Cyan
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

Write-Host "==> Registering Windows Service '$ServiceName'..." -ForegroundColor Cyan
New-Service -Name $ServiceName `
            -BinaryPathName $binArgs `
            -DisplayName $DisplayName `
            -Description $Description `
            -StartupType Automatic | Out-Null

sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null

Write-Host "==> Configuring Windows Firewall rule for TCP port $Port..." -ForegroundColor Cyan
Remove-NetFirewallRule -DisplayName "Extractarr Web UI" -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName "Extractarr Web UI" `
                    -Direction Inbound `
                    -Action Allow `
                    -Protocol TCP `
                    -LocalPort $Port | Out-Null

Write-Host "==> Starting $ServiceName service..." -ForegroundColor Cyan
Start-Service -Name $ServiceName

Write-Host "==================================================================" -ForegroundColor Green
Write-Host " Extractarr Windows Service is installed and running!" -ForegroundColor Green
Write-Host " Web UI:      http://localhost:$Port" -ForegroundColor Green
Write-Host " Data Dir:    $DataDir" -ForegroundColor Green
Write-Host " Service:     Get-Service $ServiceName" -ForegroundColor Green
Write-Host "==================================================================" -ForegroundColor Green

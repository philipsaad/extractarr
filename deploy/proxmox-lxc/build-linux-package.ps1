$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$PublishDir = Join-Path $RepoRoot "deploy\proxmox-lxc\publish"
$TarballPath = Join-Path $RepoRoot "deploy\proxmox-lxc\extractarr-linux-x64.tar.gz"

Write-Host "==> [1/4] Building React frontend..."
Push-Location (Join-Path $RepoRoot "frontend")
npm run build
Pop-Location

Write-Host "==> [2/4] Syncing frontend bundle to src/Extractarr.Host/wwwroot..."
$WwwRoot = Join-Path $RepoRoot "src\Extractarr.Host\wwwroot"
if (Test-Path $WwwRoot) {
    Remove-Item "$WwwRoot\*" -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $WwwRoot -Force | Out-Null
}
Copy-Item (Join-Path $RepoRoot "frontend\dist\*") -Destination $WwwRoot -Recurse -Force

Write-Host "==> [3/4] Publishing self-contained .NET 10 linux-x64 release..."
if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}
dotnet publish (Join-Path $RepoRoot "src\Extractarr.Host\Extractarr.Host.csproj") `
    -c Release `
    -r linux-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $PublishDir

Copy-Item (Join-Path $PSScriptRoot "extractarr.service") -Destination $PublishDir -Force

Write-Host "==> [4/4] Packaging tarball: $TarballPath..."
if (Test-Path $TarballPath) {
    Remove-Item $TarballPath -Force
}
tar -czf $TarballPath -C $PublishDir .
Remove-Item $PublishDir -Recurse -Force

$sizeMb = [Math]::Round((Get-Item $TarballPath).Length / 1MB, 2)
Write-Host "==> Created $TarballPath ($sizeMb MB) successfully!"


param(
    [Parameter(Mandatory = $true)]
    [string]$SshTarget, # e.g. "root@192.168.1.50" (either the LXC IP or the Proxmox VE host)

    [Parameter(Mandatory = $false)]
    [string]$ProxmoxCtid = "", # Optional: If SshTarget is the Proxmox host, specify the LXC Container ID (e.g. "105")

    [Parameter(Mandatory = $false)]
    [int]$SshPort = 22
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$TarballPath = Join-Path $RepoRoot "deploy\proxmox-lxc\extractarr-linux-x64.tar.gz"
$InstallerPath = Join-Path $RepoRoot "deploy\proxmox-lxc\install-lxc.sh"

if (-not (Test-Path $TarballPath)) {
    Write-Host "==> Building self-contained linux-x64 release package first..."
    & (Join-Path $PSScriptRoot "build-linux-package.ps1")
}

Write-Host "==> Uploading package and installer to $SshTarget..."
scp -P $SshPort $TarballPath $InstallerPath "${SshTarget}:/tmp/"

if ([string]::IsNullOrWhiteSpace($ProxmoxCtid)) {
    Write-Host "==> Running installer directly on LXC container ($SshTarget)..."
    ssh -p $SshPort $SshTarget "sed -i 's/\r$//' /tmp/install-lxc.sh && chmod +x /tmp/install-lxc.sh && bash /tmp/install-lxc.sh /tmp/extractarr-linux-x64.tar.gz"
} else {
    Write-Host "==> Pushing package into Proxmox LXC Container CTID $ProxmoxCtid on $SshTarget..."
    $remoteCmd = @"
sed -i 's/\r$//' /tmp/install-lxc.sh &&
pct push $ProxmoxCtid /tmp/extractarr-linux-x64.tar.gz /tmp/extractarr-linux-x64.tar.gz &&
pct push $ProxmoxCtid /tmp/install-lxc.sh /tmp/install-lxc.sh &&
pct exec $ProxmoxCtid -- chmod +x /tmp/install-lxc.sh &&
pct exec $ProxmoxCtid -- bash /tmp/install-lxc.sh /tmp/extractarr-linux-x64.tar.gz
"@
    ssh -p $SshPort $SshTarget $remoteCmd
}


#!/usr/bin/env bash
set -euo pipefail

ARCHIVE_PATH="${1:-/tmp/extractarr-linux-x64.tar.gz}"
INSTALL_DIR="/opt/Extractarr"
DATA_DIR="/var/lib/extractarr"
SERVICE_FILE="/etc/systemd/system/extractarr.service"

echo "==> [1/5] Checking runtime dependencies..."
if command -v apt-get >/dev/null 2>&1; then
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq
    # Install curl, ca-certificates, and whichever libicu package is available in this Debian/Ubuntu release
    ICU_PKG=$(apt-cache search --names-only '^libicu[0-9]+$' | awk '{print $1}' | sort -V | tail -n 1 || true)
    apt-get install -y -qq ca-certificates curl tar tzdata ${ICU_PKG:-} >/dev/null
elif command -v dnf >/dev/null 2>&1; then
    dnf install -y -q ca-certificates curl tar tzdata libicu >/dev/null
elif command -v pacman >/dev/null 2>&1; then
    pacman -Sy --noconfirm --needed ca-certificates curl tar tzdata icu >/dev/null
elif command -v apk >/dev/null 2>&1; then
    apk add --no-cache ca-certificates curl tar tzdata icu-libs >/dev/null
fi

echo "==> [2/5] Creating directories (${INSTALL_DIR}, ${DATA_DIR})..."
mkdir -p "${INSTALL_DIR}" "${DATA_DIR}"

if systemctl is-active --quiet extractarr 2>/dev/null; then
    echo "==> Stopping existing extractarr service..."
    systemctl stop extractarr
fi

echo "==> [3/5] Extracting ${ARCHIVE_PATH} to ${INSTALL_DIR}..."
tar -xzf "${ARCHIVE_PATH}" -C "${INSTALL_DIR}"
chmod +x "${INSTALL_DIR}/Extractarr.Host"

echo "==> [4/5] Configuring systemd service (${SERVICE_FILE})..."
if [ -f "${INSTALL_DIR}/extractarr.service" ]; then
    cp "${INSTALL_DIR}/extractarr.service" "${SERVICE_FILE}"
else
    cat > "${SERVICE_FILE}" <<'EOF'
[Unit]
Description=Extractarr Daemon (Automated Archive Extraction Engine for *arr)
After=syslog.target network.target

[Service]
User=root
Group=root
UMask=0002
Type=simple
WorkingDirectory=/opt/Extractarr
ExecStart=/opt/Extractarr/Extractarr.Host
Environment="UNPACKARR_DATA_DIR=/var/lib/extractarr"
Environment="UNPACKARR_PORT=8686"
Environment="ASPNETCORE_URLS=http://0.0.0.0:8686"
TimeoutStopSec=20
KillMode=process
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF
fi

systemctl daemon-reload
systemctl enable extractarr
systemctl restart extractarr

echo "==> [5/5] Verifying Extractarr health check on port 8686..."
for i in {1..15}; do
    if curl -fsS "http://127.0.0.1:8686/api/v3/system/status" >/dev/null 2>&1; then
        LXC_IP=$(hostname -I 2>/dev/null | awk '{print $1}' || echo "localhost")
        echo "=================================================================="
        echo " Extractarr is LIVE and running!"
        echo " Web UI:      http://${LXC_IP}:8686"
        echo " Swagger API: http://${LXC_IP}:8686/swagger"
        echo " Data Dir:    ${DATA_DIR}"
        echo " Service:     systemctl status extractarr"
        echo "=================================================================="
        exit 0
    fi
    sleep 1
done

echo "WARNING: Service started, check logs via: journalctl -u extractarr -n 50 --no-pager"
systemctl status extractarr --no-pager || true


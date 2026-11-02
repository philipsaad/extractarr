#!/usr/bin/env bash
set -e

PUID=${PUID:-1000}
PGID=${PGID:-1000}
UMASK=${UMASK:-002}
DATA_DIR="${EXTRACTARR_DATA_DIR:-${UNPACKARR_DATA_DIR:-/config}}"

echo "----------------------------------------------------------------"
echo " Extractarr v1.0.0.0 (Servarr / LinuxServer.io Container Standard)"
echo " PUID:     ${PUID}"
echo " PGID:     ${PGID}"
echo " UMASK:    ${UMASK}"
echo " DATA_DIR: ${DATA_DIR}"
echo "----------------------------------------------------------------"

groupmod -o -g "${PGID}" extractarr 2>/dev/null || true
usermod -o -u "${PUID}" -g "${PGID}" extractarr 2>/dev/null || true

umask "${UMASK}"

mkdir -p "${DATA_DIR}"
chown -R "${PUID}:${PGID}" "${DATA_DIR}" /app

exec gosu "${PUID}:${PGID}" dotnet /app/Extractarr.Host.dll "$@"


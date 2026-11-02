import { SystemStatus } from '../types/api';

const WINDOWS_DRIVE_REGEX = /^([a-zA-Z]):(?:[\\/](.*))?$/;
const LINUX_DRIVE_MOUNT_REGEX = /^\/(?:mnt\/|host_mnt\/)?([a-zA-Z])(?:\/(.*))?$/;
const UNC_PATH_REGEX = /^(?:\\\\|\/\/)([^\\/]+)[\\/]([^\\/]+)(?:[\\/](.*))?$/;

/**
 * Determines whether the backend host (or fallback client OS) is Windows.
 */
export function isHostWindows(systemStatus?: SystemStatus | null): boolean {
  if (systemStatus && typeof systemStatus.isWindows === 'boolean') {
    return systemStatus.isWindows;
  }
  if (typeof navigator !== 'undefined' && navigator.userAgent) {
    return /Windows/i.test(navigator.userAgent);
  }
  return false;
}

/**
 * Returns the default downloads path for the detected OS.
 */
export function getDefaultLocalDownloadsPath(systemStatus?: SystemStatus | null): string {
  if (systemStatus?.defaultDownloadsPath) {
    return systemStatus.defaultDownloadsPath;
  }
  return isHostWindows(systemStatus) ? 'C:\\Downloads' : '/downloads';
}

/**
 * Translates any Windows or Linux/WSL/Docker path into the native path format
 * for the detected host OS.
 */
export function translatePathForHostOs(rawPath: string, targetWindows: boolean): string {
  if (!rawPath || !rawPath.trim()) {
    return '';
  }

  const trimmed = rawPath.trim();

  if (targetWindows) {
    // 1. UNC path
    const uncMatch = trimmed.match(UNC_PATH_REGEX);
    if (uncMatch) {
      const [, server, share, sub] = uncMatch;
      const cleanSub = (sub || '').replace(/\//g, '\\').replace(/^\\+|\\+$/g, '');
      return cleanSub ? `\\\\${server}\\${share}\\${cleanSub}` : `\\\\${server}\\${share}`;
    }

    // 2. Windows drive path (normalize forward slashes to backslashes)
    const winMatch = trimmed.match(WINDOWS_DRIVE_REGEX);
    if (winMatch) {
      const drive = winMatch[1].toUpperCase();
      const sub = (winMatch[2] || '').replace(/\//g, '\\').replace(/^\\+|\\+$/g, '');
      return sub ? `${drive}:\\${sub}` : `${drive}:\\`;
    }

    // 3. Linux/WSL/Docker drive mount (/mnt/c/..., /host_mnt/c/..., /c/...)
    const normalizedSlashes = trimmed.replace(/\\/g, '/');
    const linuxMountMatch = normalizedSlashes.match(LINUX_DRIVE_MOUNT_REGEX);
    if (linuxMountMatch) {
      const drive = linuxMountMatch[1].toUpperCase();
      const sub = (linuxMountMatch[2] || '').replace(/\//g, '\\').replace(/^\\+|\\+$/g, '');
      return sub ? `${drive}:\\${sub}` : `${drive}:\\`;
    }

    // 4. Rooted Unix path (/downloads/...) on Windows -> C:\downloads\...
    if (normalizedSlashes.startsWith('/')) {
      const rel = normalizedSlashes.replace(/^\/+/, '').replace(/\//g, '\\');
      return rel ? `C:\\${rel}` : 'C:\\';
    }

    return trimmed.replace(/\//g, '\\');
  } else {
    // Target is Linux / macOS
    const winMatch = trimmed.match(WINDOWS_DRIVE_REGEX);
    if (winMatch) {
      const drive = winMatch[1].toLowerCase();
      const sub = (winMatch[2] || '').replace(/\\/g, '/').replace(/^\/+|\/+$/g, '');
      return sub ? `/mnt/${drive}/${sub}` : `/mnt/${drive}`;
    }

    const uncMatch = trimmed.match(UNC_PATH_REGEX);
    if (uncMatch) {
      const [, server, share, sub] = uncMatch;
      const cleanSub = (sub || '').replace(/\\/g, '/').replace(/^\/+|\/+$/g, '');
      return cleanSub ? `/mnt/${server}/${share}/${cleanSub}` : `/mnt/${server}/${share}`;
    }

    const normalized = trimmed.replace(/\\/g, '/').replace(/\/+/g, '/');
    return normalized.length > 1 ? normalized.replace(/\/+$/, '') : normalized;
  }
}


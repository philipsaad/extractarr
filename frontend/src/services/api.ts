import {
  ArrConnectionTestResult,
  ArrInstance,
  BlocklistItem,
  CommandDto,
  DiskSpaceInfo,
  ExtractionConfig,
  ExtractionHistory,
  ExtractionJob,
  HealthCheckResult,
  HostConfig,
  LogEntry,
  LogFileDto,
  NotificationDefinition,
  PagedResponse,
  RemotePathMapping,
  ScheduledTaskDto,
  SystemStatus,
  UiConfig,
  WatchFolder
} from '../types/api';

async function request<T>(url: string, options?: RequestInit): Promise<T> {
  const res = await fetch(url, {
    headers: {
      'Content-Type': 'application/json',
      ...(options?.headers || {})
    },
    ...options
  });
  if (!res.ok) {
    const errText = await res.text();
    throw new Error(errText || `HTTP ${res.status}`);
  }
  if (res.status === 204) {
    return undefined as unknown as T;
  }
  return res.json();
}

export const api = {
  // Instances
  getInstances: () => request<ArrInstance[]>('/api/v3/instance'),
  createInstance: (data: Partial<ArrInstance>) =>
    request<ArrInstance>('/api/v3/instance', { method: 'POST', body: JSON.stringify(data) }),
  updateInstance: (id: number, data: Partial<ArrInstance>) =>
    request<ArrInstance>(`/api/v3/instance/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
  deleteInstance: (id: number) =>
    request<void>(`/api/v3/instance/${id}`, { method: 'DELETE' }),
  testInstance: (data: Partial<ArrInstance>) =>
    request<ArrConnectionTestResult>('/api/v3/instance/test', { method: 'POST', body: JSON.stringify(data) }),
  pollInstance: (id: number) =>
    request<CommandDto>(`/api/v3/instance/${id}/poll`, { method: 'POST' }),

  // Queue
  getQueueDetails: () => request<ExtractionJob[]>('/api/v3/queue/details'),
  cancelQueueJob: (id: number) => request<void>(`/api/v3/queue/${id}`, { method: 'DELETE' }),
  forceExtractJob: (id: number) => request<ExtractionJob>(`/api/v3/queue/${id}/force`, { method: 'POST' }),
  retryQueueJob: (id: number, manualPassword?: string) =>
    request<ExtractionJob>(`/api/v3/queue/${id}/retry`, {
      method: 'POST',
      body: JSON.stringify({ manualPassword })
    }),

  // History
  getHistory: (page = 1, pageSize = 50, eventType = 'all', search = '') =>
    request<PagedResponse<ExtractionHistory>>(
      `/api/v3/history?page=${page}&pageSize=${pageSize}&eventType=${encodeURIComponent(eventType)}&search=${encodeURIComponent(search)}`
    ),
  deleteHistoryItem: (id: number) => request<void>(`/api/v3/history/${id}`, { method: 'DELETE' }),

  // Blocklist
  getBlocklist: (page = 1, pageSize = 50) =>
    request<PagedResponse<BlocklistItem>>(`/api/v3/blocklist?page=${page}&pageSize=${pageSize}`),
  deleteBlocklistItem: (id: number) =>
    request<void>(`/api/v3/blocklist/${id}`, { method: 'DELETE' }),
  retryBlocklistItem: (id: number, manualPassword?: string) =>
    request<{ retried: boolean; job?: ExtractionJob }>(`/api/v3/blocklist/${id}/retry`, {
      method: 'POST',
      body: JSON.stringify({ manualPassword })
    }),

  // Watch Folders
  getWatchFolders: () => request<WatchFolder[]>('/api/v3/watchfolder'),
  createWatchFolder: (data: Partial<WatchFolder>) =>
    request<WatchFolder>('/api/v3/watchfolder', { method: 'POST', body: JSON.stringify(data) }),
  updateWatchFolder: (id: number, data: Partial<WatchFolder>) =>
    request<WatchFolder>(`/api/v3/watchfolder/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
  deleteWatchFolder: (id: number) =>
    request<void>(`/api/v3/watchfolder/${id}`, { method: 'DELETE' }),
  scanWatchFolder: (id: number) =>
    request<CommandDto>(`/api/v3/watchfolder/${id}/scan`, { method: 'POST' }),

  // Remote Path Mappings
  getRemotePathMappings: () => request<RemotePathMapping[]>('/api/v3/remotepathmapping'),
  createRemotePathMapping: (data: Partial<RemotePathMapping>) =>
    request<RemotePathMapping>('/api/v3/remotepathmapping', { method: 'POST', body: JSON.stringify(data) }),
  updateRemotePathMapping: (id: number, data: Partial<RemotePathMapping>) =>
    request<RemotePathMapping>(`/api/v3/remotepathmapping/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
  deleteRemotePathMapping: (id: number) =>
    request<void>(`/api/v3/remotepathmapping/${id}`, { method: 'DELETE' }),

  // Notifications
  getNotifications: () => request<NotificationDefinition[]>('/api/v3/notification'),
  createNotification: (data: Partial<NotificationDefinition>) =>
    request<NotificationDefinition>('/api/v3/notification', { method: 'POST', body: JSON.stringify(data) }),
  updateNotification: (id: number, data: Partial<NotificationDefinition>) =>
    request<NotificationDefinition>(`/api/v3/notification/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
  deleteNotification: (id: number) =>
    request<void>(`/api/v3/notification/${id}`, { method: 'DELETE' }),
  testNotification: (data: Partial<NotificationDefinition>) =>
    request<{ isValid: boolean; message: string }>('/api/v3/notification/test', {
      method: 'POST',
      body: JSON.stringify(data)
    }),

  // Config
  getExtractionConfig: () => request<ExtractionConfig>('/api/v3/config/extraction'),
  saveExtractionConfig: (data: ExtractionConfig) =>
    request<ExtractionConfig>('/api/v3/config/extraction', { method: 'PUT', body: JSON.stringify(data) }),
  getHostConfig: () => request<HostConfig>('/api/v3/config/host'),
  saveHostConfig: (data: HostConfig) =>
    request<HostConfig>('/api/v3/config/host', { method: 'PUT', body: JSON.stringify(data) }),
  resetApiKey: () =>
    request<{ apiKey: string }>('/api/v3/config/host/apikey', { method: 'POST' }),
  getUiConfig: () => request<UiConfig>('/api/v3/config/ui'),
  saveUiConfig: (data: UiConfig) =>
    request<UiConfig>('/api/v3/config/ui', { method: 'PUT', body: JSON.stringify(data) }),

  // System & Commands
  getSystemStatus: () => request<SystemStatus>('/api/v3/system/status'),
  getHealth: () => request<HealthCheckResult[]>('/api/v3/health'),
  getDiskSpace: () => request<DiskSpaceInfo[]>('/api/v3/diskspace'),
  getTasks: () => request<ScheduledTaskDto[]>('/api/v3/system/task'),
  getCommands: () => request<CommandDto[]>('/api/v3/command'),
  runCommand: (name: string, params: Record<string, any> = {}) =>
    request<CommandDto>('/api/v3/command', {
      method: 'POST',
      body: JSON.stringify({ name, ...params })
    }),
  getLogs: (level = 'all', search = '') =>
    request<PagedResponse<LogEntry>>(
      `/api/v3/log?page=1&pageSize=200&level=${encodeURIComponent(level)}&search=${encodeURIComponent(search)}`
    ),
  getLogFiles: () => request<LogFileDto[]>('/api/v3/log/file')
};


export type ArrInstanceType = 'Sonarr' | 'Radarr' | 'Lidarr' | 'Readarr' | 'Whisparr';

export interface ArrInstance {
  id: number;
  name: string;
  enable: boolean;
  instanceType: ArrInstanceType;
  baseUrl: string;
  apiKey: string;
  pollIntervalSeconds: number;
  timeoutSeconds: number;
  verifySsl: boolean;
  monitoredProtocols: string;
  monitoredCategories: string;
  deleteOriginalArchives: boolean;
  syncthingWait: boolean;
  healthStatus: 'Online' | 'Offline' | 'Error' | 'Unknown';
  lastErrorMessage?: string;
  version?: string;
  activeQueueCount: number;
  lastPolledAt?: string;
}

export interface ArrConnectionTestResult {
  isValid: boolean;
  version?: string;
  appName?: string;
  message: string;
}

export type ExtractionStatus =
  | 'Queued'
  | 'Waiting'
  | 'Extracting'
  | 'ExtractedPendingImport'
  | 'ImportedPendingCleanup'
  | 'Completed'
  | 'Failed';

export interface ExtractionJob {
  id: number;
  arrInstanceId?: number;
  watchFolderId?: number;
  instanceName: string;
  instanceType: string;
  externalQueueId: number;
  downloadId: string;
  releaseTitle: string;
  protocol: string;
  category?: string;
  remoteSourcePath: string;
  localSourcePath: string;
  archivePath: string;
  archiveType: string;
  status: ExtractionStatus;
  progressPercent: number;
  speedBytesPerSec: number;
  etaSeconds: number;
  totalBytes: number;
  extractedBytes: number;
  extractedFilesJson: string;
  retryCount: number;
  maxRetries: number;
  manualPassword?: string;
  errorMessage?: string;
  errorStackTrace?: string;
  deleteOriginalArchives: boolean;
  syncthingWait: boolean;
  createdAt: string;
  startedAt?: string;
  extractedAt?: string;
  importedAt?: string;
  cleanupScheduledAt?: string;
  completedAt?: string;
}

export interface ExtractionHistory {
  id: number;
  extractionJobId: number;
  arrInstanceId?: number;
  watchFolderId?: number;
  instanceName: string;
  instanceType: string;
  downloadId: string;
  releaseTitle: string;
  archivePath: string;
  archiveType: string;
  eventType: 'Extracted' | 'Imported' | 'CleanedUp' | 'Failed';
  status: string;
  archiveSizeBytes: number;
  extractedFileCount: number;
  extractedFilesJson: string;
  durationMs: number;
  errorMessage?: string;
  errorStackTrace?: string;
  timestamp: string;
}

export interface BlocklistItem {
  id: number;
  arrInstanceId?: number;
  watchFolderId?: number;
  instanceName: string;
  downloadId: string;
  releaseTitle: string;
  archivePath: string;
  archiveType: string;
  failureCount: number;
  errorMessage?: string;
  errorStackTrace?: string;
  manualPassword?: string;
  createdAt: string;
}

export interface WatchFolder {
  id: number;
  name: string;
  path: string;
  targetPath?: string;
  enable: boolean;
  recursive: boolean;
  deleteOriginalArchives: boolean;
  deleteExtractedFilesDelaySeconds: number;
  syncthingWait: boolean;
  lastScannedAt?: string;
}

export interface RemotePathMapping {
  id: number;
  host: string;
  remotePath: string;
  localPath: string;
}

export interface NotificationDefinition {
  id: number;
  name: string;
  implementation: 'Webhook' | 'Discord' | 'Slack' | 'Telegram' | 'Apprise' | 'Pushover';
  enable: boolean;
  onExtractionStart: boolean;
  onExtractionComplete: boolean;
  onExtractionFailure: boolean;
  onCleanup: boolean;
  settingsJson: string;
}

export interface ExtractionConfig {
  id: number;
  maxConcurrentExtractions: number;
  bufferSizeKb: number;
  tempFolderName: string;
  filePermissions: string;
  folderPermissions: string;
  chownUid: string;
  chownGid: string;
  archivePasswords: string[];
  deleteDelaySeconds: number;
  maxRetries: number;
}

export interface HostConfig {
  id: number;
  bindAddress: string;
  port: number;
  urlBase: string;
  enableSsl: boolean;
  sslPort: number;
  authenticationMethod: string;
  authenticationRequired: string;
  username: string;
  password: string;
  apiKey: string;
  logLevel: string;
  analyticsEnabled: boolean;
  proxyEnabled: boolean;
  proxyHost: string;
  proxyPort: number;
  instanceName: string;
}

export interface UiConfig {
  id: number;
  theme: 'auto' | 'dark' | 'light';
  shortDateFormat: string;
  longDateFormat: string;
  timeFormat: string;
  showRelativeDates: boolean;
  enableColorImpairedMode: boolean;
  firstDayOfWeek: number;
}

export interface SystemStatus {
  appName: string;
  instanceName: string;
  version: string;
  buildTime: string;
  appData: string;
  startupPath: string;
  osName: string;
  osPlatform?: string;
  directorySeparator?: string;
  defaultDownloadsPath?: string;
  isLinux: boolean;
  isOsx: boolean;
  isWindows: boolean;
  isDocker: boolean;
  mode?: string;
  architecture?: string;
  branch: string;
  databaseType: string;
  databaseVersion: string;
  runtimeVersion: string;
  runtimeName: string;
  startTime: string;
}

export interface HealthCheckResult {
  source: string;
  type: 'notice' | 'warning' | 'error';
  message: string;
  wikiUrl: string;
}

export interface DiskSpaceInfo {
  path: string;
  label: string;
  freeSpace: number;
  totalSpace: number;
}

export interface ScheduledTaskDto {
  id: number;
  name: string;
  taskName: string;
  interval: number;
  lastExecution: string;
  lastStartTime: string;
  nextExecution: string;
  lastDuration: string;
}

export interface CommandDto {
  id: number;
  name: string;
  commandName: string;
  message?: string;
  priority: string;
  status: 'queued' | 'started' | 'completed' | 'failed' | 'aborted';
  queued: string;
  started?: string;
  ended?: string;
  duration?: string;
}

export interface LogEntry {
  id: number;
  time: string;
  level: string;
  logger: string;
  message: string;
  exception?: string;
}

export interface LogFileDto {
  id: number;
  filename: string;
  lastWriteTime: string;
  size: number;
  downloadUrl: string;
}

export interface PagedResponse<T> {
  page: number;
  pageSize: number;
  totalRecords: number;
  records: T[];
}

export interface SignalRMessage {
  name: string;
  action: string;
  body?: any;
}


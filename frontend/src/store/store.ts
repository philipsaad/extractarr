import { configureStore, createSlice, PayloadAction } from '@reduxjs/toolkit';
import { TypedUseSelectorHook, useDispatch, useSelector } from 'react-redux';
import {
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
  RemotePathMapping,
  ScheduledTaskDto,
  SystemStatus,
  UiConfig,
  WatchFolder
} from '../types/api';

interface AppState {
  instances: ArrInstance[];
  queue: ExtractionJob[];
  history: ExtractionHistory[];
  historyTotal: number;
  blocklist: BlocklistItem[];
  blocklistTotal: number;
  watchFolders: WatchFolder[];
  remotePathMappings: RemotePathMapping[];
  notifications: NotificationDefinition[];
  extractionConfig: ExtractionConfig | null;
  hostConfig: HostConfig | null;
  uiConfig: UiConfig;
  systemStatus: SystemStatus | null;
  health: HealthCheckResult[];
  diskSpace: DiskSpaceInfo[];
  tasks: ScheduledTaskDto[];
  commands: CommandDto[];
  logs: LogEntry[];
  logFiles: LogFileDto[];
  signalrConnected: boolean;
  globalFilter: string;
}

const initialState: AppState = {
  instances: [],
  queue: [],
  history: [],
  historyTotal: 0,
  blocklist: [],
  blocklistTotal: 0,
  watchFolders: [],
  remotePathMappings: [],
  notifications: [],
  extractionConfig: null,
  hostConfig: null,
  uiConfig: {
    id: 1,
    theme: 'dark',
    shortDateFormat: 'MMM D YYYY',
    longDateFormat: 'dddd, MMMM D YYYY',
    timeFormat: 'HH:mm',
    showRelativeDates: true,
    enableColorImpairedMode: false,
    firstDayOfWeek: 0
  },
  systemStatus: null,
  health: [],
  diskSpace: [],
  tasks: [],
  commands: [],
  logs: [],
  logFiles: [],
  signalrConnected: false,
  globalFilter: ''
};

const appSlice = createSlice({
  name: 'unpackarr',
  initialState,
  reducers: {
    setInstances(state, action: PayloadAction<ArrInstance[]>) {
      state.instances = action.payload;
    },
    upsertInstance(state, action: PayloadAction<ArrInstance>) {
      const idx = state.instances.findIndex(i => i.id === action.payload.id);
      if (idx >= 0) state.instances[idx] = action.payload;
      else state.instances.push(action.payload);
    },
    removeInstance(state, action: PayloadAction<number>) {
      state.instances = state.instances.filter(i => i.id !== action.payload);
    },
    setQueue(state, action: PayloadAction<ExtractionJob[]>) {
      state.queue = action.payload;
    },
    upsertQueueJob(state, action: PayloadAction<ExtractionJob>) {
      const idx = state.queue.findIndex(j => j.id === action.payload.id);
      if (action.payload.status === 'Completed') {
        if (idx >= 0) state.queue.splice(idx, 1);
        return;
      }
      if (idx >= 0) state.queue[idx] = action.payload;
      else state.queue.unshift(action.payload);
    },
    removeQueueJob(state, action: PayloadAction<number>) {
      state.queue = state.queue.filter(j => j.id !== action.payload);
    },
    setHistory(state, action: PayloadAction<{ records: ExtractionHistory[]; totalRecords: number }>) {
      state.history = action.payload.records;
      state.historyTotal = action.payload.totalRecords;
    },
    setBlocklist(state, action: PayloadAction<{ records: BlocklistItem[]; totalRecords: number }>) {
      state.blocklist = action.payload.records;
      state.blocklistTotal = action.payload.totalRecords;
    },
    removeBlocklistItem(state, action: PayloadAction<number>) {
      state.blocklist = state.blocklist.filter(b => b.id !== action.payload);
      state.blocklistTotal = Math.max(0, state.blocklistTotal - 1);
    },
    setWatchFolders(state, action: PayloadAction<WatchFolder[]>) {
      state.watchFolders = action.payload;
    },
    upsertWatchFolder(state, action: PayloadAction<WatchFolder>) {
      const idx = state.watchFolders.findIndex(w => w.id === action.payload.id);
      if (idx >= 0) state.watchFolders[idx] = action.payload;
      else state.watchFolders.push(action.payload);
    },
    removeWatchFolder(state, action: PayloadAction<number>) {
      state.watchFolders = state.watchFolders.filter(w => w.id !== action.payload);
    },
    setRemotePathMappings(state, action: PayloadAction<RemotePathMapping[]>) {
      state.remotePathMappings = action.payload;
    },
    setNotifications(state, action: PayloadAction<NotificationDefinition[]>) {
      state.notifications = action.payload;
    },
    setExtractionConfig(state, action: PayloadAction<ExtractionConfig>) {
      state.extractionConfig = action.payload;
    },
    setHostConfig(state, action: PayloadAction<HostConfig>) {
      state.hostConfig = action.payload;
    },
    setUiConfig(state, action: PayloadAction<UiConfig>) {
      state.uiConfig = action.payload;
    },
    setSystemStatus(state, action: PayloadAction<SystemStatus>) {
      state.systemStatus = action.payload;
    },
    setHealth(state, action: PayloadAction<HealthCheckResult[]>) {
      state.health = action.payload;
    },
    setDiskSpace(state, action: PayloadAction<DiskSpaceInfo[]>) {
      state.diskSpace = action.payload;
    },
    setTasks(state, action: PayloadAction<ScheduledTaskDto[]>) {
      state.tasks = action.payload;
    },
    setCommands(state, action: PayloadAction<CommandDto[]>) {
      state.commands = action.payload;
    },
    upsertCommand(state, action: PayloadAction<CommandDto>) {
      const idx = state.commands.findIndex(c => c.id === action.payload.id);
      if (idx >= 0) state.commands[idx] = action.payload;
      else state.commands.unshift(action.payload);
    },
    setLogs(state, action: PayloadAction<LogEntry[]>) {
      state.logs = action.payload;
    },
    setLogFiles(state, action: PayloadAction<LogFileDto[]>) {
      state.logFiles = action.payload;
    },
    setSignalRConnected(state, action: PayloadAction<boolean>) {
      state.signalrConnected = action.payload;
    },
    setGlobalFilter(state, action: PayloadAction<string>) {
      state.globalFilter = action.payload;
    }
  }
});

export const {
  setInstances,
  upsertInstance,
  removeInstance,
  setQueue,
  upsertQueueJob,
  removeQueueJob,
  setHistory,
  setBlocklist,
  removeBlocklistItem,
  setWatchFolders,
  upsertWatchFolder,
  removeWatchFolder,
  setRemotePathMappings,
  setNotifications,
  setExtractionConfig,
  setHostConfig,
  setUiConfig,
  setSystemStatus,
  setHealth,
  setDiskSpace,
  setTasks,
  setCommands,
  upsertCommand,
  setLogs,
  setLogFiles,
  setSignalRConnected,
  setGlobalFilter
} = appSlice.actions;

export const store = configureStore({
  reducer: {
    unpackarr: appSlice.reducer
  }
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
export const useAppDispatch = () => useDispatch<AppDispatch>();
export const useAppSelector: TypedUseSelectorHook<RootState> = useSelector;


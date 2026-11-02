import React, { useEffect, useState } from 'react';
import {
  Ban,
  Cpu,
  Download,
  FolderSearch,
  Heart,
  History,
  Search,
  Server,
  Settings,
  User,
  Wrench
} from 'lucide-react';
import styles from './App.module.css';
import { useSignalR } from './hooks/useSignalR';
import { api } from './services/api';
import {
  setBlocklist,
  setDiskSpace,
  setExtractionConfig,
  setGlobalFilter,
  setHealth,
  setHistory,
  setHostConfig,
  setInstances,
  setNotifications,
  setQueue,
  setRemotePathMappings,
  setSystemStatus,
  setTasks,
  setUiConfig,
  setWatchFolders,
  useAppDispatch,
  useAppSelector
} from './store/store';
import { InstancesView } from './components/InstancesView';
import { ActivityTab, ActivityView } from './components/ActivityView';
import { WatchFoldersView } from './components/WatchFoldersView';
import { SettingsTab, SettingsView } from './components/SettingsView';
import { SystemTab, SystemView } from './components/SystemView';

type MainSection = 'instances' | 'activity' | 'watchfolders' | 'settings' | 'system';

export const App: React.FC = () => {
  useSignalR();
  const dispatch = useAppDispatch();

  const instances = useAppSelector(s => s.unpackarr.instances);
  const queue = useAppSelector(s => s.unpackarr.queue);
  const blocklist = useAppSelector(s => s.unpackarr.blocklist);
  const health = useAppSelector(s => s.unpackarr.health);
  const uiConfig = useAppSelector(s => s.unpackarr.uiConfig);
  const systemStatus = useAppSelector(s => s.unpackarr.systemStatus);
  const signalrConnected = useAppSelector(s => s.unpackarr.signalrConnected);
  const globalFilter = useAppSelector(s => s.unpackarr.globalFilter);

  const [section, setSection] = useState<MainSection>('instances');
  const [activityTab, setActivityTab] = useState<ActivityTab>('queue');
  const [settingsTab, setSettingsTab] = useState<SettingsTab>('extraction');
  const [systemTab, setSystemTab] = useState<SystemTab>('status');

  useEffect(() => {
    const loadAll = async () => {
      try {
        const [
          inst,
          q,
          hist,
          bl,
          wf,
          rpm,
          notifs,
          extCfg,
          hostCfg,
          uiCfg,
          sysStatus,
          hl,
          ds,
          tsks
        ] = await Promise.all([
          api.getInstances(),
          api.getQueueDetails(),
          api.getHistory(1, 50),
          api.getBlocklist(1, 50),
          api.getWatchFolders(),
          api.getRemotePathMappings(),
          api.getNotifications(),
          api.getExtractionConfig(),
          api.getHostConfig(),
          api.getUiConfig(),
          api.getSystemStatus(),
          api.getHealth(),
          api.getDiskSpace(),
          api.getTasks()
        ]);

        dispatch(setInstances(inst));
        dispatch(setQueue(q));
        dispatch(setHistory({ records: hist.records, totalRecords: hist.totalRecords }));
        dispatch(setBlocklist({ records: bl.records, totalRecords: bl.totalRecords }));
        dispatch(setWatchFolders(wf));
        dispatch(setRemotePathMappings(rpm));
        dispatch(setNotifications(notifs));
        dispatch(setExtractionConfig(extCfg));
        dispatch(setHostConfig(hostCfg));
        dispatch(setUiConfig(uiCfg));
        dispatch(setSystemStatus(sysStatus));
        dispatch(setHealth(hl));
        dispatch(setDiskSpace(ds));
        dispatch(setTasks(tsks));
      } catch (err) {
        console.error('Initial bootstrap load error:', err);
      }
    };

    loadAll();
  }, [dispatch]);

  // Apply Official Sonarr v5 Theme Attributes
  useEffect(() => {
    let resolvedTheme = uiConfig.theme;
    if (resolvedTheme === 'auto') {
      resolvedTheme = window.matchMedia('(prefers-color-scheme: light)').matches
        ? 'light'
        : 'dark';
    }
    const root = document.documentElement;
    root.setAttribute('data-theme', resolvedTheme);
    root.removeAttribute('data-header-color');

    if (uiConfig.enableColorImpairedMode) {
      root.setAttribute('data-color-impaired', 'true');
    } else {
      root.removeAttribute('data-color-impaired');
    }
  }, [uiConfig]);

  return (
    <div className={styles.appShell}>
      {/* SONARR V5 PAGE HEADER */}
      <header className={styles.topHeader}>
        <div className={styles.brandArea}>
          <a
            className={styles.logoLink}
            onClick={e => {
              e.preventDefault();
              setSection('instances');
            }}
            href="#instances"
          >
            <img
              className={styles.brandLogoImg}
              src="/Content/Images/logo.svg"
              alt="Extractarr"
            />
            <span className={styles.brandTitle}>
              {systemStatus?.instanceName || 'Extractarr'}
            </span>
          </a>
        </div>

        <div className={styles.searchWrapper}>
          <div className={styles.searchInputWrapper}>
            <Search size={14} className={styles.searchIcon} />
            <input
              className={styles.searchInput}
              placeholder="Search"
              value={globalFilter}
              onChange={e => dispatch(setGlobalFilter(e.target.value))}
            />
          </div>
        </div>

        <div className={styles.headerRight}>
          <a
            className={styles.donateButton}
            href="https://github.com/philipsaad/extractarr"
            target="_blank"
            rel="noreferrer"
            title="Extractarr on GitHub"
          >
            <Heart size={16} />
          </a>
          <button
            className={styles.headerIconButton}
            onClick={() => {
              setSection('system');
              setSystemTab('status');
            }}
            title={
              signalrConnected
                ? 'System Status (Real-Time SignalR Connected)'
                : 'System Status (SignalR Offline)'
            }
          >
            <Wrench size={16} />
          </button>
          <button
            className={styles.headerIconButton}
            onClick={() => {
              setSection('settings');
              setSettingsTab('general');
            }}
            title="General & User Settings"
          >
            <User size={16} />
          </button>
        </div>
      </header>

      {/* SONARR V5 MAIN + SIDEBAR */}
      <div className={styles.mainLayout}>
        <aside className={styles.sidebar}>
          <nav className={styles.navGroup}>
            <span className={styles.sidebarGroupLabel}>Extraction</span>

            {/* 1. Instances */}
            <div
              className={`${styles.navItem} ${
                section === 'instances' ? styles.navItemActive : ''
              }`}
              onClick={() => setSection('instances')}
            >
              <div className={styles.navItemLeft}>
                <span className={styles.navIconContainer}>
                  <Server size={16} />
                </span>
                <span>Instances</span>
              </div>
              {instances.length > 0 && (
                <span className={styles.navBadge}>{instances.length}</span>
              )}
            </div>

            {/* 2. Watch Folders */}
            <div
              className={`${styles.navItem} ${
                section === 'watchfolders' ? styles.navItemActive : ''
              }`}
              onClick={() => setSection('watchfolders')}
            >
              <div className={styles.navItemLeft}>
                <span className={styles.navIconContainer}>
                  <FolderSearch size={16} />
                </span>
                <span>Watch Folders</span>
              </div>
            </div>

            <span className={styles.sidebarGroupLabel}>Activity</span>

            {/* 3. Persistently Expanded Activity Items (Queue, History, Blocklist) */}
            <div
              className={`${styles.navItem} ${
                section === 'activity' && activityTab === 'queue'
                  ? styles.navItemActive
                  : ''
              }`}
              onClick={() => {
                setSection('activity');
                setActivityTab('queue');
              }}
            >
              <div className={styles.navItemLeft}>
                <span className={styles.navIconContainer}>
                  <Download size={16} />
                </span>
                <span>Queue</span>
              </div>
              {queue.length > 0 && (
                <span className={styles.navBadgeWarn}>{queue.length}</span>
              )}
            </div>

            <div
              className={`${styles.navItem} ${
                section === 'activity' && activityTab === 'history'
                  ? styles.navItemActive
                  : ''
              }`}
              onClick={() => {
                setSection('activity');
                setActivityTab('history');
              }}
            >
              <div className={styles.navItemLeft}>
                <span className={styles.navIconContainer}>
                  <History size={16} />
                </span>
                <span>History</span>
              </div>
            </div>

            <div
              className={`${styles.navItem} ${
                section === 'activity' && activityTab === 'blocklist'
                  ? styles.navItemActive
                  : ''
              }`}
              onClick={() => {
                setSection('activity');
                setActivityTab('blocklist');
              }}
            >
              <div className={styles.navItemLeft}>
                <span className={styles.navIconContainer}>
                  <Ban size={16} />
                </span>
                <span>Blocklist</span>
              </div>
              {blocklist.length > 0 && (
                <span className={styles.navBadgeDanger}>{blocklist.length}</span>
              )}
            </div>

            <div className={styles.sidebarDivider} />

            {/* 4. Settings */}
            <div
              className={`${styles.navItem} ${
                section === 'settings' ? styles.navItemActiveParent : ''
              }`}
              onClick={() => setSection('settings')}
            >
              <div className={styles.navItemLeft}>
                <span className={styles.navIconContainer}>
                  <Settings size={16} />
                </span>
                <span>Settings</span>
              </div>
            </div>

            {section === 'settings' && (
              <div className={styles.subNavList}>
                <div
                  className={`${styles.subNavItem} ${
                    settingsTab === 'extraction' ? styles.subNavItemActive : ''
                  }`}
                  onClick={() => setSettingsTab('extraction')}
                >
                  <span>Extraction</span>
                </div>
                <div
                  className={`${styles.subNavItem} ${
                    settingsTab === 'remotepath' ? styles.subNavItemActive : ''
                  }`}
                  onClick={() => setSettingsTab('remotepath')}
                >
                  <span>Remote Path Mappings</span>
                </div>
                <div
                  className={`${styles.subNavItem} ${
                    settingsTab === 'connect' ? styles.subNavItemActive : ''
                  }`}
                  onClick={() => setSettingsTab('connect')}
                >
                  <span>Connect</span>
                </div>
                <div
                  className={`${styles.subNavItem} ${
                    settingsTab === 'general' ? styles.subNavItemActive : ''
                  }`}
                  onClick={() => setSettingsTab('general')}
                >
                  <span>General</span>
                </div>
                <div
                  className={`${styles.subNavItem} ${
                    settingsTab === 'ui' ? styles.subNavItemActive : ''
                  }`}
                  onClick={() => setSettingsTab('ui')}
                >
                  <span>UI</span>
                </div>
              </div>
            )}

            {/* 5. System */}
            <div
              className={`${styles.navItem} ${
                section === 'system' ? styles.navItemActiveParent : ''
              }`}
              onClick={() => setSection('system')}
            >
              <div className={styles.navItemLeft}>
                <span className={styles.navIconContainer}>
                  <Cpu size={16} />
                </span>
                <span>System</span>
              </div>
              {section !== 'system' && health.length > 0 && (
                <span className={styles.navBadgeWarn}>{health.length}</span>
              )}
            </div>

            {section === 'system' && (
              <div className={styles.subNavList}>
                <div
                  className={`${styles.subNavItem} ${
                    systemTab === 'status' ? styles.subNavItemActive : ''
                  }`}
                  onClick={() => setSystemTab('status')}
                >
                  <span>Status</span>
                  {health.length > 0 && (
                    <span className={styles.navBadgeWarn}>{health.length}</span>
                  )}
                </div>
                <div
                  className={`${styles.subNavItem} ${
                    systemTab === 'tasks' ? styles.subNavItemActive : ''
                  }`}
                  onClick={() => setSystemTab('tasks')}
                >
                  <span>Tasks</span>
                </div>
                <div
                  className={`${styles.subNavItem} ${
                    systemTab === 'events' ? styles.subNavItemActive : ''
                  }`}
                  onClick={() => setSystemTab('events')}
                >
                  <span>Events</span>
                </div>
                <div
                  className={`${styles.subNavItem} ${
                    systemTab === 'logfiles' ? styles.subNavItemActive : ''
                  }`}
                  onClick={() => setSystemTab('logfiles')}
                >
                  <span>Log Files</span>
                </div>
              </div>
            )}
          </nav>
        </aside>

        {/* ACTIVE VIEW CONTAINER */}
        <main className={styles.contentArea}>
          {section === 'instances' && <InstancesView />}
          {section === 'activity' && <ActivityView activeSubTab={activityTab} />}
          {section === 'watchfolders' && <WatchFoldersView />}
          {section === 'settings' && <SettingsView activeSubTab={settingsTab} />}
          {section === 'system' && (
            <SystemView activeSubTab={systemTab} onNavigateTab={setSystemTab} />
          )}
        </main>
      </div>
    </div>
  );
};

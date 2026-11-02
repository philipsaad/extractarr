import React, { useEffect, useState } from 'react';
import {
  AlertCircle,
  BookOpen,
  CheckCircle2,
  Download,
  FileText,
  RefreshCw
} from 'lucide-react';
import styles from '../App.module.css';
import { api } from '../services/api';
import {
  setDiskSpace,
  setHealth,
  setLogFiles,
  setLogs,
  setSystemStatus,
  setTasks,
  useAppDispatch,
  useAppSelector
} from '../store/store';

export type SystemTab = 'status' | 'tasks' | 'events' | 'logfiles';

function formatBytesBinary(bytes: number): string {
  if (!bytes || bytes <= 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KiB', 'MiB', 'GiB', 'TiB', 'PiB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  const val = bytes / Math.pow(k, i);
  return `${val >= 100 ? val.toFixed(0) : val.toFixed(1)} ${sizes[i]}`;
}

function formatUptime(startTimeStr?: string): string {
  if (!startTimeStr) return '00:00:00';
  const diffMs = Math.max(0, Date.now() - new Date(startTimeStr).getTime());
  const totalSec = Math.floor(diffMs / 1000);
  const days = Math.floor(totalSec / 86400);
  const hours = Math.floor((totalSec % 86400) / 3600);
  const mins = Math.floor((totalSec % 3600) / 60);
  const secs = totalSec % 60;
  const hhmmss = [hours, mins, secs].map(n => String(n).padStart(2, '0')).join(':');
  return days > 0 ? `${days}d ${hhmmss}` : hhmmss;
}

export const SystemView: React.FC<{
  activeSubTab: SystemTab;
  onNavigateTab?: (tab: SystemTab) => void;
}> = ({ activeSubTab, onNavigateTab }) => {
  const dispatch = useAppDispatch();
  const status = useAppSelector(s => s.unpackarr.systemStatus);
  const health = useAppSelector(s => s.unpackarr.health);
  const diskSpace = useAppSelector(s => s.unpackarr.diskSpace);
  const tasks = useAppSelector(s => s.unpackarr.tasks);
  const logs = useAppSelector(s => s.unpackarr.logs);
  const logFiles = useAppSelector(s => s.unpackarr.logFiles);

  const [logLevelFilter, setLogLevelFilter] = useState('all');
  const [runningTask, setRunningTask] = useState<string | null>(null);

  const refreshStatus = async () => {
    const [st, hl, ds] = await Promise.all([
      api.getSystemStatus(),
      api.getHealth(),
      api.getDiskSpace()
    ]);
    dispatch(setSystemStatus(st));
    dispatch(setHealth(hl));
    dispatch(setDiskSpace(ds));
  };

  const refreshTasks = async () => {
    const t = await api.getTasks();
    dispatch(setTasks(t));
  };

  const refreshLogs = async () => {
    const [l, lf] = await Promise.all([api.getLogs(logLevelFilter), api.getLogFiles()]);
    dispatch(setLogs(l.records));
    dispatch(setLogFiles(lf));
  };

  useEffect(() => {
    if (activeSubTab === 'status') refreshStatus();
    if (activeSubTab === 'tasks') refreshTasks();
    if (activeSubTab === 'events' || activeSubTab === 'logfiles') refreshLogs();
  }, [activeSubTab, logLevelFilter]);

  const handleRunTask = async (taskName: string) => {
    setRunningTask(taskName);
    try {
      await api.runCommand(taskName);
      setTimeout(refreshTasks, 600);
    } finally {
      setTimeout(() => setRunningTask(null), 600);
    }
  };

  return (
    <>
      {/* Toolbar appears on Tasks, Events, and Log Files (Status has no toolbar, matching Sonarr v5) */}
      {activeSubTab !== 'status' && (
        <div className={styles.pageToolbar}>
          <button
            className={styles.toolbarBtn}
            onClick={() => {
              if (activeSubTab === 'tasks') refreshTasks();
              if (activeSubTab === 'events' || activeSubTab === 'logfiles') refreshLogs();
            }}
          >
            <RefreshCw size={14} />
            <span>Refresh</span>
          </button>

          {activeSubTab === 'events' && (
            <>
              <div className={styles.toolbarSeparator} />
              <select
                className={styles.formSelect}
                style={{ width: 150, height: 32, padding: '4px 10px' }}
                value={logLevelFilter}
                onChange={e => setLogLevelFilter(e.target.value)}
              >
                <option value="all">All Levels</option>
                <option value="Info">Info</option>
                <option value="Warn">Warn</option>
                <option value="Error">Error</option>
                <option value="Debug">Debug</option>
              </select>
            </>
          )}
        </div>
      )}

      <div className={styles.pageBody}>
        {/* STATUS TAB — 1:1 with Sonarr v5 System -> Status */}
        {activeSubTab === 'status' && (
          <>
            <div className={styles.pageHeading}>
              <div className={styles.pageHeadingRow}>
                <h1 className={styles.pageHeadingTitle}>
                  <span className={styles.pageHeadingScope}>System · </span>
                  Status
                </h1>
              </div>
            </div>

            {/* HEALTH SECTION */}
            <div className={styles.fieldSetSection}>
              <div className={styles.sectionHeading}>
                <h2 className={styles.sectionHeadingTitle}>Health</h2>
              </div>
              <div className={styles.fieldSet}>
                {health.length === 0 ? (
                  <div
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: 10,
                      color: 'var(--inkMuted)',
                      fontSize: 13
                    }}
                  >
                    <CheckCircle2 size={16} style={{ color: 'var(--successColor)' }} />
                    <span>No issues with your configuration</span>
                  </div>
                ) : (
                  <div className={styles.tableWrapper}>
                    <table className={styles.dataTable}>
                      <thead>
                        <tr>
                          <th style={{ width: 36 }} />
                          <th>Message</th>
                          <th style={{ width: 80 }}>Actions</th>
                        </tr>
                      </thead>
                      <tbody>
                        {health.map((h, i) => (
                          <tr key={i}>
                            <td>
                              <AlertCircle
                                size={16}
                                style={{
                                  color:
                                    h.type === 'error'
                                      ? 'var(--dangerColor)'
                                      : 'var(--warningColor)',
                                  verticalAlign: 'middle'
                                }}
                              />
                            </td>
                            <td>{h.message}</td>
                            <td>
                              <a
                                href={h.wikiUrl || 'https://github.com/philipsaad/extractarr'}
                                target="_blank"
                                rel="noreferrer"
                                style={{ color: 'var(--accent)', display: 'inline-flex' }}
                                title="Read the Wiki for more information"
                              >
                                <BookOpen size={15} />
                              </a>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}

                <div className={styles.alertInfo}>
                  You can find more information about the cause of these health check messages by
                  clicking the wiki link (book icon) at the end of the row, or by checking your{' '}
                  <a
                    href="#events"
                    onClick={e => {
                      e.preventDefault();
                      onNavigateTab?.('events');
                    }}
                  >
                    logs
                  </a>
                  .
                </div>
              </div>
            </div>

            {/* DISK SPACE SECTION */}
            <div className={styles.fieldSetSection}>
              <div className={styles.sectionHeading}>
                <h2 className={styles.sectionHeadingTitle}>Disk Space</h2>
              </div>
              <div className={styles.fieldSet}>
                <div className={styles.tableWrapper}>
                  <table className={styles.dataTable}>
                    <thead>
                      <tr>
                        <th>Location</th>
                        <th style={{ width: 140 }}>Free Space</th>
                        <th style={{ width: 140 }}>Total Space</th>
                        <th style={{ width: 170 }} />
                      </tr>
                    </thead>
                    <tbody>
                      {diskSpace.map((d, idx) => {
                        const used = Math.max(0, d.totalSpace - d.freeSpace);
                        const pct = d.totalSpace > 0 ? (used / d.totalSpace) * 100 : 0;
                        return (
                          <tr key={idx}>
                            <td>{d.label || d.path}</td>
                            <td>{formatBytesBinary(d.freeSpace)}</td>
                            <td>{formatBytesBinary(d.totalSpace)}</td>
                            <td>
                              <div className={styles.diskProgressContainer}>
                                <div
                                  className={
                                    pct >= 90
                                      ? styles.diskProgressBarDanger
                                      : styles.diskProgressBar
                                  }
                                  style={{ width: `${Math.max(2, pct).toFixed(1)}%` }}
                                />
                              </div>
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
              </div>
            </div>

            {/* ABOUT SECTION */}
            {status && (
              <div className={styles.fieldSetSection}>
                <div className={styles.sectionHeading}>
                  <h2 className={styles.sectionHeadingTitle}>About</h2>
                </div>
                <dl className={styles.descriptionList}>
                  <dt className={styles.descriptionTitle}>Version</dt>
                  <dd className={styles.descriptionValue}>{status.version}</dd>

                  <dt className={styles.descriptionTitle}>.NET</dt>
                  <dd className={styles.descriptionValue}>Yes ({status.runtimeVersion})</dd>

                  <dt className={styles.descriptionTitle}>Database</dt>
                  <dd className={styles.descriptionValue}>SQLite {status.databaseVersion}</dd>

                  <dt className={styles.descriptionTitle}>Database Migration</dt>
                  <dd className={styles.descriptionValue}>1</dd>

                  <dt className={styles.descriptionTitle}>AppData Directory</dt>
                  <dd className={styles.descriptionValue}>{status.appData}</dd>

                  <dt className={styles.descriptionTitle}>Startup Directory</dt>
                  <dd className={styles.descriptionValue}>{status.startupPath}</dd>

                  <dt className={styles.descriptionTitle}>OS Platform</dt>
                  <dd className={styles.descriptionValue}>{status.osName}</dd>

                  <dt className={styles.descriptionTitle}>Mode</dt>
                  <dd className={styles.descriptionValue}>{status.mode || 'Console'}</dd>

                  <dt className={styles.descriptionTitle}>Uptime</dt>
                  <dd className={styles.descriptionValue}>{formatUptime(status.startTime)}</dd>
                </dl>
              </div>
            )}
          </>
        )}

        {/* TASKS TAB */}
        {activeSubTab === 'tasks' && (
          <>
            <div className={styles.pageHeading}>
              <div className={styles.pageHeadingRow}>
                <h1 className={styles.pageHeadingTitle}>
                  <span className={styles.pageHeadingScope}>System · </span>
                  Tasks
                </h1>
              </div>
            </div>

            <div className={styles.fieldSetSection}>
              <div className={styles.sectionHeading}>
                <h2 className={styles.sectionHeadingTitle}>Scheduled</h2>
              </div>
              <div className={styles.fieldSet}>
                <div className={styles.tableWrapper}>
                  <table className={styles.dataTable}>
                    <thead>
                      <tr>
                        <th>Name</th>
                        <th>Interval</th>
                        <th>Last Execution</th>
                        <th>Last Duration</th>
                        <th>Next Execution</th>
                        <th style={{ width: 60 }} />
                      </tr>
                    </thead>
                    <tbody>
                      {tasks.map(t => (
                        <tr key={t.id}>
                          <td>{t.name}</td>
                          <td>{t.interval} minutes</td>
                          <td>{new Date(t.lastExecution).toLocaleString()}</td>
                          <td>{t.lastDuration}</td>
                          <td>{new Date(t.nextExecution).toLocaleString()}</td>
                          <td style={{ textAlign: 'right' }}>
                            <button
                              className={styles.toolbarBtn}
                              onClick={() => handleRunTask(t.taskName)}
                              disabled={runningTask === t.taskName}
                              title="Execute Task Now"
                            >
                              <RefreshCw size={14} />
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          </>
        )}

        {/* EVENTS TAB */}
        {activeSubTab === 'events' && (
          <>
            <div className={styles.pageHeading}>
              <div className={styles.pageHeadingRow}>
                <h1 className={styles.pageHeadingTitle}>
                  <span className={styles.pageHeadingScope}>System · </span>
                  Events
                </h1>
              </div>
            </div>

            <div className={styles.fieldSetSection}>
              <div className={styles.fieldSet}>
                <div className={styles.tableWrapper}>
                  <table className={styles.dataTable}>
                    <thead>
                      <tr>
                        <th style={{ width: 90 }}>Level</th>
                        <th style={{ width: 175 }}>Time</th>
                        <th style={{ width: 210 }}>Component</th>
                        <th>Message</th>
                      </tr>
                    </thead>
                    <tbody>
                      {logs.map(l => (
                        <tr key={l.id}>
                          <td>
                            <span
                              className={`${styles.tag} ${
                                l.level === 'Error' || l.level === 'Fatal'
                                  ? styles.tagDanger
                                  : l.level === 'Warn'
                                  ? styles.tagWarning
                                  : styles.tagCyan
                              }`}
                            >
                              {l.level}
                            </span>
                          </td>
                          <td>{new Date(l.time).toLocaleTimeString()}</td>
                          <td>{l.logger.split('.').pop()}</td>
                          <td>{l.message}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          </>
        )}

        {/* LOG FILES TAB */}
        {activeSubTab === 'logfiles' && (
          <>
            <div className={styles.pageHeading}>
              <div className={styles.pageHeadingRow}>
                <h1 className={styles.pageHeadingTitle}>
                  <span className={styles.pageHeadingScope}>System · </span>
                  Log Files
                </h1>
              </div>
            </div>

            <div className={styles.fieldSetSection}>
              <div className={styles.fieldSet}>
                <div className={styles.tableWrapper}>
                  <table className={styles.dataTable}>
                    <thead>
                      <tr>
                        <th>Filename</th>
                        <th>Size</th>
                        <th>Last Modified</th>
                        <th style={{ width: 100 }} />
                      </tr>
                    </thead>
                    <tbody>
                      {logFiles.map(lf => (
                        <tr key={lf.id}>
                          <td>
                            <FileText
                              size={14}
                              style={{ marginRight: 8, verticalAlign: 'middle' }}
                            />
                            {lf.filename}
                          </td>
                          <td>{formatBytesBinary(lf.size)}</td>
                          <td>{new Date(lf.lastWriteTime).toLocaleString()}</td>
                          <td style={{ textAlign: 'right' }}>
                            <a
                              href={lf.downloadUrl}
                              download={lf.filename}
                              className={styles.toolbarBtn}
                              style={{ textDecoration: 'none' }}
                            >
                              <Download size={13} />
                              <span>Download</span>
                            </a>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          </>
        )}
      </div>
    </>
  );
};

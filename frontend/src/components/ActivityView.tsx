import React, { useEffect, useState } from 'react';
import {
  AlertTriangle,
  Archive,
  CheckCircle,
  Info,
  KeyRound,
  Play,
  RefreshCw,
  RotateCcw,
  Trash2,
  X
} from 'lucide-react';
import styles from '../App.module.css';
import { BlocklistItem, ExtractionHistory, ExtractionJob } from '../types/api';
import { api } from '../services/api';
import {
  removeBlocklistItem,
  removeQueueJob,
  setBlocklist,
  setHistory,
  setQueue,
  upsertQueueJob,
  useAppDispatch,
  useAppSelector
} from '../store/store';

export type ActivityTab = 'queue' | 'history' | 'blocklist';

function formatBytes(bytes: number): string {
  if (!bytes || bytes <= 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${(bytes / Math.pow(k, i)).toFixed(1)} ${sizes[i]}`;
}

function formatEta(seconds: number): string {
  if (!seconds || seconds <= 0) return '--';
  if (seconds < 60) return `${seconds}s`;
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return `${m}m ${s}s`;
}

export const ActivityView: React.FC<{ activeSubTab: ActivityTab }> = ({ activeSubTab }) => {
  const dispatch = useAppDispatch();
  const queue = useAppSelector(s => s.unpackarr.queue);
  const history = useAppSelector(s => s.unpackarr.history);
  const historyTotal = useAppSelector(s => s.unpackarr.historyTotal);
  const blocklist = useAppSelector(s => s.unpackarr.blocklist);
  const globalFilter = useAppSelector(s => s.unpackarr.globalFilter);

  const [historyFilter, setHistoryFilter] = useState('all');
  const [selectedHistory, setSelectedHistory] = useState<ExtractionHistory | null>(null);
  const [passwordModalItem, setPasswordModalItem] = useState<{
    type: 'queue' | 'blocklist';
    id: number;
    title: string;
  } | null>(null);
  const [manualPassword, setManualPassword] = useState('');

  const refreshQueue = async () => {
    const q = await api.getQueueDetails();
    dispatch(setQueue(q));
  };

  const refreshHistory = async (evtFilter = historyFilter) => {
    const h = await api.getHistory(1, 100, evtFilter, globalFilter);
    dispatch(setHistory({ records: h.records, totalRecords: h.totalRecords }));
  };

  const refreshBlocklist = async () => {
    const b = await api.getBlocklist(1, 100);
    dispatch(setBlocklist({ records: b.records, totalRecords: b.totalRecords }));
  };

  useEffect(() => {
    if (activeSubTab === 'queue') refreshQueue();
    if (activeSubTab === 'history') refreshHistory(historyFilter);
    if (activeSubTab === 'blocklist') refreshBlocklist();
  }, [activeSubTab, historyFilter, globalFilter]);

  const handleForceExtract = async (job: ExtractionJob) => {
    const updated = await api.forceExtractJob(job.id);
    dispatch(upsertQueueJob(updated));
  };

  const handleCancelJob = async (jobId: number) => {
    await api.cancelQueueJob(jobId);
    dispatch(removeQueueJob(jobId));
  };

  const handleSubmitPasswordRetry = async () => {
    if (!passwordModalItem) return;
    if (passwordModalItem.type === 'queue') {
      const updated = await api.retryQueueJob(passwordModalItem.id, manualPassword);
      dispatch(upsertQueueJob(updated));
    } else {
      await api.retryBlocklistItem(passwordModalItem.id, manualPassword);
      dispatch(removeBlocklistItem(passwordModalItem.id));
      refreshQueue();
    }
    setPasswordModalItem(null);
    setManualPassword('');
  };

  const filteredQueue = queue.filter(
    j =>
      !globalFilter ||
      j.releaseTitle.toLowerCase().includes(globalFilter.toLowerCase()) ||
      j.instanceName.toLowerCase().includes(globalFilter.toLowerCase())
  );

  const getStatusBadge = (status: string) => {
    switch (status) {
      case 'Extracting':
        return <span className={`${styles.tag} ${styles.tagCyan}`}>Extracting</span>;
      case 'ExtractedPendingImport':
        return <span className={`${styles.tag} ${styles.tagSuccess}`}>Extracted (Pending Import)</span>;
      case 'ImportedPendingCleanup':
        return <span className={`${styles.tag} ${styles.tagWarning}`}>Imported (Pending Cleanup)</span>;
      case 'Waiting':
        return <span className={`${styles.tag} ${styles.tagWarning}`}>Waiting (Lock/Syncthing)</span>;
      case 'Failed':
        return <span className={`${styles.tag} ${styles.tagDanger}`}>Failed</span>;
      default:
        return <span className={styles.tag}>{status}</span>;
    }
  };

  const parseFilesList = (json: string): string[] => {
    try {
      return JSON.parse(json) || [];
    } catch {
      return [];
    }
  };

  return (
    <>
      <div className={styles.pageToolbar}>
        <button
          className={styles.toolbarBtn}
          onClick={() => {
            if (activeSubTab === 'queue') refreshQueue();
            if (activeSubTab === 'history') refreshHistory();
            if (activeSubTab === 'blocklist') refreshBlocklist();
          }}
        >
          <RefreshCw size={14} />
          <span>Refresh</span>
        </button>

        {activeSubTab === 'history' && (
          <>
            <div className={styles.toolbarSeparator} />
            <select
              className={styles.formSelect}
              style={{ width: 170, height: 32, padding: '4px 10px' }}
              value={historyFilter}
              onChange={e => setHistoryFilter(e.target.value)}
            >
              <option value="all">All Events</option>
              <option value="Extracted">Extracted</option>
              <option value="Imported">Imported</option>
              <option value="CleanedUp">Cleaned Up</option>
              <option value="Failed">Failed</option>
            </select>
          </>
        )}
      </div>

      <div className={styles.pageBody}>
        <div className={styles.pageHeading}>
          <div className={styles.pageHeadingRow}>
            <h1 className={styles.pageHeadingTitle}>
              <span className={styles.pageHeadingScope}>Activity · </span>
              {activeSubTab === 'queue'
                ? 'Queue'
                : activeSubTab === 'history'
                ? 'History'
                : 'Blocklist'}
            </h1>
          </div>
        </div>

        {activeSubTab === 'queue' && (
          <div className={styles.fieldSetSection}>
            <div className={styles.fieldSet}>
              <div className={styles.tableWrapper}>
              {filteredQueue.length === 0 ? (
                <div className={styles.emptyState}>
                  <Archive size={36} style={{ marginBottom: 8, opacity: 0.5 }} />
                  <div>Queue is empty. Monitoring configured *arr instances and Watch Folders.</div>
                </div>
              ) : (
                <table className={styles.dataTable}>
                  <thead>
                    <tr>
                      <th>Source</th>
                      <th>Release Title</th>
                      <th>Archive</th>
                      <th>Status</th>
                      <th style={{ width: 210 }}>Progress</th>
                      <th>Speed</th>
                      <th>ETA</th>
                      <th style={{ textAlign: 'right' }}>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredQueue.map(job => (
                      <tr key={job.id}>
                        <td>
                          <span className={`${styles.tag} ${styles.tagCyan}`}>{job.instanceName}</span>
                        </td>
                        <td>
                          <div style={{ fontWeight: 600 }}>{job.releaseTitle}</div>
                          <div style={{ fontSize: 11, color: 'var(--inkMuted)' }}>
                            {job.archivePath}
                          </div>
                          {job.errorMessage && (
                            <div style={{ fontSize: 11, color: 'var(--dangerColor)', marginTop: 2 }}>
                              {job.errorMessage}
                            </div>
                          )}
                        </td>
                        <td>
                          <span className={styles.tag}>{job.archiveType.toUpperCase()}</span>
                          <div style={{ fontSize: 11, color: 'var(--inkMuted)', marginTop: 2 }}>
                            {formatBytes(job.totalBytes)}
                          </div>
                        </td>
                        <td>{getStatusBadge(job.status)}</td>
                        <td>
                          <div className={styles.progressTrack}>
                            <div
                              className={`${styles.progressFill} progress-fill-striped`}
                              style={{ width: `${Math.min(100, Math.max(0, job.progressPercent))}%` }}
                            />
                            <div className={styles.progressLabel}>
                              {job.progressPercent.toFixed(1)}%
                            </div>
                          </div>
                        </td>
                        <td>
                          {job.speedBytesPerSec > 0
                            ? `${formatBytes(job.speedBytesPerSec)}/s`
                            : '--'}
                        </td>
                        <td>{formatEta(job.etaSeconds)}</td>
                        <td style={{ textAlign: 'right' }}>
                          <div style={{ display: 'inline-flex', gap: 6 }}>
                            <button
                              className={styles.toolbarBtn}
                              title="Force Extract Now"
                              onClick={() => handleForceExtract(job)}
                            >
                              <Play size={13} />
                            </button>
                            <button
                              className={styles.toolbarBtn}
                              title="Retry / Supply Password"
                              onClick={() => {
                                setManualPassword(job.manualPassword || '');
                                setPasswordModalItem({
                                  type: 'queue',
                                  id: job.id,
                                  title: job.releaseTitle
                                });
                              }}
                            >
                              <RotateCcw size={13} />
                            </button>
                            <button
                              className={`${styles.toolbarBtn} ${styles.toolbarBtnDanger}`}
                              title="Cancel / Remove from Queue"
                              onClick={() => handleCancelJob(job.id)}
                            >
                              <Trash2 size={13} />
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
              </div>
            </div>
          </div>
        )}

        {activeSubTab === 'history' && (
          <div className={styles.fieldSetSection}>
            <div className={styles.fieldSet}>
              <div className={styles.tableWrapper}>
              {history.length === 0 ? (
                <div className={styles.emptyState}>No extraction history records found.</div>
              ) : (
                <table className={styles.dataTable}>
                  <thead>
                    <tr>
                      <th>Event</th>
                      <th>Source</th>
                      <th>Release Title</th>
                      <th>Archive Type</th>
                      <th>Size</th>
                      <th>Files</th>
                      <th>Duration</th>
                      <th>Date</th>
                      <th style={{ textAlign: 'right' }}>Details</th>
                    </tr>
                  </thead>
                  <tbody>
                    {history.map(item => (
                      <tr key={item.id}>
                        <td>
                          {item.eventType === 'Extracted' && (
                            <span className={`${styles.tag} ${styles.tagCyan}`}>
                              <CheckCircle size={11} style={{ marginRight: 4 }} />
                              Extracted
                            </span>
                          )}
                          {item.eventType === 'Imported' && (
                            <span className={`${styles.tag} ${styles.tagSuccess}`}>Imported</span>
                          )}
                          {item.eventType === 'CleanedUp' && (
                            <span className={`${styles.tag} ${styles.tagWarning}`}>Cleaned Up</span>
                          )}
                          {item.eventType === 'Failed' && (
                            <span className={`${styles.tag} ${styles.tagDanger}`}>
                              <AlertTriangle size={11} style={{ marginRight: 4 }} />
                              Failed
                            </span>
                          )}
                        </td>
                        <td>
                          <span className={styles.tag}>{item.instanceName}</span>
                        </td>
                        <td style={{ fontWeight: 500 }}>{item.releaseTitle}</td>
                        <td>
                          <span className={styles.tag}>{item.archiveType.toUpperCase()}</span>
                        </td>
                        <td>{formatBytes(item.archiveSizeBytes)}</td>
                        <td>{item.extractedFileCount}</td>
                        <td>{item.durationMs > 0 ? `${(item.durationMs / 1000).toFixed(2)}s` : '--'}</td>
                        <td style={{ color: 'var(--inkMuted)', fontSize: 12 }}>
                          {new Date(item.timestamp).toLocaleString()}
                        </td>
                        <td style={{ textAlign: 'right' }}>
                          <button
                            className={styles.toolbarBtn}
                            onClick={() => setSelectedHistory(item)}
                          >
                            <Info size={13} />
                            <span>Info</span>
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
              </div>
            </div>
          </div>
        )}

        {activeSubTab === 'blocklist' && (
          <div className={styles.fieldSetSection}>
            <div className={styles.fieldSet}>
              <div className={styles.tableWrapper}>
              {blocklist.length === 0 ? (
                <div className={styles.emptyState}>
                  No blocklisted or ignored archives. Failed archives exceeding max retries appear here.
                </div>
              ) : (
                <table className={styles.dataTable}>
                  <thead>
                    <tr>
                      <th>Source</th>
                      <th>Release Title</th>
                      <th>Archive Path</th>
                      <th>Failures</th>
                      <th>Reason</th>
                      <th>Date</th>
                      <th style={{ textAlign: 'right' }}>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {blocklist.map((b: BlocklistItem) => (
                      <tr key={b.id}>
                        <td>
                          <span className={styles.tag}>{b.instanceName}</span>
                        </td>
                        <td style={{ fontWeight: 600 }}>{b.releaseTitle}</td>
                        <td style={{ fontSize: 12, color: 'var(--inkMuted)' }}>
                          {b.archivePath}
                        </td>
                        <td>
                          <span className={`${styles.tag} ${styles.tagDanger}`}>
                            {b.failureCount}x
                          </span>
                        </td>
                        <td style={{ color: 'var(--dangerColor)', fontSize: 12 }}>
                          {b.errorMessage || 'Extraction failed'}
                        </td>
                        <td style={{ fontSize: 12, color: 'var(--inkMuted)' }}>
                          {new Date(b.createdAt).toLocaleString()}
                        </td>
                        <td style={{ textAlign: 'right' }}>
                          <div style={{ display: 'inline-flex', gap: 6 }}>
                            <button
                              className={styles.toolbarBtn}
                              onClick={() => {
                                setManualPassword(b.manualPassword || '');
                                setPasswordModalItem({
                                  type: 'blocklist',
                                  id: b.id,
                                  title: b.releaseTitle
                                });
                              }}
                            >
                              <KeyRound size={13} />
                              <span>Retry with Password</span>
                            </button>
                            <button
                              className={`${styles.toolbarBtn} ${styles.toolbarBtnDanger}`}
                              onClick={async () => {
                                await api.deleteBlocklistItem(b.id);
                                dispatch(removeBlocklistItem(b.id));
                              }}
                            >
                              <Trash2 size={13} />
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
              </div>
            </div>
          </div>
        )}
      </div>

      {/* History Info Modal */}
      {selectedHistory && (
        <div className={styles.modalBackdrop} onClick={() => setSelectedHistory(null)}>
          <div className={styles.modalDialog} onClick={e => e.stopPropagation()}>
            <div className={styles.modalHeader}>
              <span>History Details — {selectedHistory.releaseTitle}</span>
              <button className={styles.toolbarBtn} onClick={() => setSelectedHistory(null)}>
                <X size={15} />
              </button>
            </div>
            <div className={styles.modalBody}>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Event Type</div>
                <div>{selectedHistory.eventType}</div>
              </div>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Source Instance</div>
                <div>
                  {selectedHistory.instanceName} ({selectedHistory.instanceType})
                </div>
              </div>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Archive Path</div>
                <div style={{ wordBreak: 'break-all', fontSize: 12 }}>
                  {selectedHistory.archivePath}
                </div>
              </div>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Archive Size & Duration</div>
                <div>
                  {formatBytes(selectedHistory.archiveSizeBytes)} &bull;{' '}
                  {(selectedHistory.durationMs / 1000).toFixed(2)}s
                </div>
              </div>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>
                  Extracted Files ({selectedHistory.extractedFileCount})
                </div>
                <div
                  style={{
                    background: 'var(--inputBackgroundColor)',
                    padding: 10,
                    borderRadius: 4,
                    border: '1px solid var(--borderColor)',
                    fontSize: 12,
                    fontFamily: 'monospace',
                    maxHeight: 180,
                    overflowY: 'auto'
                  }}
                >
                  {parseFilesList(selectedHistory.extractedFilesJson).length === 0 ? (
                    <span>No files recorded</span>
                  ) : (
                    parseFilesList(selectedHistory.extractedFilesJson).map((f, idx) => (
                      <div key={idx}>{f}</div>
                    ))
                  )}
                </div>
              </div>
              {selectedHistory.errorMessage && (
                <div className={styles.formGroup}>
                  <div className={styles.formLabel}>Error / Stack Trace</div>
                  <pre
                    style={{
                      background: 'var(--inputBackgroundColor)',
                      padding: 10,
                      borderRadius: 4,
                      color: 'var(--dangerColor)',
                      fontSize: 11,
                      overflowX: 'auto'
                    }}
                  >
                    {selectedHistory.errorStackTrace || selectedHistory.errorMessage}
                  </pre>
                </div>
              )}
            </div>
            <div className={styles.modalFooter}>
              <div />
              <button className={styles.toolbarBtn} onClick={() => setSelectedHistory(null)}>
                Close
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Manual Password & Retry Modal */}
      {passwordModalItem && (
        <div className={styles.modalBackdrop} onClick={() => setPasswordModalItem(null)}>
          <div className={styles.modalDialog} onClick={e => e.stopPropagation()}>
            <div className={styles.modalHeader}>
              <span>Retry Extraction — {passwordModalItem.title}</span>
              <button className={styles.toolbarBtn} onClick={() => setPasswordModalItem(null)}>
                <X size={15} />
              </button>
            </div>
            <div className={styles.modalBody}>
              <div className={styles.formGroup}>
                <div>
                  <div className={styles.formLabel}>Manual Archive Password</div>
                  <div className={styles.formHelp}>
                    Optional password for encrypted RAR/7z/ZIP archives (leave blank to try global password list)
                  </div>
                </div>
                <input
                  className={styles.formInput}
                  type="text"
                  value={manualPassword}
                  onChange={e => setManualPassword(e.target.value)}
                  placeholder="Enter archive password..."
                />
              </div>
            </div>
            <div className={styles.modalFooter}>
              <div />
              <div style={{ display: 'flex', gap: 8 }}>
                <button className={styles.toolbarBtn} onClick={() => setPasswordModalItem(null)}>
                  Cancel
                </button>
                <button
                  className={`${styles.toolbarBtn} ${styles.toolbarBtnPrimary}`}
                  onClick={handleSubmitPasswordRetry}
                >
                  <RotateCcw size={14} />
                  <span>Retry Extraction Now</span>
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
};


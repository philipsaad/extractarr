import React, { useState } from 'react';
import { Plus, RefreshCw, Trash2, X } from 'lucide-react';
import styles from '../App.module.css';
import { WatchFolder } from '../types/api';
import { api } from '../services/api';
import {
  removeWatchFolder,
  upsertWatchFolder,
  useAppDispatch,
  useAppSelector
} from '../store/store';
import {
  getDefaultLocalDownloadsPath,
  isHostWindows,
  translatePathForHostOs
} from '../utils/pathTranslator';

export const WatchFoldersView: React.FC = () => {
  const dispatch = useAppDispatch();
  const watchFolders = useAppSelector(s => s.unpackarr.watchFolders);
  const systemStatus = useAppSelector(s => s.unpackarr.systemStatus);
  const hostIsWindows = isHostWindows(systemStatus);
  const [editing, setEditing] = useState<Partial<WatchFolder> | null>(null);

  const openAdd = () => {
    const baseDownloads = getDefaultLocalDownloadsPath(systemStatus);
    const defaultManualPath = translatePathForHostOs(
      `${baseDownloads}/manual`,
      hostIsWindows
    );
    setEditing({
      id: 0,
      name: 'Downloads Watch Folder',
      path: defaultManualPath,
      targetPath: '',
      enable: true,
      recursive: true,
      deleteOriginalArchives: false,
      deleteExtractedFilesDelaySeconds: 0,
      syncthingWait: true
    });
  };

  const handleSave = async () => {
    if (!editing || !editing.name || !editing.path) return;
    const payload: Partial<WatchFolder> = {
      ...editing,
      path: translatePathForHostOs(editing.path, hostIsWindows),
      targetPath: editing.targetPath
        ? translatePathForHostOs(editing.targetPath, hostIsWindows)
        : ''
    };
    if (payload.id && payload.id > 0) {
      const updated = await api.updateWatchFolder(payload.id, payload);
      dispatch(upsertWatchFolder(updated));
    } else {
      const created = await api.createWatchFolder(payload);
      dispatch(upsertWatchFolder(created));
    }
    setEditing(null);
  };

  const handleDelete = async (id: number) => {
    await api.deleteWatchFolder(id);
    dispatch(removeWatchFolder(id));
    setEditing(null);
  };

  return (
    <>
      <div className={styles.pageToolbar}>
        <button className={styles.toolbarBtn} onClick={() => api.runCommand('ScanWatchFolders')}>
          <RefreshCw size={14} />
          <span>Scan All</span>
        </button>
        <div className={styles.toolbarSeparator} />
        <button className={styles.toolbarBtn} onClick={openAdd}>
          <Plus size={14} />
          <span>Add Watch Folder</span>
        </button>
      </div>

      <div className={styles.pageBody}>
        <div className={styles.pageHeading}>
          <div className={styles.pageHeadingRow}>
            <h1 className={styles.pageHeadingTitle}>
              <span className={styles.pageHeadingScope}>Extraction · </span>
              Watch Folders
            </h1>
          </div>
        </div>

        <div className={styles.fieldSetSection}>
          <div className={styles.sectionHeading}>
            <h2 className={styles.sectionHeadingTitle}>Watch Folders</h2>
          </div>
          <div className={styles.fieldSet}>
            <div className={styles.tableWrapper}>
              {watchFolders.length === 0 ? (
                <div className={styles.emptyState}>
                  No standalone Watch Folders configured. Add a local directory to automatically unpack archives independent of *arr apps.
                </div>
              ) : (
                <table className={styles.dataTable}>
                  <thead>
                    <tr>
                      <th>Name</th>
                      <th>Watched Path</th>
                      <th>Target Output Path</th>
                      <th>Options</th>
                      <th>Last Scanned</th>
                      <th style={{ textAlign: 'right' }}>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {watchFolders.map(wf => (
                      <tr key={wf.id}>
                        <td>{wf.name}</td>
                        <td style={{ fontFamily: 'monospace', fontSize: 12 }}>{wf.path}</td>
                        <td style={{ fontFamily: 'monospace', fontSize: 12, color: 'var(--inkMuted)' }}>
                          {wf.targetPath || '(Extract In-Place)'}
                        </td>
                        <td>
                          <div className={styles.tagRow}>
                            <span className={`${styles.tag} ${wf.enable ? styles.tagSuccess : styles.tagWarning}`}>
                              {wf.enable ? 'Enabled' : 'Disabled'}
                            </span>
                            {wf.recursive && <span className={styles.tag}>Recursive</span>}
                            {wf.syncthingWait && <span className={styles.tag}>Syncthing Wait</span>}
                            {wf.deleteOriginalArchives && (
                              <span className={`${styles.tag} ${styles.tagDanger}`}>Delete Source</span>
                            )}
                          </div>
                        </td>
                        <td style={{ fontSize: 12, color: 'var(--inkMuted)' }}>
                          {wf.lastScannedAt ? new Date(wf.lastScannedAt).toLocaleString() : 'Never'}
                        </td>
                        <td style={{ textAlign: 'right' }}>
                          <div style={{ display: 'inline-flex', gap: 6 }}>
                            <button
                              className={styles.toolbarBtn}
                              onClick={() => api.scanWatchFolder(wf.id)}
                            >
                              <RefreshCw size={13} />
                              <span>Scan</span>
                            </button>
                            <button className={styles.toolbarBtn} onClick={() => setEditing({ ...wf })}>
                              Edit
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
      </div>

      {editing && (
        <div className={styles.modalBackdrop} onClick={() => setEditing(null)}>
          <div className={styles.modalDialog} onClick={e => e.stopPropagation()}>
            <div className={styles.modalHeader}>
              <span>{editing.id ? 'Edit Watch Folder' : 'Add Watch Folder'}</span>
              <button className={styles.toolbarBtn} onClick={() => setEditing(null)}>
                <X size={15} />
              </button>
            </div>
            <div className={styles.modalBody}>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Name</div>
                <input
                  className={styles.formInput}
                  value={editing.name || ''}
                  onChange={e => setEditing({ ...editing, name: e.target.value })}
                />
              </div>
              <div className={styles.formGroup}>
                <div>
                  <div className={styles.formLabel}>Watched Directory Path</div>
                  <div className={styles.formHelp}>
                    Local folder monitored via FileSystemWatcher + polling (auto-translates Windows/Linux paths for {hostIsWindows ? 'Windows' : 'Linux'})
                  </div>
                </div>
                <input
                  className={styles.formInput}
                  value={editing.path || ''}
                  onChange={e => setEditing({ ...editing, path: e.target.value })}
                  onBlur={e =>
                    setEditing({
                      ...editing,
                      path: translatePathForHostOs(e.target.value, hostIsWindows)
                    })
                  }
                />
              </div>
              <div className={styles.formGroup}>
                <div>
                  <div className={styles.formLabel}>Target Output Path</div>
                  <div className={styles.formHelp}>Leave blank to extract in-place next to the archive</div>
                </div>
                <input
                  className={styles.formInput}
                  value={editing.targetPath || ''}
                  onChange={e => setEditing({ ...editing, targetPath: e.target.value })}
                  onBlur={e =>
                    setEditing({
                      ...editing,
                      targetPath: e.target.value
                        ? translatePathForHostOs(e.target.value, hostIsWindows)
                        : ''
                    })
                  }
                  placeholder="(Optional) Extract in-place if empty"
                />
              </div>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Enable</div>
                <label className={styles.checkboxLabel}>
                  <input
                    type="checkbox"
                    checked={!!editing.enable}
                    onChange={e => setEditing({ ...editing, enable: e.target.checked })}
                  />
                  <span>Actively watch this folder</span>
                </label>
              </div>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Recursive</div>
                <label className={styles.checkboxLabel}>
                  <input
                    type="checkbox"
                    checked={!!editing.recursive}
                    onChange={e => setEditing({ ...editing, recursive: e.target.checked })}
                  />
                  <span>Monitor subdirectories recursively</span>
                </label>
              </div>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Syncthing Wait</div>
                <label className={styles.checkboxLabel}>
                  <input
                    type="checkbox"
                    checked={!!editing.syncthingWait}
                    onChange={e => setEditing({ ...editing, syncthingWait: e.target.checked })}
                  />
                  <span>Wait for .tmp files to finish syncing</span>
                </label>
              </div>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Delete Original Archives</div>
                <label className={styles.checkboxLabel}>
                  <input
                    type="checkbox"
                    checked={!!editing.deleteOriginalArchives}
                    onChange={e =>
                      setEditing({ ...editing, deleteOriginalArchives: e.target.checked })
                    }
                  />
                  <span>Delete source .rar/.zip after successful extraction</span>
                </label>
              </div>
              <div className={styles.formGroup}>
                <div>
                  <div className={styles.formLabel}>Delete Extracted Files Delay (s)</div>
                  <div className={styles.formHelp}>0 = keep extracted files permanently</div>
                </div>
                <input
                  type="number"
                  className={styles.formInput}
                  value={editing.deleteExtractedFilesDelaySeconds ?? 0}
                  onChange={e =>
                    setEditing({
                      ...editing,
                      deleteExtractedFilesDelaySeconds: Number(e.target.value)
                    })
                  }
                />
              </div>
            </div>
            <div className={styles.modalFooter}>
              <div>
                {editing.id ? (
                  <button
                    className={`${styles.toolbarBtn} ${styles.toolbarBtnDanger}`}
                    onClick={() => handleDelete(editing.id!)}
                  >
                    <Trash2 size={14} />
                    <span>Delete</span>
                  </button>
                ) : null}
              </div>
              <div style={{ display: 'flex', gap: 8 }}>
                <button className={styles.toolbarBtn} onClick={() => setEditing(null)}>
                  Cancel
                </button>
                <button
                  className={`${styles.toolbarBtn} ${styles.toolbarBtnPrimary}`}
                  onClick={handleSave}
                >
                  Save
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
};


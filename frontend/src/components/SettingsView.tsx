import React, { useEffect, useState } from 'react';
import {
  Check,
  Copy,
  Key,
  Plus,
  Save,
  Trash2,
  Wifi,
  X
} from 'lucide-react';
import styles from '../App.module.css';
import {
  ExtractionConfig,
  HostConfig,
  NotificationDefinition,
  RemotePathMapping,
  UiConfig
} from '../types/api';
import { api } from '../services/api';
import {
  setExtractionConfig,
  setHostConfig,
  setNotifications,
  setRemotePathMappings,
  setUiConfig,
  useAppDispatch,
  useAppSelector
} from '../store/store';
import {
  getDefaultLocalDownloadsPath,
  isHostWindows,
  translatePathForHostOs
} from '../utils/pathTranslator';

export type SettingsTab = 'extraction' | 'remotepath' | 'connect' | 'general' | 'ui';

const NOTIFICATION_PROVIDERS: NotificationDefinition['implementation'][] = [
  'Webhook',
  'Discord',
  'Slack',
  'Telegram',
  'Apprise',
  'Pushover'
];

export const SettingsView: React.FC<{ activeSubTab: SettingsTab }> = ({ activeSubTab }) => {
  const dispatch = useAppDispatch();
  const extCfgStore = useAppSelector(s => s.unpackarr.extractionConfig);
  const hostCfgStore = useAppSelector(s => s.unpackarr.hostConfig);
  const uiCfgStore = useAppSelector(s => s.unpackarr.uiConfig);
  const mappings = useAppSelector(s => s.unpackarr.remotePathMappings);
  const notifications = useAppSelector(s => s.unpackarr.notifications);
  const systemStatus = useAppSelector(s => s.unpackarr.systemStatus);
  const hostIsWindows = isHostWindows(systemStatus);

  const [extForm, setExtForm] = useState<ExtractionConfig | null>(extCfgStore);
  const [passwordsText, setPasswordsText] = useState('');
  const [hostForm, setHostForm] = useState<HostConfig | null>(hostCfgStore);
  const [uiForm, setUiForm] = useState<UiConfig>(uiCfgStore);
  const [saveBanner, setSaveBanner] = useState<string | null>(null);

  // Remote Path Mapping Modal
  const [editingMapping, setEditingMapping] = useState<Partial<RemotePathMapping> | null>(null);

  // Notification Modal
  const [editingNotif, setEditingNotif] = useState<Partial<NotificationDefinition> | null>(null);
  const [notifSettingsObj, setNotifSettingsObj] = useState<Record<string, string>>({});
  const [notifTestMsg, setNotifTestMsg] = useState<{ isValid: boolean; message: string } | null>(null);

  useEffect(() => {
    if (extCfgStore) {
      setExtForm(extCfgStore);
      setPasswordsText((extCfgStore.archivePasswords || []).join('\n'));
    }
  }, [extCfgStore]);

  useEffect(() => {
    if (hostCfgStore) setHostForm(hostCfgStore);
  }, [hostCfgStore]);

  useEffect(() => {
    setUiForm(uiCfgStore);
  }, [uiCfgStore]);

  const showSaved = (msg = 'Settings saved successfully') => {
    setSaveBanner(msg);
    setTimeout(() => setSaveBanner(null), 2500);
  };

  const handleSaveCurrentTab = async () => {
    if (activeSubTab === 'extraction' && extForm) {
      const payload: ExtractionConfig = {
        ...extForm,
        archivePasswords: passwordsText
          .split(/\r?\n/)
          .map(s => s.trim())
          .filter(Boolean)
      };
      const saved = await api.saveExtractionConfig(payload);
      dispatch(setExtractionConfig(saved));
      showSaved('Extraction settings saved');
    } else if (activeSubTab === 'general' && hostForm) {
      const saved = await api.saveHostConfig(hostForm);
      dispatch(setHostConfig(saved));
      showSaved('General host settings saved');
    } else if (activeSubTab === 'ui') {
      const saved = await api.saveUiConfig(uiForm);
      dispatch(setUiConfig(saved));
      showSaved('UI settings saved');
    }
  };

  const handleSaveMapping = async () => {
    if (!editingMapping?.host || !editingMapping?.remotePath || !editingMapping?.localPath) return;
    const payload: Partial<RemotePathMapping> = {
      ...editingMapping,
      localPath: translatePathForHostOs(editingMapping.localPath, hostIsWindows)
    };
    if (payload.id && payload.id > 0) {
      await api.updateRemotePathMapping(payload.id, payload);
    } else {
      await api.createRemotePathMapping(payload);
    }
    const list = await api.getRemotePathMappings();
    dispatch(setRemotePathMappings(list));
    setEditingMapping(null);
  };

  const handleDeleteMapping = async (id: number) => {
    await api.deleteRemotePathMapping(id);
    const list = await api.getRemotePathMappings();
    dispatch(setRemotePathMappings(list));
    setEditingMapping(null);
  };

  const openAddNotification = (impl: NotificationDefinition['implementation']) => {
    setNotifTestMsg(null);
    setNotifSettingsObj({ url: '', webhookUrl: '', username: 'Extractarr' });
    setEditingNotif({
      id: 0,
      name: `${impl} Notification`,
      implementation: impl,
      enable: true,
      onExtractionStart: false,
      onExtractionComplete: true,
      onExtractionFailure: true,
      onCleanup: false,
      settingsJson: '{}'
    });
  };

  const openEditNotification = (n: NotificationDefinition) => {
    setNotifTestMsg(null);
    try {
      setNotifSettingsObj(JSON.parse(n.settingsJson || '{}'));
    } catch {
      setNotifSettingsObj({});
    }
    setEditingNotif({ ...n });
  };

  const handleTestNotification = async () => {
    if (!editingNotif) return;
    const payload = {
      ...editingNotif,
      settingsJson: JSON.stringify(notifSettingsObj)
    };
    const res = await api.testNotification(payload);
    setNotifTestMsg(res);
  };

  const handleSaveNotification = async () => {
    if (!editingNotif || !editingNotif.name) return;
    const payload = {
      ...editingNotif,
      settingsJson: JSON.stringify(notifSettingsObj)
    };
    if (editingNotif.id && editingNotif.id > 0) {
      await api.updateNotification(editingNotif.id, payload);
    } else {
      await api.createNotification(payload);
    }
    const list = await api.getNotifications();
    dispatch(setNotifications(list));
    setEditingNotif(null);
  };

  return (
    <>
      <div className={styles.pageToolbar}>
        {(activeSubTab === 'extraction' || activeSubTab === 'general' || activeSubTab === 'ui') && (
          <button className={styles.toolbarBtn} onClick={handleSaveCurrentTab}>
            <Save size={14} />
            <span>Save Changes</span>
          </button>
        )}
        {activeSubTab === 'remotepath' && (
          <button
            className={styles.toolbarBtn}
            onClick={() =>
              setEditingMapping({
                id: 0,
                host: '*',
                remotePath: '/data/torrents',
                localPath: getDefaultLocalDownloadsPath(systemStatus)
              })
            }
          >
            <Plus size={14} />
            <span>Add Mapping</span>
          </button>
        )}
        {saveBanner && (
          <>
            <div className={styles.toolbarSeparator} />
            <span className={`${styles.tag} ${styles.tagSuccess}`}>
              <Check size={12} style={{ marginRight: 4 }} />
              {saveBanner}
            </span>
          </>
        )}
      </div>

      <div className={styles.pageBody}>
        <div className={styles.pageHeading}>
          <div className={styles.pageHeadingRow}>
            <h1 className={styles.pageHeadingTitle}>
              <span className={styles.pageHeadingScope}>Settings · </span>
              {activeSubTab === 'extraction'
                ? 'Extraction'
                : activeSubTab === 'remotepath'
                ? 'Remote Path Mappings'
                : activeSubTab === 'connect'
                ? 'Connect'
                : activeSubTab === 'general'
                ? 'General'
                : 'UI'}
            </h1>
          </div>
        </div>

        {/* EXTRACTION SETTINGS */}
        {activeSubTab === 'extraction' && extForm && (
          <div className={styles.fieldSetSection}>
            <div className={styles.sectionHeading}>
              <h2 className={styles.sectionHeadingTitle}>Archive Engine & Lifecycle Rules</h2>
            </div>
            <div className={styles.fieldSet}>
              <div className={styles.formContainer}>
              <div className={styles.formGroup}>
                <div>
                  <div className={styles.formLabel}>Max Concurrent Extractions</div>
                  <div className={styles.formHelp}>Maximum parallel SharpCompress workers</div>
                </div>
                <input
                  type="number"
                  className={styles.formInput}
                  value={extForm.maxConcurrentExtractions}
                  onChange={e =>
                    setExtForm({ ...extForm, maxConcurrentExtractions: Number(e.target.value) })
                  }
                />
              </div>

              <div className={styles.formGroup}>
                <div>
                  <div className={styles.formLabel}>Stream Buffer Size (KB)</div>
                  <div className={styles.formHelp}>I/O buffer size per archive stream</div>
                </div>
                <input
                  type="number"
                  className={styles.formInput}
                  value={extForm.bufferSizeKb}
                  onChange={e => setExtForm({ ...extForm, bufferSizeKb: Number(e.target.value) })}
                />
              </div>

              <div className={styles.formGroup}>
                <div>
                  <div className={styles.formLabel}>Temporary Folder Naming</div>
                  <div className={styles.formHelp}>
                    Temporary directory inside download folder during extraction (e.g. _unpackerred)
                  </div>
                </div>
                <input
                  className={styles.formInput}
                  value={extForm.tempFolderName}
                  onChange={e => setExtForm({ ...extForm, tempFolderName: e.target.value })}
                />
              </div>

              {!hostIsWindows && (
                <>
                  <div className={styles.formGroup}>
                    <div>
                      <div className={styles.formLabel}>File Permissions (chmod)</div>
                      <div className={styles.formHelp}>Octal mode applied to extracted files on Linux/macOS</div>
                    </div>
                    <input
                      className={styles.formInput}
                      value={extForm.filePermissions}
                      onChange={e => setExtForm({ ...extForm, filePermissions: e.target.value })}
                      placeholder="0644"
                    />
                  </div>

                  <div className={styles.formGroup}>
                    <div>
                      <div className={styles.formLabel}>Folder Permissions (chmod)</div>
                      <div className={styles.formHelp}>Octal mode applied to extracted folders on Linux/macOS</div>
                    </div>
                    <input
                      className={styles.formInput}
                      value={extForm.folderPermissions}
                      onChange={e => setExtForm({ ...extForm, folderPermissions: e.target.value })}
                      placeholder="0755"
                    />
                  </div>

                  <div className={styles.formGroup}>
                    <div>
                      <div className={styles.formLabel}>Chown UID / GID</div>
                      <div className={styles.formHelp}>Optional Linux UID/GID ownership (defaults to PUID/PGID)</div>
                    </div>
                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
                      <input
                        className={styles.formInput}
                        value={extForm.chownUid}
                        onChange={e => setExtForm({ ...extForm, chownUid: e.target.value })}
                        placeholder="UID (e.g. 1000)"
                      />
                      <input
                        className={styles.formInput}
                        value={extForm.chownGid}
                        onChange={e => setExtForm({ ...extForm, chownGid: e.target.value })}
                        placeholder="GID (e.g. 1000)"
                      />
                    </div>
                  </div>
                </>
              )}

              <div className={styles.formGroup}>
                <div>
                  <div className={styles.formLabel}>Delete Delay Timer (seconds)</div>
                  <div className={styles.formHelp}>
                    Time to wait after *arr imports the release before deleting extracted leftover files (preserving original .rar files for torrent seeding)
                  </div>
                </div>
                <input
                  type="number"
                  className={styles.formInput}
                  value={extForm.deleteDelaySeconds}
                  onChange={e =>
                    setExtForm({ ...extForm, deleteDelaySeconds: Number(e.target.value) })
                  }
                />
              </div>

              <div className={styles.formGroup}>
                <div>
                  <div className={styles.formLabel}>Retry Limit</div>
                  <div className={styles.formHelp}>
                    Max extraction attempts before moving archive to Blocklist
                  </div>
                </div>
                <input
                  type="number"
                  className={styles.formInput}
                  value={extForm.maxRetries}
                  onChange={e => setExtForm({ ...extForm, maxRetries: Number(e.target.value) })}
                />
              </div>

              <div className={styles.formGroup}>
                <div>
                  <div className={styles.formLabel}>Archive Passwords List</div>
                  <div className={styles.formHelp}>
                    One password per line. Automatically tried in order when encrypted RAR/7z/ZIP archives are encountered.
                  </div>
                </div>
                <textarea
                  rows={5}
                  className={styles.formTextarea}
                  value={passwordsText}
                  onChange={e => setPasswordsText(e.target.value)}
                  placeholder="Enter one password per line..."
                />
              </div>
              </div>
            </div>
          </div>
        )}

        {/* REMOTE PATH MAPPINGS */}
        {activeSubTab === 'remotepath' && (
          <div className={styles.fieldSetSection}>
            <div className={styles.sectionHeading}>
              <h2 className={styles.sectionHeadingTitle}>Remote Path Mappings</h2>
            </div>
            <div className={styles.fieldSet}>
              <div className={styles.tableWrapper}>
              {mappings.length === 0 ? (
                <div className={styles.emptyState}>
                  No Remote Path Mappings configured. Map container/NAS download paths reported by *arr APIs to local paths accessible by Extractarr.
                </div>
              ) : (
                <table className={styles.dataTable}>
                  <thead>
                    <tr>
                      <th>Host / Instance</th>
                      <th>Remote Path (Reported by *arr)</th>
                      <th>Local Path (Accessible by Extractarr)</th>
                      <th style={{ textAlign: 'right' }}>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {mappings.map(m => (
                      <tr key={m.id}>
                        <td>
                          <span className={`${styles.tag} ${styles.tagCyan}`}>{m.host}</span>
                        </td>
                        <td style={{ fontFamily: 'monospace', fontSize: 12 }}>{m.remotePath}</td>
                        <td style={{ fontFamily: 'monospace', fontSize: 12 }}>{m.localPath}</td>
                        <td style={{ textAlign: 'right' }}>
                          <button
                            className={styles.toolbarBtn}
                            onClick={() => setEditingMapping({ ...m })}
                          >
                            Edit
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

        {/* CONNECT (NOTIFICATIONS) */}
        {activeSubTab === 'connect' && (
          <>
            <div className={styles.fieldSetSection}>
              <div className={styles.sectionHeading}>
                <h2 className={styles.sectionHeadingTitle}>Add Notification Connection</h2>
              </div>
              <div className={styles.fieldSet}>
                <div className={styles.providerGrid}>
                  {NOTIFICATION_PROVIDERS.map(impl => (
                    <div
                      key={impl}
                      className={styles.providerTile}
                      onClick={() => openAddNotification(impl)}
                    >
                      <div style={{ fontWeight: 700, color: 'var(--accent)' }}>{impl}</div>
                      <div style={{ fontSize: 11, color: 'var(--inkMuted)', marginTop: 4 }}>
                        OnExtract / OnFail / OnCleanup
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            </div>

            <div className={styles.fieldSetSection}>
              <div className={styles.sectionHeading}>
                <h2 className={styles.sectionHeadingTitle}>Configured Notification Connections</h2>
              </div>
              <div className={styles.fieldSet}>
                <div className={styles.tableWrapper}>
                {notifications.length === 0 ? (
                  <div className={styles.emptyState}>No notification connections configured.</div>
                ) : (
                  <table className={styles.dataTable}>
                    <thead>
                      <tr>
                        <th>Name</th>
                        <th>Provider</th>
                        <th>Triggers</th>
                        <th style={{ textAlign: 'right' }}>Actions</th>
                      </tr>
                    </thead>
                    <tbody>
                      {notifications.map(n => (
                        <tr key={n.id}>
                          <td style={{ fontWeight: 600 }}>{n.name}</td>
                          <td>
                            <span className={`${styles.tag} ${styles.tagCyan}`}>
                              {n.implementation}
                            </span>
                          </td>
                          <td>
                            <div className={styles.tagRow}>
                              {n.onExtractionStart && <span className={styles.tag}>On Start</span>}
                              {n.onExtractionComplete && (
                                <span className={`${styles.tag} ${styles.tagSuccess}`}>
                                  On Complete
                                </span>
                              )}
                              {n.onExtractionFailure && (
                                <span className={`${styles.tag} ${styles.tagDanger}`}>
                                  On Failure
                                </span>
                              )}
                              {n.onCleanup && (
                                <span className={`${styles.tag} ${styles.tagWarning}`}>
                                  On Cleanup
                                </span>
                              )}
                            </div>
                          </td>
                          <td style={{ textAlign: 'right' }}>
                            <button
                              className={styles.toolbarBtn}
                              onClick={() => openEditNotification(n)}
                            >
                              Edit
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
          </>
        )}

        {/* GENERAL HOST SETTINGS */}
        {activeSubTab === 'general' && hostForm && (
          <div className={styles.fieldSetSection}>
            <div className={styles.sectionHeading}>
              <h2 className={styles.sectionHeadingTitle}>Host, Security, Logging & API Key</h2>
            </div>
            <div className={styles.fieldSet}>
              <div className={styles.formContainer}>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Instance Name</div>
                <input
                  className={styles.formInput}
                  value={hostForm.instanceName}
                  onChange={e => setHostForm({ ...hostForm, instanceName: e.target.value })}
                />
              </div>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Bind Address</div>
                <input
                  className={styles.formInput}
                  value={hostForm.bindAddress}
                  onChange={e => setHostForm({ ...hostForm, bindAddress: e.target.value })}
                />
              </div>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Port Number</div>
                <input
                  type="number"
                  className={styles.formInput}
                  value={hostForm.port}
                  onChange={e => setHostForm({ ...hostForm, port: Number(e.target.value) })}
                />
              </div>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>URL Base</div>
                <input
                  className={styles.formInput}
                  value={hostForm.urlBase}
                  onChange={e => setHostForm({ ...hostForm, urlBase: e.target.value })}
                  placeholder="(Optional) e.g. /extractarr"
                />
              </div>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Authentication</div>
                <select
                  className={styles.formSelect}
                  value={hostForm.authenticationMethod}
                  onChange={e =>
                    setHostForm({ ...hostForm, authenticationMethod: e.target.value })
                  }
                >
                  <option value="none">None</option>
                  <option value="forms">Forms (Login Page)</option>
                  <option value="basic">Basic (Browser Popup)</option>
                  <option value="external">External</option>
                </select>
              </div>
              <div className={styles.formGroup}>
                <div>
                  <div className={styles.formLabel}>API Key</div>
                  <div className={styles.formHelp}>Use for /api/v3/webhook and external integrations</div>
                </div>
                <div style={{ display: 'flex', gap: 8 }}>
                  <input className={styles.formInput} readOnly value={hostForm.apiKey} />
                  <button
                    className={styles.toolbarBtn}
                    onClick={() => navigator.clipboard?.writeText(hostForm.apiKey)}
                    title="Copy API Key"
                  >
                    <Copy size={14} />
                  </button>
                  <button
                    className={styles.toolbarBtn}
                    onClick={async () => {
                      const res = await api.resetApiKey();
                      setHostForm({ ...hostForm, apiKey: res.apiKey });
                    }}
                    title="Regenerate API Key"
                  >
                    <Key size={14} />
                  </button>
                </div>
              </div>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Log Level</div>
                <select
                  className={styles.formSelect}
                  value={hostForm.logLevel}
                  onChange={e => setHostForm({ ...hostForm, logLevel: e.target.value })}
                >
                  <option value="info">Info</option>
                  <option value="debug">Debug</option>
                  <option value="trace">Trace</option>
                </select>
              </div>
              </div>
            </div>
          </div>
        )}

        {/* UI SETTINGS */}
        {activeSubTab === 'ui' && (
          <div className={styles.fieldSetSection}>
            <div className={styles.sectionHeading}>
              <h2 className={styles.sectionHeadingTitle}>Theme & Display Preferences</h2>
            </div>
            <div className={styles.fieldSet}>
              <div className={styles.formContainer}>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Theme</div>
                <select
                  className={styles.formSelect}
                  value={uiForm.theme}
                  onChange={e =>
                    setUiForm({ ...uiForm, theme: e.target.value as UiConfig['theme'] })
                  }
                >
                  <option value="auto">Auto (System Default)</option>
                  <option value="dark">Extractarr Dark (Electric Violet / Carbon)</option>
                  <option value="light">Extractarr Light (Electric Violet / Zinc)</option>
                </select>
              </div>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Short Date Format</div>
                <select
                  className={styles.formSelect}
                  value={uiForm.shortDateFormat}
                  onChange={e => setUiForm({ ...uiForm, shortDateFormat: e.target.value })}
                >
                  <option value="MMM D YYYY">Oct 18 2026</option>
                  <option value="DD MMM YYYY">18 Oct 2026</option>
                  <option value="YYYY-MM-DD">2026-10-18</option>
                </select>
              </div>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Time Format</div>
                <select
                  className={styles.formSelect}
                  value={uiForm.timeFormat}
                  onChange={e => setUiForm({ ...uiForm, timeFormat: e.target.value })}
                >
                  <option value="HH:mm">24-Hour (17:30)</option>
                  <option value="h(:mm)a">12-Hour (5:30pm)</option>
                </select>
              </div>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Color-Impaired Mode</div>
                <label className={styles.checkboxLabel}>
                  <input
                    type="checkbox"
                    checked={uiForm.enableColorImpairedMode}
                    onChange={e =>
                      setUiForm({ ...uiForm, enableColorImpairedMode: e.target.checked })
                    }
                  />
                  <span>Enable striped progress bars and high-contrast status indicators</span>
                </label>
              </div>
              </div>
            </div>
          </div>
        )}
      </div>

      {/* Remote Path Mapping Modal */}
      {editingMapping && (
        <div className={styles.modalBackdrop} onClick={() => setEditingMapping(null)}>
          <div className={styles.modalDialog} onClick={e => e.stopPropagation()}>
            <div className={styles.modalHeader}>
              <span>{editingMapping.id ? 'Edit' : 'Add'} Remote Path Mapping</span>
              <button className={styles.toolbarBtn} onClick={() => setEditingMapping(null)}>
                <X size={15} />
              </button>
            </div>
            <div className={styles.modalBody}>
              <div className={styles.formGroup}>
                <div>
                  <div className={styles.formLabel}>Host / Instance</div>
                  <div className={styles.formHelp}>Instance Name, Hostname, or * for all</div>
                </div>
                <input
                  className={styles.formInput}
                  value={editingMapping.host || ''}
                  onChange={e => setEditingMapping({ ...editingMapping, host: e.target.value })}
                />
              </div>
              <div className={styles.formGroup}>
                <div>
                  <div className={styles.formLabel}>Remote Path</div>
                  <div className={styles.formHelp}>Root path reported by the *arr queue API</div>
                </div>
                <input
                  className={styles.formInput}
                  value={editingMapping.remotePath || ''}
                  onChange={e =>
                    setEditingMapping({ ...editingMapping, remotePath: e.target.value })
                  }
                />
              </div>
              <div className={styles.formGroup}>
                <div>
                  <div className={styles.formLabel}>Local Path</div>
                  <div className={styles.formHelp}>
                    Path mounted inside Extractarr (auto-translates to {hostIsWindows ? 'Windows' : 'Linux'} format)
                  </div>
                </div>
                <input
                  className={styles.formInput}
                  value={editingMapping.localPath || ''}
                  onChange={e =>
                    setEditingMapping({ ...editingMapping, localPath: e.target.value })
                  }
                  onBlur={e =>
                    setEditingMapping({
                      ...editingMapping,
                      localPath: translatePathForHostOs(e.target.value, hostIsWindows)
                    })
                  }
                />
              </div>
            </div>
            <div className={styles.modalFooter}>
              <div>
                {editingMapping.id ? (
                  <button
                    className={`${styles.toolbarBtn} ${styles.toolbarBtnDanger}`}
                    onClick={() => handleDeleteMapping(editingMapping.id!)}
                  >
                    <Trash2 size={14} />
                    <span>Delete</span>
                  </button>
                ) : null}
              </div>
              <div style={{ display: 'flex', gap: 8 }}>
                <button className={styles.toolbarBtn} onClick={() => setEditingMapping(null)}>
                  Cancel
                </button>
                <button
                  className={`${styles.toolbarBtn} ${styles.toolbarBtnPrimary}`}
                  onClick={handleSaveMapping}
                >
                  Save
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Notification Modal */}
      {editingNotif && (
        <div className={styles.modalBackdrop} onClick={() => setEditingNotif(null)}>
          <div className={styles.modalDialog} onClick={e => e.stopPropagation()}>
            <div className={styles.modalHeader}>
              <span>
                {editingNotif.id ? 'Edit' : 'Add'} {editingNotif.implementation} Notification
              </span>
              <button className={styles.toolbarBtn} onClick={() => setEditingNotif(null)}>
                <X size={15} />
              </button>
            </div>
            <div className={styles.modalBody}>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Name</div>
                <input
                  className={styles.formInput}
                  value={editingNotif.name || ''}
                  onChange={e => setEditingNotif({ ...editingNotif, name: e.target.value })}
                />
              </div>
              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Triggers</div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                  <label className={styles.checkboxLabel}>
                    <input
                      type="checkbox"
                      checked={!!editingNotif.onExtractionStart}
                      onChange={e =>
                        setEditingNotif({ ...editingNotif, onExtractionStart: e.target.checked })
                      }
                    />
                    <span>On Extraction Start</span>
                  </label>
                  <label className={styles.checkboxLabel}>
                    <input
                      type="checkbox"
                      checked={!!editingNotif.onExtractionComplete}
                      onChange={e =>
                        setEditingNotif({
                          ...editingNotif,
                          onExtractionComplete: e.target.checked
                        })
                      }
                    />
                    <span>On Extraction Complete</span>
                  </label>
                  <label className={styles.checkboxLabel}>
                    <input
                      type="checkbox"
                      checked={!!editingNotif.onExtractionFailure}
                      onChange={e =>
                        setEditingNotif({
                          ...editingNotif,
                          onExtractionFailure: e.target.checked
                        })
                      }
                    />
                    <span>On Extraction Failure</span>
                  </label>
                  <label className={styles.checkboxLabel}>
                    <input
                      type="checkbox"
                      checked={!!editingNotif.onCleanup}
                      onChange={e =>
                        setEditingNotif({ ...editingNotif, onCleanup: e.target.checked })
                      }
                    />
                    <span>On Extracted Files Cleanup</span>
                  </label>
                </div>
              </div>

              <div className={styles.formGroup}>
                <div className={styles.formLabel}>Webhook / Endpoint URL</div>
                <input
                  className={styles.formInput}
                  value={notifSettingsObj.url || notifSettingsObj.webhookUrl || ''}
                  onChange={e =>
                    setNotifSettingsObj({
                      ...notifSettingsObj,
                      url: e.target.value,
                      webhookUrl: e.target.value
                    })
                  }
                  placeholder="https://discord.com/api/webhooks/... or mock://test"
                />
              </div>

              {notifTestMsg && (
                <div
                  className={styles.alertBox}
                  style={{
                    borderColor: notifTestMsg.isValid ? 'var(--successColor)' : 'var(--dangerColor)',
                    color: notifTestMsg.isValid ? 'var(--successColor)' : 'var(--dangerColor)'
                  }}
                >
                  {notifTestMsg.message}
                </div>
              )}
            </div>
            <div className={styles.modalFooter}>
              <div>
                {editingNotif.id ? (
                  <button
                    className={`${styles.toolbarBtn} ${styles.toolbarBtnDanger}`}
                    onClick={async () => {
                      await api.deleteNotification(editingNotif.id!);
                      dispatch(setNotifications(await api.getNotifications()));
                      setEditingNotif(null);
                    }}
                  >
                    <Trash2 size={14} />
                    <span>Delete</span>
                  </button>
                ) : null}
              </div>
              <div style={{ display: 'flex', gap: 8 }}>
                <button className={styles.toolbarBtn} onClick={handleTestNotification}>
                  <Wifi size={14} />
                  <span>Test</span>
                </button>
                <button className={styles.toolbarBtn} onClick={() => setEditingNotif(null)}>
                  Cancel
                </button>
                <button
                  className={`${styles.toolbarBtn} ${styles.toolbarBtnPrimary}`}
                  onClick={handleSaveNotification}
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


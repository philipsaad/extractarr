import React, { useState } from 'react';
import {
  CheckCircle2,
  Plus,
  RefreshCw,
  ShieldAlert,
  Trash2,
  Wifi,
  X
} from 'lucide-react';
import styles from '../App.module.css';
import { ArrConnectionTestResult, ArrInstance, ArrInstanceType } from '../types/api';
import { api } from '../services/api';
import { removeInstance, upsertInstance, useAppDispatch, useAppSelector } from '../store/store';

const ARR_TYPES: { type: ArrInstanceType; defaultPort: number; color: string; desc: string }[] = [
  { type: 'Sonarr', defaultPort: 8989, color: '#35c5f4', desc: 'TV Series PVR' },
  { type: 'Radarr', defaultPort: 7878, color: '#ffc230', desc: 'Movie Collection Manager' },
  { type: 'Lidarr', defaultPort: 8686, color: '#2ecc71', desc: 'Music Collection Manager' },
  { type: 'Readarr', defaultPort: 8787, color: '#e74c3c', desc: 'Book & Audiobook Manager' },
  { type: 'Whisparr', defaultPort: 6969, color: '#e84393', desc: 'Adult Video PVR' }
];

export const InstancesView: React.FC = () => {
  const dispatch = useAppDispatch();
  const instances = useAppSelector(s => s.unpackarr.instances);
  const globalFilter = useAppSelector(s => s.unpackarr.globalFilter);

  const [modalOpen, setModalOpen] = useState(false);
  const [stepSelectType, setStepSelectType] = useState(true);
  const [editing, setEditing] = useState<Partial<ArrInstance> | null>(null);
  const [testResult, setTestResult] = useState<ArrConnectionTestResult | null>(null);
  const [testing, setTesting] = useState(false);
  const [saving, setSaving] = useState(false);
  const [pollingId, setPollingId] = useState<number | null>(null);

  const filtered = instances.filter(
    i =>
      !globalFilter ||
      i.name.toLowerCase().includes(globalFilter.toLowerCase()) ||
      i.instanceType.toLowerCase().includes(globalFilter.toLowerCase()) ||
      i.baseUrl.toLowerCase().includes(globalFilter.toLowerCase())
  );

  const openAddModal = () => {
    setStepSelectType(true);
    setTestResult(null);
    setEditing(null);
    setModalOpen(true);
  };

  const selectProviderType = (type: ArrInstanceType, port: number) => {
    setStepSelectType(false);
    setTestResult(null);
    setEditing({
      id: 0,
      name: `${type} Main`,
      enable: true,
      instanceType: type,
      baseUrl: `http://localhost:${port}`,
      apiKey: '',
      pollIntervalSeconds: 15,
      timeoutSeconds: 15,
      verifySsl: false,
      monitoredProtocols: '["torrent","usenet"]',
      monitoredCategories: '[]',
      deleteOriginalArchives: false,
      syncthingWait: true,
      healthStatus: 'Unknown',
      activeQueueCount: 0
    });
  };

  const openEditModal = (inst: ArrInstance) => {
    setStepSelectType(false);
    setTestResult(null);
    setEditing({ ...inst });
    setModalOpen(true);
  };

  const handleTest = async () => {
    if (!editing) return;
    setTesting(true);
    setTestResult(null);
    try {
      const res = await api.testInstance(editing);
      setTestResult(res);
    } catch (e: any) {
      setTestResult({ isValid: false, message: e.message || 'Test failed' });
    } finally {
      setTesting(false);
    }
  };

  const handleSave = async () => {
    if (!editing || !editing.name || !editing.baseUrl || !editing.apiKey) return;
    setSaving(true);
    try {
      if (editing.id && editing.id > 0) {
        const updated = await api.updateInstance(editing.id, editing);
        dispatch(upsertInstance(updated));
      } else {
        const created = await api.createInstance(editing);
        dispatch(upsertInstance(created));
      }
      setModalOpen(false);
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async () => {
    if (!editing?.id) return;
    await api.deleteInstance(editing.id);
    dispatch(removeInstance(editing.id));
    setModalOpen(false);
  };

  const handlePollInstance = async (id: number) => {
    setPollingId(id);
    try {
      await api.pollInstance(id);
    } finally {
      setTimeout(() => setPollingId(null), 600);
    }
  };

  const getBorderColor = (type: string) => {
    return ARR_TYPES.find(t => t.type.toLowerCase() === type.toLowerCase())?.color || '#35c5f4';
  };

  return (
    <>
      <div className={styles.pageToolbar}>
        <button className={styles.toolbarBtn} onClick={() => api.runCommand('PollArrInstances')}>
          <RefreshCw size={14} />
          <span>Sync / Poll All</span>
        </button>
        <div className={styles.toolbarSeparator} />
        <button className={styles.toolbarBtn} onClick={openAddModal}>
          <Plus size={14} />
          <span>Add Instance</span>
        </button>
      </div>

      <div className={styles.pageBody}>
        <div className={styles.pageHeading}>
          <div className={styles.pageHeadingRow}>
            <h1 className={styles.pageHeadingTitle}>
              <span className={styles.pageHeadingScope}>Extraction · </span>
              Instances
            </h1>
          </div>
        </div>

        <div className={styles.fieldSetSection}>
          <div className={styles.sectionHeading}>
            <h2 className={styles.sectionHeadingTitle}>Configured *arr Instances</h2>
            <p className={styles.sectionHeadingDescription}>
              Monitor Sonarr, Radarr, Lidarr, Readarr, and Whisparr queues for packed releases
            </p>
          </div>
        </div>

        <div className={styles.cardsGrid}>
          {filtered.map(inst => (
            <div
              key={inst.id}
              className={styles.instanceCard}
              style={{ borderLeftColor: getBorderColor(inst.instanceType) }}
            >
              <div>
                <div className={styles.cardHeader}>
                  <div>
                    <div className={styles.cardTitle} onClick={() => openEditModal(inst)}>
                      {inst.name}
                    </div>
                    <div className={styles.cardSubtitle}>{inst.baseUrl}</div>
                  </div>
                  <span
                    className={`${styles.tag} ${
                      inst.healthStatus === 'Online'
                        ? styles.tagSuccess
                        : inst.healthStatus === 'Error' || inst.healthStatus === 'Offline'
                        ? styles.tagDanger
                        : styles.tagWarning
                    }`}
                  >
                    {inst.healthStatus}
                  </span>
                </div>

                <div className={styles.tagRow} style={{ marginTop: 12 }}>
                  <span className={`${styles.tag} ${styles.tagCyan}`}>{inst.instanceType}</span>
                  {inst.version && <span className={styles.tag}>v{inst.version}</span>}
                  <span className={styles.tag}>{inst.enable ? 'Enabled' : 'Disabled'}</span>
                  <span className={styles.tag}>Poll: {inst.pollIntervalSeconds}s</span>
                  {inst.syncthingWait && <span className={styles.tag}>Syncthing Wait</span>}
                </div>

                {inst.lastErrorMessage && (
                  <div
                    style={{
                      marginTop: 10,
                      fontSize: 12,
                      color: 'var(--dangerColor)',
                      display: 'flex',
                      alignItems: 'center',
                      gap: 6
                    }}
                  >
                    <ShieldAlert size={14} />
                    <span>{inst.lastErrorMessage}</span>
                  </div>
                )}
              </div>

              <div
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  paddingTop: 10,
                  borderTop: '1px solid var(--borderColor)'
                }}
              >
                <span style={{ fontSize: 12, color: 'var(--inkMuted)' }}>
                  Queue items: <strong>{inst.activeQueueCount}</strong>
                </span>
                <div style={{ display: 'flex', gap: 6 }}>
                  <button
                    className={styles.toolbarBtn}
                    onClick={() => handlePollInstance(inst.id)}
                    title="Poll this instance's queue immediately"
                  >
                    <RefreshCw size={13} className={pollingId === inst.id ? 'spin' : ''} />
                    <span>Poll Now</span>
                  </button>
                  <button className={styles.toolbarBtn} onClick={() => openEditModal(inst)}>
                    Edit
                  </button>
                </div>
              </div>
            </div>
          ))}

          <div className={styles.addCard} onClick={openAddModal}>
            <Plus size={34} />
            <span style={{ fontWeight: 600 }}>Add *arr Instance</span>
            <span style={{ fontSize: 12 }}>Sonarr, Radarr, Lidarr, Readarr, Whisparr</span>
          </div>
        </div>
      </div>

      {modalOpen && (
        <div className={styles.modalBackdrop} onClick={() => setModalOpen(false)}>
          <div className={styles.modalDialog} onClick={e => e.stopPropagation()}>
            <div className={styles.modalHeader}>
              <span>
                {stepSelectType
                  ? 'Add *arr Instance — Select Type'
                  : `${editing?.id ? 'Edit' : 'Add'} ${editing?.instanceType} Instance`}
              </span>
              <button className={styles.toolbarBtn} onClick={() => setModalOpen(false)}>
                <X size={15} />
              </button>
            </div>

            <div className={styles.modalBody}>
              {stepSelectType ? (
                <div className={styles.providerGrid}>
                  {ARR_TYPES.map(p => (
                    <div
                      key={p.type}
                      className={styles.providerTile}
                      onClick={() => selectProviderType(p.type, p.defaultPort)}
                    >
                      <div style={{ fontSize: 17, fontWeight: 700, color: p.color, marginBottom: 4 }}>
                        {p.type}
                      </div>
                      <div style={{ fontSize: 12, color: 'var(--inkMuted)' }}>{p.desc}</div>
                      <div style={{ fontSize: 11, color: 'var(--inkSubtle)', marginTop: 6 }}>
                        Default Port: {p.defaultPort}
                      </div>
                    </div>
                  ))}
                </div>
              ) : (
                editing && (
                  <>
                    <div className={styles.formGroup}>
                      <div className={styles.formLabel}>Name</div>
                      <input
                        className={styles.formInput}
                        value={editing.name || ''}
                        onChange={e => setEditing({ ...editing, name: e.target.value })}
                        placeholder="e.g. Sonarr 4K"
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
                        <span>Monitor this {editing.instanceType} instance queue</span>
                      </label>
                    </div>

                    <div className={styles.formGroup}>
                      <div className={styles.formLabel}>*arr Type</div>
                      <select
                        className={styles.formSelect}
                        value={editing.instanceType || 'Sonarr'}
                        onChange={e =>
                          setEditing({ ...editing, instanceType: e.target.value as ArrInstanceType })
                        }
                      >
                        {ARR_TYPES.map(t => (
                          <option key={t.type} value={t.type}>
                            {t.type}
                          </option>
                        ))}
                      </select>
                    </div>

                    <div className={styles.formGroup}>
                      <div>
                        <div className={styles.formLabel}>Base URL</div>
                        <div className={styles.formHelp}>Full URL including http/https and port</div>
                      </div>
                      <input
                        className={styles.formInput}
                        value={editing.baseUrl || ''}
                        onChange={e => setEditing({ ...editing, baseUrl: e.target.value })}
                        placeholder="http://localhost:8989"
                      />
                    </div>

                    <div className={styles.formGroup}>
                      <div>
                        <div className={styles.formLabel}>API Key</div>
                        <div className={styles.formHelp}>Found in {editing.instanceType} Settings &rarr; General</div>
                      </div>
                      <input
                        className={styles.formInput}
                        value={editing.apiKey || ''}
                        onChange={e => setEditing({ ...editing, apiKey: e.target.value })}
                        placeholder="32-character hex API key"
                      />
                    </div>

                    <div className={styles.formGroup}>
                      <div>
                        <div className={styles.formLabel}>Poll Interval (seconds)</div>
                        <div className={styles.formHelp}>How often to poll /api/v3/queue</div>
                      </div>
                      <input
                        type="number"
                        className={styles.formInput}
                        value={editing.pollIntervalSeconds ?? 15}
                        onChange={e =>
                          setEditing({ ...editing, pollIntervalSeconds: Number(e.target.value) })
                        }
                      />
                    </div>

                    <div className={styles.formGroup}>
                      <div className={styles.formLabel}>Timeout (seconds)</div>
                      <input
                        type="number"
                        className={styles.formInput}
                        value={editing.timeoutSeconds ?? 15}
                        onChange={e =>
                          setEditing({ ...editing, timeoutSeconds: Number(e.target.value) })
                        }
                      />
                    </div>

                    <div className={styles.formGroup}>
                      <div>
                        <div className={styles.formLabel}>Protocols to Monitor</div>
                        <div className={styles.formHelp}>JSON or comma-separated (torrent, usenet)</div>
                      </div>
                      <input
                        className={styles.formInput}
                        value={editing.monitoredProtocols || '["torrent","usenet"]'}
                        onChange={e =>
                          setEditing({ ...editing, monitoredProtocols: e.target.value })
                        }
                      />
                    </div>

                    <div className={styles.formGroup}>
                      <div>
                        <div className={styles.formLabel}>Content Categories</div>
                        <div className={styles.formHelp}>Optional category filter (leave [] for all)</div>
                      </div>
                      <input
                        className={styles.formInput}
                        value={editing.monitoredCategories || '[]'}
                        onChange={e =>
                          setEditing({ ...editing, monitoredCategories: e.target.value })
                        }
                      />
                    </div>

                    <div className={styles.formGroup}>
                      <div className={styles.formLabel}>Verify SSL</div>
                      <label className={styles.checkboxLabel}>
                        <input
                          type="checkbox"
                          checked={!!editing.verifySsl}
                          onChange={e => setEditing({ ...editing, verifySsl: e.target.checked })}
                        />
                        <span>Validate HTTPS certificate chain</span>
                      </label>
                    </div>

                    <div className={styles.formGroup}>
                      <div className={styles.formLabel}>Syncthing Wait</div>
                      <label className={styles.checkboxLabel}>
                        <input
                          type="checkbox"
                          checked={!!editing.syncthingWait}
                          onChange={e =>
                            setEditing({ ...editing, syncthingWait: e.target.checked })
                          }
                        />
                        <span>Wait for .tmp / .syncthing files to disappear before extracting</span>
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
                        <span style={{ color: 'var(--warningColor)' }}>
                          Delete original .rar/.zip archives after import (Warning: breaks torrent seeding!)
                        </span>
                      </label>
                    </div>

                    {testResult && (
                      <div
                        className={styles.alertBox}
                        style={{
                          borderColor: testResult.isValid ? 'var(--successColor)' : 'var(--dangerColor)',
                          color: testResult.isValid ? 'var(--successColor)' : 'var(--dangerColor)',
                          display: 'flex',
                          alignItems: 'center',
                          gap: 8
                        }}
                      >
                        <CheckCircle2 size={16} />
                        <span>
                          {testResult.message}
                          {testResult.version ? ` (Version: ${testResult.version})` : ''}
                        </span>
                      </div>
                    )}
                  </>
                )
              )}
            </div>

            {!stepSelectType && (
              <div className={styles.modalFooter}>
                <div>
                  {editing?.id ? (
                    <button
                      className={`${styles.toolbarBtn} ${styles.toolbarBtnDanger}`}
                      onClick={handleDelete}
                    >
                      <Trash2 size={14} />
                      <span>Delete</span>
                    </button>
                  ) : null}
                </div>
                <div style={{ display: 'flex', gap: 8 }}>
                  <button className={styles.toolbarBtn} onClick={handleTest} disabled={testing}>
                    <Wifi size={14} />
                    <span>{testing ? 'Testing...' : 'Test Connection'}</span>
                  </button>
                  <button className={styles.toolbarBtn} onClick={() => setModalOpen(false)}>
                    Cancel
                  </button>
                  <button
                    className={`${styles.toolbarBtn} ${styles.toolbarBtnPrimary}`}
                    onClick={handleSave}
                    disabled={saving}
                  >
                    <span>{saving ? 'Saving...' : 'Save'}</span>
                  </button>
                </div>
              </div>
            )}
          </div>
        </div>
      )}
    </>
  );
};


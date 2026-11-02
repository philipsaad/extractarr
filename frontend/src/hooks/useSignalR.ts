import { useEffect } from 'react';
import * as signalR from '@microsoft/signalr';
import {
  removeBlocklistItem,
  removeInstance,
  removeQueueJob,
  removeWatchFolder,
  setBlocklist,
  setHealth,
  setHistory,
  setSignalRConnected,
  setTasks,
  upsertCommand,
  upsertInstance,
  upsertQueueJob,
  upsertWatchFolder,
  useAppDispatch
} from '../store/store';
import { SignalRMessage } from '../types/api';
import { api } from '../services/api';

export function useSignalR() {
  const dispatch = useAppDispatch();

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/signalr/messages')
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connection.on('receiveMessage', async (msg: SignalRMessage) => {
      if (!msg || !msg.name) return;

      switch (msg.name.toLowerCase()) {
        case 'queue':
          if (msg.action === 'deleted' && msg.body?.id) {
            dispatch(removeQueueJob(msg.body.id));
          } else if (msg.body) {
            dispatch(upsertQueueJob(msg.body));
          }
          break;

        case 'instance':
          if (msg.action === 'deleted' && msg.body?.id) {
            dispatch(removeInstance(msg.body.id));
          } else if (msg.body) {
            dispatch(upsertInstance(msg.body));
          }
          break;

        case 'watchfolder':
          if (msg.action === 'deleted' && msg.body?.id) {
            dispatch(removeWatchFolder(msg.body.id));
          } else if (msg.body) {
            dispatch(upsertWatchFolder(msg.body));
          }
          break;

        case 'command':
          if (msg.body) {
            dispatch(upsertCommand(msg.body));
            if (msg.body.status === 'completed') {
              const tasks = await api.getTasks().catch(() => null);
              if (tasks) dispatch(setTasks(tasks));
            }
          }
          break;

        case 'health':
          if (Array.isArray(msg.body)) {
            dispatch(setHealth(msg.body));
          }
          break;

        case 'history': {
          const hist = await api.getHistory(1, 50).catch(() => null);
          if (hist) dispatch(setHistory(hist));
          break;
        }

        case 'blocklist':
          if (msg.action === 'deleted' && msg.body?.id) {
            dispatch(removeBlocklistItem(msg.body.id));
          } else {
            const bl = await api.getBlocklist(1, 50).catch(() => null);
            if (bl) dispatch(setBlocklist(bl));
          }
          break;
      }
    });

    connection.onreconnecting(() => dispatch(setSignalRConnected(false)));
    connection.onreconnected(() => dispatch(setSignalRConnected(true)));
    connection.onclose(() => dispatch(setSignalRConnected(false)));

    connection
      .start()
      .then(() => dispatch(setSignalRConnected(true)))
      .catch(() => dispatch(setSignalRConnected(false)));

    return () => {
      connection.stop();
    };
  }, [dispatch]);
}


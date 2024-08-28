using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;

namespace HSL.Core
{
    public class ServerManager : IDisposable,INotifyPropertyChanged
    {

        public event PropertyChangedEventHandler PropertyChanged;
        public List<ServerInstance> servers { get; private set; }

        internal event EventHandler<ServerInstance> OnCreated, OnDeleted, OnProcessStarted, OnProcessStopped;

        private object _serverLock = new object();

        internal ServerManager()
        {
            servers = new List<ServerInstance>();
        }

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        internal ServerInstance Create(string exePath, bool autoStart = false) => Create(exePath, Guid.NewGuid(), autoStart);
        internal ServerInstance Create(string exePath, Guid guid, bool autoStart = false)
        {
            ServerInstance instance = new ServerInstance(exePath, guid, autoStart);
            instance.ProcessStarted += (s, e) => HandleEvent(OnProcessStarted, instance);
            instance.ProcessStopped += (s, e) => HandleEvent(OnProcessStopped, instance);
            servers.Add(instance);
            OnCreated?.Invoke(null, instance);
            OnPropertyChanged(nameof(servers));
            return instance;
        }

        private void HandleEvent(EventHandler<ServerInstance> handler, ServerInstance instance) => handler?.Invoke(this, instance);

        internal bool Delete(Guid guid) => Delete(servers.Where(x => x.Guid == guid).FirstOrDefault());

        internal bool Delete(ServerInstance instance)
        {
            if (instance == null)
                return false;

            instance.Dispose();

            lock (_serverLock)
            {
                if (servers.Remove(instance))
                {
                    OnDeleted?.Invoke(null, instance);
                    return true;
                }
                return false;
            }
        }

        public void Dispose()
        {
            lock (_serverLock)
            {
                foreach (ServerInstance instance in servers)
                {
                    instance.Dispose();
                }
                servers.Clear();
            }
        }

    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace HSL.Core
{
    public class ServerManager : IDisposable, INotifyPropertyChanged
    {

        public event PropertyChangedEventHandler PropertyChanged;
        public ObservableCollection<ServerInstance> servers { get; private set; }

        internal event EventHandler<ServerInstance> OnCreated, OnDeleted, OnProcessStarted, OnProcessStopped;

        private bool _dirtyConfig = false;
        private object _serverLock = new object();
        private Windows.Launcher _launcher;

        internal ServerManager(Windows.Launcher launcher)
        {
            _launcher = launcher;
            servers = new ObservableCollection<ServerInstance>();
        }

        internal ServerInstance? Create(string exePath, bool autoStart = false) => Create(new ServerData() { exe_file = exePath, guid = Guid.NewGuid(), auto_start = autoStart });
        internal ServerInstance? Create(ServerData data)
        {
            try
            {
                ServerInstance instance = new ServerInstance(this, data);
                instance.ProcessStarted += (s, e) => HandleEvent(OnProcessStarted, instance);
                instance.ProcessStopped += (s, e) => HandleEvent(OnProcessStopped, instance);
                instance.ServerUpdated += (s, e) => OnPropertyChanged(nameof(servers));
                lock (_serverLock)
                {
                    servers.Add(instance);
                }
                OnCreated?.Invoke(null, instance);
                OnPropertyChanged(nameof(servers));
                return instance;
            }
            catch (Exception e)
            {
                Trace.WriteLine(e.ToString());
            }
            return null;
        }

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
                    OnPropertyChanged(nameof(servers));
                    return true;
                }
                return false;
            }
        }

        internal bool Delete(Guid guid) => Delete(servers.Where(x => x.Guid == guid).FirstOrDefault());

        private void HandleEvent(EventHandler<ServerInstance> handler, ServerInstance instance) => handler?.Invoke(this, instance);

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        internal void MarkConfigDirty() => _dirtyConfig = true;

        internal bool IsDirty(bool clean = false)
        {
            if (_dirtyConfig & clean)
            {
                _dirtyConfig = !_dirtyConfig;
                return true;
            }
            return _dirtyConfig;
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

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace HSL
{
    public class ServerManager : IDisposable //: INotifyPropertyChanged
    {

        public event PropertyChangedEventHandler PropertyChanged;

        internal event EventHandler<ServerInstance> OnCreated;
        internal event EventHandler<ServerInstance> OnDeleted;

        internal event EventHandler<ServerInstance> OnProcessStarted;
        internal event EventHandler<ServerInstance> OnProcessStopped;

        public ObservableCollection<ServerInstance> servers { get; private set; }

        internal ServerManager() => servers = new ObservableCollection<ServerInstance>();

        private object _serverLock = new object();

        // private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        internal ServerInstance Create(string exePath, bool autoStart = false) => Create(exePath, Guid.NewGuid(), autoStart);
        internal ServerInstance Create(string exePath, Guid guid, bool autoStart = false)
        {
            ServerInstance instance = new ServerInstance(exePath, guid, autoStart);
            instance.ProcessStarted += (s, e) => HandleEvent(OnProcessStarted, instance);
            instance.ProcessStopped += (s, e) => HandleEvent(OnProcessStopped, instance);
            servers.Add(instance);
            // OnPropertyChanged(nameof(servers));
            OnCreated?.Invoke(null, instance);
            return instance;
        }

        private void HandleEvent(EventHandler<ServerInstance> handler, ServerInstance instance)
            => handler?.Invoke(this, instance);

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
                    //instance.Dispose();
                }
                servers.Clear();
            }
        }

    }
}

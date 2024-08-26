using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace HSL
{
    public class ServerManager : INotifyPropertyChanged
    {

        public event PropertyChangedEventHandler PropertyChanged;

        public List<ServerInstance> servers { get; private set; }

        internal ServerManager() => servers = new List<ServerInstance>();

        internal ServerInstance Create(string exePath, bool autoStart = false) => Create(exePath, Guid.NewGuid(), autoStart);

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        internal ServerInstance Create(string exePath, Guid guid, bool autoStart = false)
        {
            ServerInstance instance = new ServerInstance(exePath, guid, autoStart);
            servers.Add(instance);
            OnPropertyChanged(nameof(servers));
            return instance;
        }

    }
}

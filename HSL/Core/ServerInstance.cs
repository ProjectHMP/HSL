using HSL.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Xml;

namespace HSL.Core
{


    public class ServerInstance : INotifyPropertyChanged, IDisposable
    {

        public class ResourceMeta
        {
            public string Name { get; set; }
            public bool IsEnabled { get; set; } = false;
        }

        internal ServerData ServerData { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;
        public ServerState State { get; private set; } = ServerState.Stopped;
        public List<ResourceMeta> Resources { get; private set; }
        public Dictionary<string, ResourceMeta> ResourceMap { get; private set; }

        public List<string> ServerLog { get; private set; }

        public bool AutoStart
        {
            get => ServerData.auto_start;
            set
            {
                ServerData.auto_start = value;
                OnPropertyChanged(nameof(AutoStart));
                ServerManager.MarkConfigDirty();
            }
        }

        public bool AutoRestart
        {
            get => ServerData.auto_restart;
            set
            {
                ServerData.auto_restart = value;
                OnPropertyChanged(nameof(AutoRestart));
                ServerManager.MarkConfigDirty();
            }
        }

        public bool AutoReloadResources
        {
            get => ServerData.auto_reload_resources;
            set
            {
                ServerData.auto_reload_resources = value;
                OnPropertyChanged(nameof(AutoReloadResources));
                ServerManager.MarkConfigDirty();
            }
        }

        public TimeSpan RestartTimer
        {
            get => ServerData.restart_timer;
            set
            {
                ServerData.restart_timer = value;
                OnPropertyChanged(nameof(RestartTimer));
                ServerManager.MarkConfigDirty();
            }
        }

        public uint RestartTimer_Hours
        {
            get => (uint)(RestartTimer.Days * 24 + RestartTimer.Hours);
            set
            {
                RestartTimer = TimeSpan.FromHours(value).Add(TimeSpan.FromMinutes(RestartTimer.Minutes).Add(TimeSpan.FromSeconds(RestartTimer.Seconds)));
            }
        }

        public uint RestartTimer_Minutes
        {
            get => (uint)RestartTimer.Minutes;
            set
            {
                RestartTimer = TimeSpan.FromMinutes(value).Add(TimeSpan.FromHours(RestartTimer.Days * 24 + RestartTimer.Hours).Add(TimeSpan.FromSeconds(RestartTimer.Seconds)));
            }
        }
        public uint RestartTimer_Seconds
        {
            get => (uint)RestartTimer.Seconds;
            set
            {
                RestartTimer = TimeSpan.FromSeconds(value).Add(TimeSpan.FromHours(RestartTimer.Days * 24 + RestartTimer.Hours).Add(TimeSpan.FromMinutes(RestartTimer.Minutes)));
            }
        }

        public string Name
        {
            get => ServerSettings.Get<string>("hostname", "HappinessMP");
            set
            {
                ServerSettings.Set<string>("hostname", value);
                OnPropertyChanged(nameof(Name));
            }
        }

        public string Hostname
        {
            get => ServerSettings.Get<string>("hostname", "HappinessMP");
            set
            {
                ServerSettings.Set<string>("hostname", value);
                OnPropertyChanged(nameof(Hostname));
            }
        }

        public bool Listed
        {
            get => ServerSettings.Get<bool>("listed", false);
            set
            {
                ServerSettings.Set<bool>("listed", value);
                OnPropertyChanged(nameof(Listed));
            }
        }

        public int Port
        {
            get => ServerSettings.Get<int>("port", 9999);
            set
            {
                ServerSettings.Set<int>("port", Math.Min(65535, value));
                OnPropertyChanged(nameof(Port));
            }
        }

        public int MaxPlayers
        {
            get => ServerSettings.Get<int>("maxplayers", 100);
            set
            {
                ServerSettings.Set<int>("maxplayers", Math.Min(100, value));
                OnPropertyChanged(nameof(MaxPlayers));
            }
        }

        public Episode Episode
        {
            get => Episode.TryParse(ServerSettings.Get<string>("episode", "0"), out Episode episode) ? episode : Episode.IV;
            set
            {
                ServerSettings.Set<string>("episode", ((int)value).ToString());
                OnPropertyChanged(nameof(Episode));
            }
        }

        public bool Chat
        {
            get => ServerSettings.Get<bool>("chat", true);
            set
            {
                ServerSettings.Set<bool>("chat", value);
                OnPropertyChanged(nameof(Chat));
            }
        }

        public string Secret
        {
            get => ServerSettings.Get<string>("secret", "happy");
            set
            {
                ServerSettings.Set<string>("secret", value);
                OnPropertyChanged(nameof(Secret));
            }
        }

        public string HostAddress
        {
            get => ServerSettings.Get<string>("hostaddress", "::");
            set
            {
                ServerSettings.Set<string>("hostaddress", value);
                OnPropertyChanged(nameof(HostAddress));
            }
        }

        public LogLevel LogLevel
        {
            get => LogLevel.TryParse(ServerSettings.Get<string>("loglevel", "2"), out LogLevel level) ? level : LogLevel.Info;
            set
            {
                ServerSettings.Set<string>("loglevel", ((int)value).ToString());
                OnPropertyChanged(nameof(LogLevel));
            }
        }

        internal string ServerDirectory { get; private set; }
        internal string ResourceDirectory { get; private set; }
        internal string ExePath
        {
            get => ServerData.exe_file;
            set
            {
                ServerData.exe_file = value;
                ServerManager.MarkConfigDirty();
            }
        }

        internal readonly string LogFile;
        internal readonly string ServerSettingsFile;

        internal ServerManager ServerManager;
        internal Guid Guid => ServerData.guid;

        internal ServerSettings ServerSettings { get; private set; }

        internal event EventHandler<string> StdOutput;
        internal event EventHandler ProcessStarted, ProcessStopped, ServerUpdated;

        internal Process process { get; set; }
        private Task _task { get; set; }
        private CancellationTokenSource _cts, _resourceCts;
        private FileSystemWatcher _resourceWatcher;
        private DateTime _StartTime = DateTime.Now;

        private bool _wasForcedClosed = false;
        private bool _documentSave = false;
        private object _stdOutLock = new object();
        private object _resourceListLock = new object();
        private object _serverLogLock = new object();

        internal ServerInstance(ServerManager manager, ServerData data)
        {
            ServerManager = manager;
            ServerData = data;
            ServerDirectory = Path.GetDirectoryName(data.exe_file);
            LogFile = ServerDirectory.CombinePath("server.log");
            ResourceDirectory = ServerDirectory.CombinePath("resources");
            ServerSettingsFile = ServerDirectory.CombinePath("settings.xml");
            State = ServerState.Stopped;

            ServerSettings = new ServerSettings(ServerSettingsFile);

            Resources = new List<ResourceMeta>();
            ResourceMap = new Dictionary<string, ResourceMeta>();
            ServerLog = new List<string>();

            _resourceWatcher = new FileSystemWatcher(ServerDirectory)
            {
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.DirectoryName,
                IncludeSubdirectories = true
            };

            _resourceWatcher.Changed += _watcherHandler;
            _resourceWatcher.Created += _watcherHandler;
            _resourceWatcher.Deleted += _watcherHandler;
            _resourceWatcher.Renamed += _watcherHandler;

            SyncServerSettings();
            RefreshServerInformation();

            if (AutoStart)
            {
                Start();
            }
        }

        private bool SyncServerSettings()
        {
            lock (_resourceListLock)
            {
                foreach (XmlNode resource in ServerSettings.GetNodes("resource"))
                {
                    Trace.WriteLine("Found XML Resource: " + resource.InnerText);

                    if (!ResourceMap.ContainsKey(resource.InnerText))
                    {
                        ResourceMap.Add(resource.InnerText, new ResourceMeta { Name = resource.InnerText, IsEnabled = true });
                        Resources.Add(ResourceMap[resource.InnerText]);
                    }
                }
            }
            OnPropertyChanged(nameof(Resources));
            return true;
        }

        private void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

        private void RefreshServerInformation()
        {
            lock (_resourceListLock)
            {
                // grab resource directory names, that has a valid meta.xml file (later validations)
                var _resources = Directory.GetFileSystemEntries(ResourceDirectory).Where(path => File.Exists(path.CombinePath("meta.xml"))).Select(Path.GetFileName);

                // remove resources that no longer exist
                Resources.RemoveAll(x => _resources.Contains(x.Name) ? false : ResourceMap.Remove(x.Name));

                // add new resources
                foreach (var resource in _resources)
                {
                    if (!ResourceMap.ContainsKey(resource))
                    {
                        ResourceMap.Add(resource, new ResourceMeta { Name = resource, IsEnabled = false });
                        Resources.Add(ResourceMap[resource]);
                    }
                }

                OnPropertyChanged(nameof(Resources));
                // OnPropertyChanged(nameof(ResourceMap));
            }
        }

        internal void ClearServerLog()
        {
            lock (_serverLogLock)
            {
                ServerLog.Clear();
                OnPropertyChanged(nameof(ServerLog));
            }
        }

        private async void _watcherHandler(object sender, FileSystemEventArgs e)
        {

            if (_resourceCts != null && !_resourceCts.Token.CanBeCanceled)
            {
                return;
            }

            _resourceCts = new CancellationTokenSource();

            await Task.Delay(500);

            if (e.FullPath != ServerSettingsFile)
            {
                try
                {
                    if (e.FullPath.IndexOf(ResourceDirectory) >= 0)
                    {
                        Match match = Regex.Match(e.FullPath, @"[\\\/]{1}resources[\\\/]{1}(.*)[\\\/]{1}?");
                        if (match.Success && match.Groups.Count > 0)
                        {
                            RefreshServerInformation();
                            if (AutoReloadResources && IsProcessRunning() && File.Exists(ResourceDirectory.CombinePath(match.Groups[1].Value, "meta.xml")))
                            {
                                ReloadResource(match.Groups[1].Value);
                            }
                        }
                    }
                }
                catch { }
            }
            else
            {

                if (!_documentSave)
                {
                    _documentSave = true;
                    ServerSettings.RefreshDocument();
                    ServerUpdated?.Invoke(null, null);
                }
                else _documentSave = false;
            }
            _resourceCts.CancelAfter(500);
        }

        internal bool IsProcessRunning() => process != null && !process.HasExited && _cts != null && !_cts.IsCanceled();

        internal bool Stop()
        {
            if (IsProcessRunning())
            {
                _wasForcedClosed = true;
                Dispose();
            }
            State = ServerState.Stopped;
            OnPropertyChanged(nameof(State));
            return true;
        }

        internal async void Restart()
        {
            if (Stop())
            {
                await Task.Delay(1000);
                Start();
            }
        }

        internal void ReloadAllResources()
        {
            lock (_resourceListLock)
            {
                foreach (string resource in ResourceMap.Keys)
                {
                    ReloadResource(resource);
                }
            }
        }

        internal void StartAllResources()
        {
            lock (_resourceListLock)
            {
                foreach (string resource in ResourceMap.Keys)
                {
                    StartResource(resource);
                }
            }
        }

        internal void StopAllResources()
        {
            lock (_resourceListLock)
            {
                foreach (string resource in ResourceMap.Keys)
                {
                    StopResource(resource);
                }
            }
        }

        internal void ReloadResource(string name)
        {
            StopResource(name);
            StartResource(name);
        }

        internal void StartResource(string name)
        {
            if (SendInput("start " + name) && ResourceMap.ContainsKey(name))
            {
                ResourceMap[name].IsEnabled = true;
                OnPropertyChanged(nameof(Resources));
                OnPropertyChanged(nameof(ResourceMap));
            }
        }
        internal void StopResource(string name)
        {
            if (SendInput("stop " + name) && ResourceMap.ContainsKey(name))
            {
                ResourceMap[name].IsEnabled = false;
                OnPropertyChanged(nameof(Resources));
                OnPropertyChanged(nameof(ResourceMap));
            }
        }

        internal bool Start()
        {

            if (IsProcessRunning())
            {
                Trace.WriteLine("Process already running");
                return false;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            IEnumerable<Process> processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExePath)).Where(x => x.MainModule.FileName == ExePath);

            if (processes != null && processes.Count() > 0)
            {

                if (MessageBox.Show("This server is already running. Do you want to terminate and start new?", "Hmm..", MessageBoxButton.YesNo) == MessageBoxResult.No)
                {
                    return false;
                }

                foreach (Process p in processes)
                {
                    if (p.MainModule.FileName == ExePath)
                    {
                        Trace.WriteLine("Killing active process: " + p.ProcessName + " & " + p.MainModule.FileName);
                        p.Kill();
                    }
                }
            }

            process = new Process()
            {
                StartInfo = {
                    FileName = ExePath,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true,
            };

            process.Disposed += (s, e) => _cts?.Dispose();
            process.Exited += async (s, e) =>
            {
                Dispose();
                ProcessStopped?.Invoke(null, null);

                if (AutoRestart && !_wasForcedClosed)
                {
                    _wasForcedClosed = false;
                    await Task.Delay(1500);
                    Start();
                }

            };

            processes = null;

            if (process.Start())
            {
                _StartTime = DateTime.Now;
                State = ServerState.Started;
                OnPropertyChanged(nameof(State));
                ProcessStarted?.Invoke(null, null);
                _task = new Task(async () => await ServerUpdateThread(), _cts.Token);
                _task.Start();
                return true;
            }
            else Trace.WriteLine("Failed to start process: " + Name);
            State = ServerState.Stopped;
            OnPropertyChanged(nameof(State));
            return false;
        }

        internal bool SendInput(string data)
        {
            if (IsProcessRunning())
            {
                lock (_stdOutLock)
                {
                    //process.StandardInput.Flush();
                    process.StandardInput.WriteLine(data);
                    return true;
                }
            }
            return false;
        }

        private async Task<Task> ServerUpdateThread()
        {
            using (FileStream fs = File.Open(LogFile, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite))
            {
                long pos = fs.Length;
                fs.Position = pos;
                string buffer = "";
                using (StreamReader sr = new StreamReader(fs))
                {
                    while (_cts != null && !_cts.IsCanceled() && IsProcessRunning())
                    {
                        await fs.FlushAsync();
                        if ( pos != fs.Length)
                        {
                            buffer = await sr.ReadLineAsync();
                            if (!string.IsNullOrEmpty(buffer))
                            {
                                pos += buffer.Length;
                                lock (_serverLogLock)
                                {
                                    ServerLog.Add(buffer);
                                }
                                StdOutput?.Invoke(null, buffer);
                                OnPropertyChanged(nameof(ServerLog));
                                continue;
                            }
                        }

                        if (AutoRestart && DateTime.Now >= _StartTime.Add(RestartTimer))
                        {
                            break;
                        }

                        await Task.Delay(500);
                    }
                }
            }
            Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_cts?.Token.CanBeCanceled ?? false)
            {
                _cts.Cancel();
            }
            State = ServerState.Stopped;
            OnPropertyChanged(nameof(State));
            if (process != null && !process.HasExited)
            {
                process.Kill();
            }
            ClearServerLog();
        }

    }
}

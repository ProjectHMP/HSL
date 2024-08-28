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

namespace HSL
{

    public enum ServerState : byte
    {
        Stopped = 0x0,
        Started = 0x2,
        Restarting = 0x4
    }

    public enum Episode
    {
        IV = 0,
        TLAD = 1,
        TBOGT = 2
    }

    public class ServerInstance : INotifyPropertyChanged, IDisposable
    {

        public static Episode[] Episodes = new Episode[] { Episode.IV, Episode.TLAD, Episode.TBOGT }; 

        public class ResourceMeta
        {
            public string Name { get; set; }
            public bool IsEnabled { get; set; } = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public Guid Guid { get; private set; }
        public List<string> ServerLog { get; private set; }
        public string Name { get; private set; }
        public ServerState State { get; private set; } = ServerState.Stopped;
        public List<ResourceMeta> Resources { get; private set; }
        public Dictionary<string, ResourceMeta> ResourceMap { get; private set; }
        public bool StartAutomatically { get; set; } = false;
        public bool AutoReloadResources { get; set; } = false;

        private object _docSaveLock = new object();

        public string Hostname
        {
            get
            {
                if (Document != null)
                {
                    XmlNode node = Document.DocumentElement.SelectSingleNode("hostname");
                    if(node != null && !string.IsNullOrEmpty(node.InnerText))
                    {
                        return node.InnerText;
                    }
                }
                return "HappinessMP";
            }
            set
            {
                if (Document != null)
                {
                    XmlNode node = Document.DocumentElement.SelectSingleNode("hostname");
                    if (node == null)
                    {
                        node = Document.CreateNode("element", "hostname", "");
                        Document.DocumentElement.AppendChild(node);
                    }
                    node.InnerText = value;
                    OnPropertyChanged(nameof(Hostname));
                    lock (_docSaveLock)
                    {
                        Document.Save(ServerConfiguration);
                    }
                }
            }
        }

        public bool Listed
        {
            get
            {
                if(Document != null)
                {
                    XmlNode node = Document.DocumentElement.SelectSingleNode("listed");
                    if(node != null && !string.IsNullOrEmpty(node.InnerText) && bool.TryParse(node.InnerText, out bool b))
                    {
                        return b;
                    }
                }
                return false;
            }
            set {
                if(Document != null)
                {
                    XmlNode node = Document.DocumentElement.SelectSingleNode("listed");
                    if (node == null)
                    {
                        node = Document.CreateNode("element", "listed", "");
                        Document.DocumentElement.AppendChild(node);
                    }
                    node.InnerText = value.ToString().ToLower();
                    OnPropertyChanged(nameof(Listed));
                    lock (_docSaveLock)
                    {
                        Document.Save(ServerConfiguration);
                    }
                }
            }
        }

        public int Port
        {
            get
            {
                if (Document != null)
                {
                    XmlNode node = Document.DocumentElement.SelectSingleNode("port");
                    if (node != null && !string.IsNullOrEmpty(node.InnerText) && int.TryParse(node.InnerText, out int b))
                    {
                        return b;
                    }
                }
                return 9999;
            }
            set
            {
                if (Document != null)
                {
                    XmlNode node = Document.DocumentElement.SelectSingleNode("port");
                    if (node == null)
                    {
                        node = Document.CreateNode("element", "port", "");
                        Document.DocumentElement.AppendChild(node);
                    }
                    node.InnerText = value.ToString().ToLower();
                    OnPropertyChanged(nameof(Port));
                    lock (_docSaveLock)
                    {
                        Document.Save(ServerConfiguration);
                    }
                }
            }
        }

        public int MaxPlayers
        {
            get
            {
                if (Document != null)
                {
                    XmlNode node = Document.DocumentElement.SelectSingleNode("maxplayers");
                    if (node != null && !string.IsNullOrEmpty(node.InnerText) && int.TryParse(node.InnerText, out int b))
                    {
                        return b;
                    }
                }
                return 100;
            }
            set
            {
                if (Document != null)
                {
                    XmlNode node = Document.DocumentElement.SelectSingleNode("maxplayers");
                    if (node == null)
                    {
                        node = Document.CreateNode("element", "maxplayers", "");
                        Document.DocumentElement.AppendChild(node);
                    }
                    node.InnerText = Math.Min(100, value).ToString();
                    OnPropertyChanged(nameof(MaxPlayers));
                    lock (_docSaveLock)
                    {
                        Document.Save(ServerConfiguration);
                    }
                }
            }
        }

        public Episode Episode
        {
            get
            {
                if (Document != null)
                {
                    XmlNode node = Document.DocumentElement.SelectSingleNode("episode");
                    if (node != null && !string.IsNullOrEmpty(node.InnerText) && Enum.TryParse<Episode>(node.InnerText, out Episode b))
                    {
                        return b;
                    }
                }
                return Episode.IV;
            }
            set
            {
                if (Document != null)
                {
                    XmlNode node = Document.DocumentElement.SelectSingleNode("episode");
                    if (node == null)
                    {
                        node = Document.CreateNode("element", "episode", "");
                        Document.DocumentElement.AppendChild(node);
                    }
                    node.InnerText = ((int)value).ToString();
                    OnPropertyChanged(nameof(Episode));
                    lock (_docSaveLock)
                    {
                        Document.Save(ServerConfiguration);
                    }
                }
            }
        }

        public TimeSpan RestartTimer { get; private set; } = TimeSpan.Zero;

        internal string ServerDirectory { get; private set; }
        internal string ResourceDirectory { get; private set; }
        internal string ExePath { get; private set; }
        internal string LogFile { get; private set; }
        internal string ServerConfiguration { get; private set; }

        private XmlDocument Document = new XmlDocument();

        internal event EventHandler<string> StdOutput;
        internal event EventHandler ProcessStarted, ProcessStopped;

        internal Process process { get; set; }
        private Task _task { get; set; }
        private CancellationTokenSource _cts, _resourceCts;
        private FileSystemWatcher _resourceWatcher;
        private DateTime _StartTime = DateTime.Now;

        private object _stdOutLock = new object();
        private object _resourceListLock = new object();
        private object _serverLogLock = new object();

        internal ServerInstance(string serverExe, Guid guid, bool autoStart = false)
        {
            Guid = guid;
            ExePath = serverExe;
            StartAutomatically = autoStart;
            ServerDirectory = Path.GetDirectoryName(serverExe);
            LogFile = ServerDirectory.CombineAsPath("server.log");
            ResourceDirectory = ServerDirectory.CombineAsPath("resources");
            ServerConfiguration = ServerDirectory.CombineAsPath("settings.xml");
            State = ServerState.Stopped;
            Resources = new List<ResourceMeta>();
            ResourceMap = new Dictionary<string, ResourceMeta>();
            ServerLog = new List<string>();
            _resourceWatcher = new FileSystemWatcher(ResourceDirectory)
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

            if (autoStart)
            {
                Start();
            }
        }

        private bool SyncServerSettings()
        {

            if (!File.Exists(ServerConfiguration))
                return false;

            Document = new XmlDocument();
            Document.Load(ServerConfiguration);

            Name = Document.DocumentElement.SelectSingleNode("hostname").InnerText;
            foreach(XmlNode resource in Document.DocumentElement.SelectNodes("resource"))
            {

                Trace.WriteLine("Found XML Resource: " + resource.InnerText);

                if(!ResourceMap.ContainsKey(resource.InnerText))
                {
                    ResourceMap.Add(resource.InnerText, new ResourceMeta { Name = resource.InnerText, IsEnabled = true });
                    Resources.Add(ResourceMap[resource.InnerText]);
                }
            }

            OnPropertyChanged(nameof(Resources));
            OnPropertyChanged(nameof(ResourceMap));

            return true;
        }

        private void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

        private void RefreshServerInformation()
        {
            lock (_resourceListLock)
            {
                // grab resource directory names, that has a valid meta.xml file (later validations)
                var _resources = Directory.GetFileSystemEntries(ResourceDirectory).Where(path => File.Exists(path.CombineAsPath("meta.xml"))).Select(Path.GetFileName);

                // remove resources that no longer exist
                Resources.RemoveAll(x => _resources.Contains(x.Name) ? false : ResourceMap.Remove(x.Name));
                
                // add new resources
                foreach(var resource in _resources)
                {
                    if(!ResourceMap.ContainsKey(resource))
                    {
                        ResourceMap.Add(resource, new ResourceMeta { Name = resource, IsEnabled = false });
                        Resources.Add(ResourceMap[resource]);
                    }
                }

                OnPropertyChanged(nameof(Resources));
                OnPropertyChanged(nameof(ResourceMap));
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

        private void _watcherHandler(object sender, FileSystemEventArgs e)
        {
            /*
             * Due too an old "issue/bug" with FSW, it can send duplicate events at a time.
             * Using Monitor (lock), we instead want to capture one call per 100 ms, and discard any other (duplicates).
             */

            if (_resourceCts != null && !_resourceCts.Token.CanBeCanceled)
            {
                return;
            }

            _resourceCts = new CancellationTokenSource();

            try
            {
                // reload resource if was enabled
                Match match = Regex.Match(e.FullPath, @"[\\\/]{1}Resources[\\\/]{1}(.*)[\\\/]{1}?");
                if (match.Success && match.Groups.Count > 0)
                {
                    RefreshServerInformation();

                    // reload resource if was enabled
                    // make sure this is a valid resource (will add more checks later)
                    if(AutoReloadResources && IsProcessRunning() && File.Exists(ResourceDirectory.CombineAsPath(match.Groups[1].Value, "meta.xml")))
                    {
                        SendInput("stop " + match.Groups[1].Value);
                        SendInput("start " + match.Groups[1].Value);
                    }

                    Trace.WriteLine("Resource Updating: " + match.Groups[1].Value);
                }
            }
            catch { }

            try
            {
                _resourceCts.CancelAfter(500);
            }
            catch { }
        }

        internal bool IsProcessRunning() => process != null && !process.HasExited && _cts != null && !_cts.IsCanceled();

        internal bool Stop()
        {
            if (IsProcessRunning())
            {
                Dispose();
            }
            State = ServerState.Stopped;
            OnPropertyChanged(nameof(State));
            return true;
        }

        internal async void Restart()
        {
            if(Stop())
            {
                await Task.Delay(1000);
                Start();
            }
        }

        internal void ReloadAllResources()
        {
            lock(_resourceListLock)
            {
                foreach(string resource in ResourceMap.Keys)
                {
                    ReloadResource(resource);
                }
            }
        }

        internal void StartAllResources()
        {
            lock (_resourceListLock)
            {
                foreach(string resource in ResourceMap.Keys)
                {
                    StartResource(resource);
                }
            }
        }

        internal void StopAllResources()
        {
            lock(_resourceListLock)
            {
                foreach(string resource in ResourceMap.Keys)
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

        internal void StartResource(string name) {
            if(SendInput("start " + name) && ResourceMap.ContainsKey(name))
            {
                ResourceMap[name].IsEnabled = true;
                OnPropertyChanged(nameof(Resources));
                OnPropertyChanged(nameof(ResourceMap));
            }
        }
        internal void StopResource(string name) {
            if(SendInput("stop " + name) && ResourceMap.ContainsKey(name))
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

            // delete old server log
            string tmpLog = LogFile + ".tmp";
            if (File.Exists(LogFile))
            {
                if (File.Exists(tmpLog))
                {
                    File.Delete(tmpLog);
                }
                File.Copy(LogFile, tmpLog);
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
            process.Exited += (s, e) =>
            {
                State = ServerState.Stopped;
                _cts?.Cancel();
                ProcessStopped?.Invoke(null, null);
            };

            processes = null;

            if (process.Start())
            {
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
            lock (_stdOutLock)
            {
                if (IsProcessRunning())
                {
                    //process.StandardInput.Flush();
                    process.StandardInput.WriteLine(data);
                    return true;
                }
                return false;
            }
        }

        private async Task<Task> ServerUpdateThread()
        {
            using (FileStream fs = File.Open(LogFile, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Position = fs.Length;
                using (StreamReader sr = new StreamReader(fs))
                {
                    long pos = fs.Length;
                    string buffer = "";
                    while (_cts != null && !_cts.IsCanceled() && IsProcessRunning())
                    {
                        await fs.FlushAsync();
                        if (pos != fs.Length)
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
                        await Task.Delay(500);
                    }
                }
            }
            Stop();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _cts?.Cancel();
            if (process != null && !process.HasExited)
            {
                process.Kill();
            }
            ClearServerLog();
        }

    }
}

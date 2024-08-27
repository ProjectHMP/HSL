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

    public class ServerInstance : INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public Guid Guid { get; private set; }
        public List<string> ServerLog { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public ServerState State { get; private set; } = ServerState.Stopped;
        public List<string> Resources { get; private set; } = new List<string>();
        public bool StartAutomatically { get; set; } = false;
        public bool AutoReloadResources { get; set; } = false;

        public TimeSpan RestartTimer { get; private set; } = TimeSpan.Zero;

        internal string ServerDirectory { get; private set; }
        internal string ResourceDirectory { get; private set; }
        internal string ExePath { get; private set; }
        internal string LogFile { get; private set; }

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
            State = ServerState.Stopped;
            Resources = new List<string>();
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

            string xml = ServerDirectory.CombineAsPath("settings.xml");
            if (File.Exists(xml))
            {
                XmlDocument document = new XmlDocument();
                document.Load(xml);
                Name = document.DocumentElement.SelectSingleNode("hostname").InnerText;
            }

            RefreshServerInformation();

            if (autoStart)
            {
                Start();
            }
        }

        private void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

        private void RefreshServerInformation()
        {
            lock (_resourceListLock)
            {
                Resources.Clear();
                var _resources = Directory.GetFileSystemEntries(ResourceDirectory).Where(path => File.Exists(path.CombineAsPath("meta.xml"))).Select(Path.GetFileName);
                foreach (var resource in _resources)
                {
                    Resources.Add(resource);
                }
                OnPropertyChanged(nameof(Resources));
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
            _resourceWatcher?.Dispose();
            _cts?.Cancel();
            if (process != null && !process.HasExited)
            {
                process.Kill();
            }
            ClearServerLog();
        }

    }
}

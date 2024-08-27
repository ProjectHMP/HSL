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

        public List<string> serverLog { get; private set; }
        public string Name { get; private set; }
        public ServerState state { get; private set; }
        public List<string> resources { get; private set; }

        internal Guid Guid { get; private set; }
        internal string servDir { get; private set; }
        internal string exeFile { get; private set; }
        internal string logFile { get; private set; }
        internal string resDir { get; private set; }

        internal event EventHandler<string> StdOutput;
        internal event EventHandler ProcessStarted, ProcessStopped;

        internal Process process { get; set; }
        private Task task { get; set; }
        private CancellationTokenSource cts, _resourceCts;
        private FileSystemWatcher resourceWatcher;

        private object _stdOutLock = new object();
        private object _resourceListLock = new object();
        private object _serverLogLock = new object();

        public ServerInstance(string serverExe, Guid guid, bool autoStart = false)
        {
            Guid = guid;
            exeFile = serverExe;
            servDir = Path.GetDirectoryName(serverExe);
            logFile = servDir.CombineAsPath("server.log");
            resDir = servDir.CombineAsPath("resources");
            state = ServerState.Stopped;
            resources = new List<string>();
            serverLog = new List<string>();

            resourceWatcher = new FileSystemWatcher(resDir)
            {
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.DirectoryName,
                IncludeSubdirectories = true
            };

            resourceWatcher.Changed += _watcherHandler;
            resourceWatcher.Created += _watcherHandler;
            resourceWatcher.Deleted += _watcherHandler;
            resourceWatcher.Renamed += _watcherHandler;

            string xml = servDir.CombineAsPath("settings.xml");
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
                resources.Clear();
                var _resources = Directory.GetFileSystemEntries(resDir).Where(path => File.Exists(path.CombineAsPath("meta.xml"))).Select(Path.GetFileName);
                foreach (var resource in _resources)
                {
                    resources.Add(resource);
                }
                OnPropertyChanged(nameof(resources));
                Trace.WriteLine("Resources Updated");
            }
        }

        internal void ClearServerLog()
        {
            lock (_serverLogLock)
            {
                serverLog.Clear();
                OnPropertyChanged(nameof(serverLog));
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
                Match match = Regex.Match(e.FullPath, @"[\\\/]{1}resources[\\\/]{1}(.*)[\\\/]{1}?");
                if (match.Success && match.Groups.Count > 0)
                {
                    RefreshServerInformation();

                    // reload resource if was enabled
                    /*
                    if (IsProcessRunning())
                    {
                        // reload resource if enabled
                        SendInput("stop " + match.Groups[1].Value);
                        SendInput("start " + match.Groups[1].Value);
                    }
                    */
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

        internal bool IsProcessRunning() => process != null && !process.HasExited && cts != null && !cts.IsCanceled();

        internal bool Stop()
        {
            if (IsProcessRunning())
            {
                Dispose();
            }
            state = ServerState.Stopped;
            OnPropertyChanged(nameof(state));
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

            cts?.Cancel();
            cts = new CancellationTokenSource();

            IEnumerable<Process> processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeFile)).Where(x => x.MainModule.FileName == exeFile);

            if (processes != null && processes.Count() > 0)
            {

                if (MessageBox.Show("This server is already running. Do you want to terminate and start new?", "Hmm..", MessageBoxButton.YesNo) == MessageBoxResult.No)
                {
                    return false;
                }

                foreach (Process p in processes)
                {
                    if (p.MainModule.FileName == exeFile)
                    {
                        Trace.WriteLine("Killing active process: " + p.ProcessName + " & " + p.MainModule.FileName);
                        p.Kill();
                    }
                }
            }

            // delete old server log
            string tmpLog = logFile + ".tmp";
            if (File.Exists(logFile))
            {
                if (File.Exists(tmpLog))
                {
                    File.Delete(tmpLog);
                }
                File.Copy(logFile, tmpLog);
            }

            process = new Process()
            {
                StartInfo = {
                    FileName = exeFile,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true,
            };

            process.Disposed += (s, e) => cts?.Dispose();
            process.Exited += (s, e) =>
            {
                state = ServerState.Stopped;
                cts?.Cancel();
                ProcessStopped?.Invoke(null, null);
            };

            processes = null;

            if (process.Start())
            {
                state = ServerState.Started;
                OnPropertyChanged(nameof(state));
                ProcessStarted?.Invoke(null, null);
                task = new Task(async () => await ServerUpdateThread(), cts.Token);
                task.Start();
                return true;
            }
            else Trace.WriteLine("Failed to start process: " + Name);
            state = ServerState.Stopped;
            OnPropertyChanged(nameof(state));
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
            using (FileStream fs = File.Open(logFile, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite))
            {

                // if failed to delete log file, we will advance pointer to end of file and read from there.
                long length = fs.Length;
                fs.Position = length;

                using (StreamReader sr = new StreamReader(fs))
                {
                    long cpos = length;
                    long pos = 0;
                    string buffer = "";
                    while (cts != null && !cts.IsCanceled() && IsProcessRunning())
                    {
                        await fs.FlushAsync();
                        pos = fs.Length;
                        if (pos > cpos)
                        {
                            buffer = await sr.ReadLineAsync();
                            if (!string.IsNullOrEmpty(buffer))
                            {
                                cpos += buffer.Length;
                                lock (_serverLogLock)
                                {
                                    serverLog.Add(buffer);
                                }
                                StdOutput?.Invoke(null, buffer);
                                OnPropertyChanged(nameof(serverLog));
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
            Trace.WriteLine("Dispose called for  " + Name);
            resourceWatcher?.Dispose();
            cts?.Cancel();
            if (process != null && !process.HasExited)
            {
                process.Kill();
            }
            ClearServerLog();
        }

    }
}

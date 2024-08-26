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

    [TypeConverter(typeof(Converters.EnumConverter))]
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
        public Guid Guid { get; private set; }
        public string Name { get; private set; }
        public ServerState state { get; private set; }
        public List<string> resources { get; private set; }
        public string servDir { get; private set; }
        public string exeFile { get; private set; }
        public string logFile { get; private set; }
        public string resDir { get; private set; }

        internal event EventHandler<string> StdOutput;
        internal event EventHandler Exited;
        internal event EventHandler ProcessStarted;

        internal XmlDocument XMLData;

        private Process serverProcess;
        private Task serverTask;
        private CancellationTokenSource cts;
        private FileSystemWatcher resourceWatcher;

        private object resourceWatcherLock = new object();
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
                XMLData = new XmlDocument();
                XMLData.Load(xml);
                Name = XMLData.DocumentElement.SelectSingleNode("hostname").InnerText;
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
                resources.AddRange(Directory.GetFileSystemEntries(resDir).Where(path => File.Exists(path.CombineAsPath("meta.xml"))).Select(Path.GetFileName));
                OnPropertyChanged(nameof(resources));
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
            if (!Monitor.TryEnter(resourceWatcherLock, 100))
            {
                return;
            }

            // reload resource if was enabled, 

            Match match = Regex.Match(e.FullPath, @"""[\\\/]resource[\\\/]([A-Za-z0-9\-\s]*)[\\\/]?""");
            if (match.Success && match.Groups.Count > 0)
            {
                Trace.WriteLine("Resource Updating: " + match.Groups[0].Value);
            }

            // MUST EXIT
            Monitor.Exit(resourceWatcher);
        }


        public void Dispose()
        {
            cts?.Cancel();
            if (serverProcess != null && !serverProcess.HasExited)
            {
                serverProcess.Kill();
            }
        }

        internal bool IsProcessRunning() => serverProcess != null && !serverProcess.HasExited && cts != null && !cts.IsCancellationRequested;
        internal bool Stop()
        {
            if (IsProcessRunning())
            {
                Dispose();
            }
            ClearServerLog();
            OnPropertyChanged(nameof(state));
            return true;
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

            Process[] processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeFile));

            if (processes != null)
            {

                if(MessageBox.Show("This server is already running. Do you want to terminate and start new?", "Hmm..", MessageBoxButton.YesNo) == MessageBoxResult.No)
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

            serverProcess = new Process()
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

            serverProcess.Disposed += (s, e) => cts?.Dispose();
            serverProcess.Exited += (s, e) =>
            {
                state = ServerState.Stopped;
                cts?.Cancel();
                Exited?.Invoke(null, null);
            };

            processes = null;

            if (serverProcess.Start())
            {
                state = ServerState.Started;
                OnPropertyChanged(nameof(state));
                ProcessStarted?.Invoke(null, null);
                serverTask = new Task(async () => await ServerUpdateThread(), cts.Token);
                serverTask.Start();
                return true;
            }
            else Trace.WriteLine("Failed to start process: " + Name);
            state = ServerState.Stopped;
            OnPropertyChanged(nameof(state));
            return false;
        }

        internal bool SendInput(string data)
        {
            if (IsProcessRunning())
            {
                serverProcess.StandardInput.Flush();
                serverProcess.StandardInput.WriteLine(data);
                return true;
            }
            return false;
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

                    while (cts != null && !cts.IsCanceled())
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

    }
}

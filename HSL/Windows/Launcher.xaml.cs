using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace HSL.Windows
{
    public partial class Launcher : Window, IDisposable, INotifyPropertyChanged
    {

        public ServerManager manager { get; private set; }
        public ServerInstance currentInstance { get; private set; } = default(ServerInstance);

        public event PropertyChangedEventHandler PropertyChanged;

        private HSLConfig config;
        private OpenFileDialog ofd;
        private object _configLock { get; set; } = new object();

        public void Dispose()
        {
            if(manager != null)
            {
                Dispatcher.Invoke(manager.Dispose);
            }
        }

        public Launcher()
        {
            InitializeComponent();

            AppDomain.CurrentDomain.UnhandledException += async (s, e) =>
            {
                Trace.WriteLine(e.ToString());
                string cr = Utils.CurrentDirectory.CombineAsPath("crash-report.txt");
                if(File.Exists(cr))
                {
                    File.Delete(cr);
                }
                await File.WriteAllTextAsync(cr, e.ToString());
            };

            Closing += (s, e) => Dispose();

            Trace.WriteLine("Loading Configuration");
            LoadConfiguration().ConfigureAwait(false).GetAwaiter(); // intentional thread lock
            Trace.WriteLine("Configuration Loaded");

            DataContext = this;

            manager = new ServerManager();
            manager.OnProcessStarted += Manager_OnProcessStarted;
            manager.OnProcessStopped += Manager_OnProcessStopped;
            manager.OnCreated += Manager_OnCreated;
            manager.OnDeleted += Manager_OnDeleted;

            // load servers

            bool markdirty = false;

            foreach (var e in config.servers)
            {
                // check if path is still valid.
                if(!Directory.Exists(Path.GetDirectoryName(e.Value.exe_file)) || !File.Exists(e.Value.exe_file))
                {
                    if(MessageBox.Show(String.Format("Failed to load pre-existing server: {0}{1}Would you like to change location?", "Error", MessageBoxButton.YesNo)) == MessageBoxResult.Yes)
                    {
                        ofd ??= new OpenFileDialog() {
                            Multiselect = false,
                            Filter = "HappinessMP.Server.Exe | *.exe"
                        };

                        if(!(ofd?.ShowDialog() ?? false) || string.IsNullOrEmpty(ofd.FileName) || config.servers.Any(x => x.Value.exe_file == ofd.FileName))
                        {
                            continue;
                        }
                        config.servers[e.Key].exe_file = ofd.FileName;
                        markdirty = true;
                    }
                }
                manager.Create(e.Value.exe_file, e.Value.guid, e.Value.auto_start);
            }

            if (markdirty)
            {
                config.Save().ConfigureAwait(false).GetAwaiter(); // intentional thread lock
            }
            RegisterListenered();
        }

        private async Task LoadConfiguration() {
            config = await HSLConfig.Load("hsl.json");
        }

        private async void Manager_OnDeleted(object sender, ServerInstance e)
        {
            if(config.servers.ContainsKey(e.Guid))
            {
                config.servers.Remove(e.Guid);
                await config.Save();
            }
        }

        private async void Manager_OnCreated(object sender, ServerInstance e)
        {
            if (!config.servers.ContainsKey(e.Guid))
            {
                config.servers.Add(e.Guid, new HSLConfig.ServerConfig()
                {
                    exe_file = e.exeFile,
                    guid = e.Guid,
                });
            }
            await config.Save();
        }

        private async void Manager_OnProcessStopped(object sender, ServerInstance e)
        {
            if(config.servers.ContainsKey(e.Guid) && config.servers[e.Guid].auto_start) {
                await Task.Delay(1000);
                e.Start();
            }
        }

        private void Manager_OnProcessStarted(object sender, ServerInstance e)
        {
        }

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void ShowServerContext(ServerInstance instance)
        {
            if (currentInstance != null)
            {
                currentInstance.StdOutput -= null;
            }
            currentInstance = instance;
            OnPropertyChanged(nameof(currentInstance));
            rtb_ServerLog.ScrollToVerticalOffset(rtb_ServerLog.ActualHeight);
            currentInstance.StdOutput += (s, e) => Dispatcher.Invoke(() => rtb_ServerLog.ScrollToVerticalOffset(rtb_ServerLog.ActualHeight));
        }

        private void RegisterListenered()
        {

            lv_ServerList.SelectionChanged += (s, e) =>
            {
                if (lv_ServerList.SelectedItem != null && lv_ServerList.SelectedItem is ServerInstance instance)
                {
                    ShowServerContext(instance);
                }
            };

            tb_ServerCmd.KeyUp += (s, e) =>
            {
                if (e.Key == Key.Enter && currentInstance != null)
                {
                    currentInstance.SendInput(tb_ServerCmd.Text);
                    tb_ServerCmd.Clear();
                }
            };

            mi_OpenServerPath.Click += (s, e) =>
            {
                ofd ??= new OpenFileDialog();
                ofd.Filter = "HappinessMP.Server.Exe | *.exe";
                if (ofd.ShowDialog() ?? false)
                {
                    if (!File.Exists(Path.GetDirectoryName(ofd.FileName).CombineAsPath("settings.xml")))
                    {
                        MessageBox.Show("This path does not contain a valid HappinessMP server.", "Error", MessageBoxButton.OK);
                        return;
                    }
                    ShowServerContext(manager.Create(ofd.FileName, false));
                }
            };

            btn_ClearServerLog.Click += (s, e) => currentInstance?.ClearServerLog();

            btn_StartResource.Click += (s, e) =>
            {
                if (lv_ResourceList.SelectedIndex >= 0)
                {
                    if (currentInstance != null && lv_ResourceList.SelectedItem is string resource && !string.IsNullOrEmpty(resource))
                    {
                        currentInstance.SendInput("start " + resource);
                    }
                }
            };

            btn_StopResource.Click += (s, e) =>
            {
                if (currentInstance != null && lv_ResourceList.SelectedItem is string resource && !string.IsNullOrEmpty(resource))
                {
                    currentInstance.SendInput("stop " + resource);
                }
            };

            btn_ReloadResource.Click +=  (s, e) =>
            {
                if (currentInstance != null && lv_ResourceList.SelectedItem is string resource && !string.IsNullOrEmpty(resource))
                {
                    currentInstance.SendInput("stop " + resource);
                    currentInstance.SendInput("start " + resource);
                }
            };

            btn_StopAllResources.Click +=  (s, e) =>
            {
                if (currentInstance != null && currentInstance.resources.Count > 0)
                {
                    foreach (string resource in currentInstance.resources)
                    {
                        currentInstance.SendInput("stop " + resource);
                    }
                }
            };

            btn_ReloadAllResources.Click +=  (s, e) =>
            {
                if (currentInstance != null && currentInstance.resources.Count > 0)
                {
                    foreach (string resource in currentInstance.resources)
                    {
                        currentInstance.SendInput("stop " + resource);
                        currentInstance.SendInput("start " + resource);
                    }
                }
            };

            mi_StartServer.Click += (s, e) => currentInstance?.Start();

            mi_StopServer.Click += (s, e) => currentInstance?.Stop();

            mi_RestartServer.Click += async (s, e) =>
            {
                if (currentInstance != null)
                {
                    currentInstance.Stop();
                    await Task.Delay(1000);
                    currentInstance.Start();
                }
            };

            mi_DeleteServer.Click += (s, e) =>
            {
                if(MessageBox.Show("Are you sure you want to delete" + currentInstance.Name + "? Files will NOT be deleted!", "Delete Server?", MessageBoxButton.YesNo) == MessageBoxResult.No)
                {
                    return;
                }

                manager.Delete(currentInstance);
            };

            mi_CreateServer.Click += async (s, e) =>
            {

                // open a valid/empty path
                string directory = string.Empty;
                using (System.Windows.Forms.FolderBrowserDialog fbd = new System.Windows.Forms.FolderBrowserDialog())
                {
                    if (fbd.ShowDialog() != System.Windows.Forms.DialogResult.OK || string.IsNullOrEmpty(fbd.SelectedPath))
                    {
                        MessageBox.Show("No directory given to install server.", "Error", MessageBoxButton.OK);
                        return;
                    }
                    directory = fbd.SelectedPath;
                }

                if (!Utils.IsDirectoryEmpty(directory))
                {
                    MessageBox.Show("Directory given is not empty.");
                    return;
                }

                // make sure directory exists

                if (!Directory.Exists(directory))
                {
                    try
                    {
                        Directory.CreateDirectory(directory);
                    }
                    catch
                    {
                        MessageBox.Show("Failed to create server in given directory.");
                        return;
                    }
                }

                string url = await Utils.GetLatestServerURL();

                if (string.IsNullOrEmpty(url))
                {
                    MessageBox.Show("Failed to download server files");
                    return;
                }

                string filename = url.Split("/")[^1];

                byte[] _server_archive = await Utils.HTTP.GetBinaryAsync(url);

                if (_server_archive == null || _server_archive.Length < 1024)
                {
                    MessageBox.Show("Failed to download server files.");
                    return;
                }

                string tmpFolder = directory.CombineAsPath("temp");
                string zip = directory.CombineAsPath(filename);

                if(!Directory.Exists(tmpFolder))
                {
                    Directory.CreateDirectory(tmpFolder);
                }

                try
                {
                    using (FileStream fs = File.Open(zip, FileMode.OpenOrCreate))
                    {
                        await fs.WriteAsync(_server_archive, 0, _server_archive.Length);
                        string root = null;
                        using (ZipArchive archive = new ZipArchive(fs))
                        {
                            if (!archive.Entries.Any(x => x.Name.IndexOf(".exe") > 0))
                            {
                                MessageBox.Show("Failed to install server files 2.");
                                return;
                            }


                            root = archive.Entries[0].FullName;
                            for(int i = 1; i < archive.Entries.Count; i++)
                            {
                                string file = archive.Entries[i].FullName.Substring(root.Length);
                                string filepath = directory.CombineAsPath(file);
                                if (archive.Entries[i].Length == 0)
                                {
                                    // this may or may not cause issues in future.
                                    Directory.CreateDirectory(filepath);
                                    continue;
                                }
                                Trace.WriteLine("Extracting " + file);
                                archive.Entries[i].ExtractToFile(filepath);
                            }
                        }
                    }
                    Directory.Delete(tmpFolder);
                    File.Delete(zip);
                }
                catch (Exception ee)
                {
                    Trace.WriteLine(ee);
                    if (Directory.Exists(tmpFolder))
                    {
                        Directory.Delete(tmpFolder, true);
                    }
                    if(File.Exists(zip))
                    {
                        File.Delete(zip);
                    }
                    MessageBox.Show("Failed to install server files: " + ee.ToString(), "Line 2412");
                    return;
                }
                manager.Create(Directory.GetFiles(directory, "*.exe").FirstOrDefault(), false);
            };
        }
    }
}

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

        public event PropertyChangedEventHandler PropertyChanged;

        public ServerManager manager { get; private set; }
        public ServerInstance currentInstance { get; private set; } = default(ServerInstance);

        private HSLConfig _config;
        private OpenFileDialog _ofd;
        private object _configLock { get; set; } = new object();

        public Launcher()
        {
            InitializeComponent();

            AppDomain.CurrentDomain.UnhandledException += async (s, e) =>
            {
                string cr = Utils.CurrentDirectory.CombineAsPath("crash-report.txt");
                if (File.Exists(cr))
                {
                    File.Delete(cr);
                }
                await File.WriteAllTextAsync(cr, e.ToString());
            };

            Closing += (s, e) => Dispose();

            manager = new ServerManager();
            manager.OnProcessStarted += Manager_OnProcessStarted;
            manager.OnProcessStopped += Manager_OnProcessStopped;
            manager.OnCreated += Manager_OnCreated;
            manager.OnDeleted += Manager_OnDeleted;

            DataContext = this;

            LoadConfiguration().ConfigureAwait(false).GetAwaiter(); // intentional thread lock

            RegisterListeners();
        }

        private async Task LoadConfiguration()
        {
            _config = await HSLConfig.Load("hsl.json");

            bool markdirty = false;
            lock(_configLock)
            {
                foreach (var kvp in _config.servers)
                {
                    if (!Directory.Exists(Path.GetDirectoryName(kvp.Value.exe_file)) || !File.Exists(kvp.Value.exe_file))
                    {
                        if (MessageBox.Show(String.Format("Failed to load pre-existing server: {0}{1}Would you like to change location?", "Error", MessageBoxButton.YesNo)) == MessageBoxResult.Yes)
                        {
                            _ofd ??= new OpenFileDialog();
                            _ofd.Multiselect = false;
                            _ofd.Filter = "HappinessMP.Server.Exe | *.exe";
                            if (!(_ofd?.ShowDialog() ?? false) || string.IsNullOrEmpty(_ofd.FileName) || _config.servers.Any(x => x.Value.exe_file == _ofd.FileName))
                            {
                                continue;
                            }
                            _config.servers[kvp.Key].exe_file = _ofd.FileName;
                            markdirty = true;
                        }
                    }
                    manager.Create(kvp.Value.exe_file, kvp.Value.guid, kvp.Value.auto_start);
                }
            }

            if (markdirty)
            {
                await _config.Save();
            }

        }

        private async void Manager_OnDeleted(object sender, ServerInstance e)
        {
            if (_config.servers.ContainsKey(e.Guid))
            {
                _config.servers.Remove(e.Guid);
                await _config.Save();
            }
        }

        private async void Manager_OnCreated(object sender, ServerInstance e)
        {
            if (!_config.servers.ContainsKey(e.Guid))
            {
                _config.servers.Add(e.Guid, new HSLConfig.ServerConfig()
                {
                    exe_file = e.ExePath,
                    guid = e.Guid,
                });
                await _config.Save();
            }
        }

        private async void Manager_OnProcessStopped(object sender, ServerInstance e)
        {
            if (_config.servers.ContainsKey(e.Guid) && _config.servers[e.Guid].auto_start)
            {
                await Task.Delay(1000);
                e.Start();
            }
        }

        private void Manager_OnProcessStarted(object sender, ServerInstance e) {}

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

        private void RegisterListeners()
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
                _ofd ??= new OpenFileDialog();
                _ofd.Filter = "HappinessMP.Server.Exe | *.exe";
                if (_ofd.ShowDialog() ?? false)
                {
                    if (!File.Exists(Path.GetDirectoryName(_ofd.FileName).CombineAsPath("settings.xml")))
                    {
                        MessageBox.Show("This path does not contain a valid HappinessMP server.", "Error", MessageBoxButton.OK);
                        return;
                    }
                    
                    if(_config.servers.Any(x => x.Value.exe_file == _ofd.FileName))
                    {
                        MessageBox.Show("This server has already been added.");
                        return;
                    }
                    ShowServerContext(manager.Create(_ofd.FileName, false));
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

            btn_ReloadResource.Click += (s, e) =>
            {
                if (currentInstance != null && lv_ResourceList.SelectedItem is string resource && !string.IsNullOrEmpty(resource))
                {
                    currentInstance.SendInput("stop " + resource);
                    currentInstance.SendInput("start " + resource);
                }
            };

            btn_StopAllResources.Click += (s, e) =>
            {
                if (currentInstance != null && currentInstance.Resources.Count > 0)
                {
                    foreach (string resource in currentInstance.Resources)
                    {
                        currentInstance.SendInput("stop " + resource);
                    }
                }
            };

            btn_ReloadAllResources.Click += (s, e) =>
            {
                if (currentInstance != null && currentInstance.Resources.Count > 0)
                {
                    foreach (string resource in currentInstance.Resources)
                    {
                        currentInstance.SendInput("stop " + resource);
                        currentInstance.SendInput("start " + resource);
                    }
                }
            };


            (lv_ResourceList.ContextMenu = new System.Windows.Controls.ContextMenu()).Items.Add(new System.Windows.Controls.MenuItem() { Header = "Open Folder" });
            (lv_ServerList.ContextMenu = new System.Windows.Controls.ContextMenu()).Items.Add(new System.Windows.Controls.MenuItem() { Header = "Open Folder" });
            (lv_ResourceList.ContextMenu.Items[0] as System.Windows.Controls.MenuItem).Click += (s, e) =>
            {
                if(lv_ResourceList.SelectedIndex >= 0)
                {
                    Process.Start("explorer.exe", currentInstance.ResourceDirectory.CombineAsPath((string)lv_ResourceList.SelectedItem));
                }
            };
            (lv_ServerList.ContextMenu.Items[0] as System.Windows.Controls.MenuItem).Click += (s, e) => { 
                if(lv_ServerList.SelectedIndex >= 0 && lv_ServerList.SelectedItem is ServerInstance instance)
                {
                    Process.Start("explorer.exe", instance.ServerDirectory);
                }
            };

            mi_StartServer.Click += (s, e) => currentInstance?.Start();

            mi_StopServer.Click += (s, e) => currentInstance?.Stop();

            mi_RestartServer.Click += (s, e) => currentInstance?.Restart();

            mi_DeleteServer.Click += (s, e) =>
            {
                if (MessageBox.Show("Are you sure you want to delete" + currentInstance.Name + "? Files will NOT be deleted!", "Delete Server?", MessageBoxButton.YesNo) == MessageBoxResult.No)
                {
                    return;
                }
                manager.Delete(currentInstance);
            };

            mi_OpenServerDirectory.Click += (s, e) => Process.Start("explorer.exe", currentInstance.ServerDirectory);

            mi_CreateServer.Click += async (s, e) =>
            {

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
                    MessageBox.Show("Directory selected is not empty.");
                    return;
                }

                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string url = await Utils.GetLatestServerURL();

                if (string.IsNullOrEmpty(url))
                {
                    MessageBox.Show("Failed to download server files.");
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
                string zip = tmpFolder.CombineAsPath(filename + ".tmp");

                if (!Directory.Exists(tmpFolder))
                {
                    Directory.CreateDirectory(tmpFolder);
                }

                try
                {
                    await File.WriteAllBytesAsync(zip, _server_archive);
                    using(FileStream fs = File.Open(zip, FileMode.Open, FileAccess.Read))
                    {
                        using(ZipArchive archive = new ZipArchive(fs))
                        {
                            if(!archive.Entries.Any(x => x.Name.IndexOf(".exe") > 0))
                            {
                                throw new Exception("Failed to install server files.");
                            }

                            for(int i = 1; i < archive.Entries.Count; i++)
                            {
                                string destination = directory.CombineAsPath(archive.Entries[i].FullName.Substring(archive.Entries[0].FullName.Length));
                                if (archive.Entries[i].Length == 0)
                                {
                                    Directory.CreateDirectory(destination);
                                    continue;
                                }
                                archive.Entries[i].ExtractToFile(destination);
                            }
                        }
                        Directory.Delete(tmpFolder, true);
                    }
                }
                catch (Exception ee)
                {
                    if (Directory.Exists(tmpFolder))
                    {
                        Directory.Delete(tmpFolder, true);
                    }
                    MessageBox.Show("Failed to install server files: " + ee.ToString(), "Error", MessageBoxButton.OK);
                    return;
                }
                manager.Create(Directory.GetFiles(directory, "*.exe").FirstOrDefault(), false);
            };
        }

        public void Dispose()
        {
            if (manager != null)
            {
                Dispatcher.Invoke(manager.Dispose);
            }
        }
    }
}

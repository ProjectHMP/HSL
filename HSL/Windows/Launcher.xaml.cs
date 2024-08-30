using HSL.Core;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Input;

namespace HSL.Windows
{
    public partial class Launcher : Window, IDisposable, INotifyPropertyChanged
    {

        public event PropertyChangedEventHandler PropertyChanged;

        public ServerManager manager { get; private set; }
        public ServerInstance currentInstance { get; private set; } = default(ServerInstance);
        public ServerInstance.ResourceMeta currentResource { get; private set; } = default(ServerInstance.ResourceMeta);

        internal HSLConfig Config { get; private set; }
        private OpenFileDialog _ofd;
        private object _configLock { get; set; } = new object();
        private Timer _timer;


        public Launcher()
        {
            InitializeComponent();

            AppDomain.CurrentDomain.UnhandledException += async (s, e) =>
            {
                string cr = Utils.CurrentDirectory.CombinePath("crash-reports.txt");
                if (File.Exists(cr))
                {
                    File.Delete(cr);
                }

                await File.AppendAllTextAsync(cr, "--------------------------------------" + Environment.NewLine + ((Exception)e.ExceptionObject).ToString());

                if(e.IsTerminating)
                {
                    Dispose();
                }
            };

            Closing += (s, e) => Dispose();

            manager = new ServerManager(this);
            manager.OnCreated += Manager_OnCreated;
            manager.OnDeleted += Manager_OnDeleted;

            DataContext = this;
            _timer = new Timer() { Enabled = true, Interval = 2500 };

            LoadConfiguration().ConfigureAwait(false).GetAwaiter(); // intentional thread lock

            if (manager.servers.Count > 0)
            {
                ShowServerContext(manager.servers.FirstOrDefault());
            }

            RegisterListeners();
            _timer.Start();
        }

        private async Task LoadConfiguration()
        {
            Config = await HSLConfig.Load("hsl.json");
            List<Guid> deleteCache = new List<Guid>();
            bool markdirty = false;
            lock (_configLock)
            {
                foreach (var key in Config.servers.Keys)
                {
                    if (!Directory.Exists(Path.GetDirectoryName(Config.servers[key].exe_file)) || !File.Exists(Config.servers[key].exe_file))
                    {
                        if (MessageBox.Show(String.Format("Failed to load pre-existing server: {0}{1}Would you like to change location?", Environment.NewLine, Config.servers[key].exe_file), "Error", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        {
                            _ofd ??= new OpenFileDialog();
                            _ofd.Multiselect = false;
                            _ofd.Filter = "HappinessMP.Server.Exe | *.exe";
                            if (!(_ofd?.ShowDialog() ?? false) || string.IsNullOrEmpty(_ofd.FileName) || Config.servers.Any(x => x.Value.exe_file == _ofd.FileName))
                            {
                                deleteCache.Add(key);
                                continue;
                            }
                            Config.servers[key].exe_file = _ofd.FileName;
                            markdirty = true;
                        }
                    }
                    manager.Create(Config.servers[key]);
                }
            }

            foreach(Guid guid in deleteCache)
            {
                Config.servers.Remove(guid);
            }

            if (markdirty)
            {
                await Config.Save();
            }

        }

        private async void Manager_OnDeleted(object sender, ServerInstance e)
        {
            if (e == currentInstance)
            {
                ShowServerContext(null);
            }

            if (Config.servers.ContainsKey(e.Guid))
            {
                Config.servers.Remove(e.Guid);
                await Config.Save();
            }
        }

        private async void Manager_OnCreated(object sender, ServerInstance e)
        {
            if (!Config.servers.ContainsKey(e.Guid))
            {
                Config.servers.Add(e.Guid, e.ServerData);
                await Config.Save();
            }
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

            if (currentInstance != null)
            {
                currentInstance.StdOutput += (s, e) => Dispatcher.Invoke(() => {
                    rtb_ServerLog.UpdateLayout();
                    rtb_ServerLog.ScrollToEnd();
                    rtb_ServerLog.ScrollToVerticalOffset(double.MaxValue);
                });
            }

            Title = currentInstance != null ? String.Format("HSL - {0}", currentInstance.Name) : "Happiness Server Launcher";
        }

        private void RegisterListeners()
        {

            _timer.Elapsed += async (s, e) =>
            {
                if (manager.IsDirty(true))
                {
                    await Config.Save();
                }
            };

            lv_ServerList.SelectionChanged += (s, e) =>
            {
                if (lv_ServerList.SelectedItem != null && lv_ServerList.SelectedItem is ServerInstance instance)
                {
                    currentResource = null;
                    OnPropertyChanged(nameof(currentResource));
                    ShowServerContext(instance);
                }
            };

            lv_ResourceList.SelectionChanged += (s,e) => {
                if(lv_ResourceList.SelectedItem is ServerInstance.ResourceMeta meta)
                {
                    currentResource = meta;
                    OnPropertyChanged(nameof(currentResource));
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
                    if (!File.Exists(Path.GetDirectoryName(_ofd.FileName).CombinePath("settings.xml")))
                    {
                        MessageBox.Show("This path does not contain a valid HappinessMP server.", "Error", MessageBoxButton.OK);
                        return;
                    }

                    if (Config.servers.Any(x => x.Value.exe_file == _ofd.FileName))
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
                if (currentInstance != null && lv_ResourceList.SelectedItem is ServerInstance.ResourceMeta meta && !string.IsNullOrEmpty(meta.Name))
                {
                    currentInstance?.StartResource(meta.Name);
                }
            };

            btn_StopResource.Click += (s, e) =>
            {
                if (currentInstance != null && lv_ResourceList.SelectedItem is ServerInstance.ResourceMeta meta && !string.IsNullOrEmpty(meta.Name))
                {
                    currentInstance?.StopResource(meta.Name);
                }
            };

            btn_ReloadResource.Click += (s, e) =>
            {
                if (currentInstance != null && lv_ResourceList.SelectedItem is ServerInstance.ResourceMeta meta && !string.IsNullOrEmpty(meta.Name))
                {
                    currentInstance?.ReloadResource(meta.Name);
                }
            };

            btn_StopAllResources.Click += (s, e) => currentInstance?.StopAllResources();

            btn_StartAllResources.Click += (s, e) => currentInstance?.StartAllResources();

            btn_ReloadAllResources.Click += (s, e) => currentInstance?.ReloadAllResources();

            (lv_ResourceList.ContextMenu = new System.Windows.Controls.ContextMenu()).Items.Add(new System.Windows.Controls.MenuItem() { Header = "Open Folder" });
            (lv_ServerList.ContextMenu = new System.Windows.Controls.ContextMenu()).Items.Add(new System.Windows.Controls.MenuItem() { Header = "Open Folder" });
            (lv_ResourceList.ContextMenu.Items[0] as System.Windows.Controls.MenuItem).Click += (s, e) =>
            {
                if (lv_ResourceList.SelectedIndex >= 0 && lv_ResourceList.SelectedItems is ServerInstance.ResourceMeta meta)
                {
                    Process.Start("explorer.exe", currentInstance.ResourceDirectory.CombinePath(meta.Name));
                }
            };
            (lv_ServerList.ContextMenu.Items[0] as System.Windows.Controls.MenuItem).Click += (s, e) =>
            {
                if (lv_ServerList.SelectedIndex >= 0 && lv_ServerList.SelectedItem is ServerInstance instance)
                {
                    Process.Start("explorer.exe", instance.ServerDirectory);
                }
            };

            mi_StartServer.Click += (s, e) => currentInstance?.Start();

            mi_StopServer.Click += (s, e) => currentInstance?.Stop(true);

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

            mi_UpdateServer.Click += async (s, e) =>
            {

                if (currentInstance == null)
                {
                    return;
                }

                if (currentInstance.State != Enums.ServerState.Stopped)
                {
                    MessageBox.Show("Please stop the server first before updating!");
                    return;
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

                string zip = currentInstance.ServerDirectory.CombinePath(filename + ".tmp");

                if (File.Exists(zip))
                {
                    File.Delete(zip);
                }

                try
                {
                    await File.WriteAllBytesAsync(zip, _server_archive);

                    string version = string.Empty;

                    using (FileStream fs = File.Open(zip, FileMode.Open, FileAccess.Read))
                    {
                        using (ZipArchive archive = new ZipArchive(fs))
                        {
                            if (!archive.Entries.Any(x => x.Name.IndexOf(".exe") > 0))
                            {
                                throw new Exception("Failed to install server files.");
                            }

                            version = archive.Entries[0].FullName;

                            for (int i = 1; i < archive.Entries.Count; i++)
                            {
                                if (archive.Entries[i].FullName.IndexOf("resources") >= 0 || archive.Entries[i].Name == "settings.xml")
                                {
                                    continue;
                                }

                                string file = currentInstance.ServerDirectory.CombinePath(archive.Entries[i].FullName.Substring(archive.Entries[0].FullName.Length));

                                if (File.Exists(file))
                                {
                                    File.Delete(file);
                                }
                                archive.Entries[i].ExtractToFile(file);
                            }
                        }
                    }

                    File.Delete(zip);

                    MessageBox.Show("Updated Server: " + version);

                }
                catch (Exception ee)
                {
                    if(File.Exists(zip))
                    {
                        File.Delete(zip);
                    }
                    MessageBox.Show("Failed to install server files: " + ee.ToString(), "Error", MessageBoxButton.OK);
                }

            };

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

                    if (!ServerInstance.IsValidInstallation(fbd.SelectedPath))
                    {
                        MessageBox.Show("This path has no valid HMP server installation. Did you mean Create Server instead?", "Oops", MessageBoxButton.OK);
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

                string zip = directory.CombinePath(filename + ".tmp");

                if (!File.Exists(zip))
                {
                    File.Delete(zip);
                }

                try
                {
                    await File.WriteAllBytesAsync(zip, _server_archive);
                    using (FileStream fs = File.Open(zip, FileMode.Open, FileAccess.Read))
                    {
                        using (ZipArchive archive = new ZipArchive(fs))
                        {
                            if (!archive.Entries.Any(x => x.Name.IndexOf(".exe") > 0))
                            {
                                throw new Exception("Corrupted server download. Aborted");
                            }

                            for (int i = 1; i < archive.Entries.Count; i++)
                            {
                                string destination = directory.CombinePath(archive.Entries[i].FullName.Substring(archive.Entries[0].FullName.Length));
                                if (archive.Entries[i].Length == 0)
                                {
                                    Directory.CreateDirectory(destination);
                                    continue;
                                }
                                archive.Entries[i].ExtractToFile(destination);
                            }
                        }
                    }
                    File.Delete(zip);
                }
                catch (Exception ee)
                {
                    if (!File.Exists(zip))
                    {
                        File.Delete(zip);
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

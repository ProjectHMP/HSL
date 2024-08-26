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
    public partial class Launcher : Window, INotifyPropertyChanged
    {

        public ServerManager manager { get; private set; }
        public ServerInstance currentInstance { get; private set; } = default(ServerInstance);
        public event PropertyChangedEventHandler PropertyChanged;

        private OpenFileDialog ofd;

        public Launcher()
        {
            InitializeComponent();
            manager = new ServerManager();
            lv_ServerList.DataContext = manager;
            // test
            manager.Create(@"D:\Servers\ProjectHMP\HappinessMP.Server.exe", Guid.NewGuid(), false);

            menu_hmp.DataContext = this;
            RegisterListenered();
        }

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void ShowServerContext(ServerInstance instance)
        {

            if (currentInstance != null)
            {
                currentInstance.StdOutput -= null;
            }

            currentInstance = instance;
            rtb_ServerLog.ScrollToVerticalOffset(rtb_ServerLog.ActualHeight);
            currentInstance.StdOutput += (s, e) => Dispatcher.Invoke(() => rtb_ServerLog.ScrollToEnd());
            rtb_ServerLog.DataContext = instance;
            lv_ResourceList.ItemsSource = instance.resources;

            OnPropertyChanged(nameof(currentInstance));
        }

        private async void RegisterListenered()
        {

            Trace.WriteLine(await Utils.GetLatestServerURL());

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
                    lv_ServerList.ItemsSource = manager.servers;
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

            btn_ReloadResource.Click += async (s, e) =>
            {
                if (currentInstance != null && lv_ResourceList.SelectedItem is string resource && !string.IsNullOrEmpty(resource))
                {
                    currentInstance.SendInput("stop " + resource);
                    await Task.Delay(100);
                    currentInstance.SendInput("start " + resource);
                }
            };

            btn_StopAllResources.Click += async (s, e) =>
            {
                if (currentInstance != null && currentInstance.resources.Count > 0)
                {
                    foreach (string resource in currentInstance.resources)
                    {
                        currentInstance.SendInput("stop " + resource);
                        await Task.Delay(100);
                    }
                }
            };

            btn_ReloadAllResources.Click += async (s, e) =>
            {
                if (currentInstance != null && currentInstance.resources.Count > 0)
                {
                    foreach (string resource in currentInstance.resources)
                    {
                        currentInstance.SendInput("stop " + resource);
                        await Task.Delay(100);
                        currentInstance.SendInput("start " + resource);
                        await Task.Delay(100);
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
                MessageBox.Show(filename + " || " + url);
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

                string exe = Directory.GetFiles(directory, "*.exe").FirstOrDefault();
                manager.Create(exe, false);

            };

        }

    }
}

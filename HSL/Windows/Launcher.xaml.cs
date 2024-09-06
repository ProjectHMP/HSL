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
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Resources;
using System.Xml;

namespace HSL.Windows
{
    public partial class Launcher : Window, IDisposable, INotifyPropertyChanged
    {

        public event PropertyChangedEventHandler PropertyChanged;

        public ServerManager manager { get; private set; }
        public ServerInstance currentInstance { get; private set; } = default(ServerInstance);
        public ServerInstance.ResourceMeta currentResource { get; private set; } = default(ServerInstance.ResourceMeta);

        public List<Language> Languages { get; private set; }

        internal HSLConfig Config { get; private set; }
        private OpenFileDialog _ofd;
        private object _configLock { get; set; } = new object();
        private Timer _timer;

        public Launcher()
        {
            InitializeComponent();


            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Utils.AppendToCrashReport(((Exception)e.ExceptionObject).ToString());
                if (e.IsTerminating)
                {
                    Dispose();
                }
            };

            Closing += (s, e) => Dispose();

            manager = new ServerManager(this);
            _timer = new Timer() { Enabled = true, Interval = 2500 };

            RegisterListeners();
            LoadConfiguration().ConfigureAwait(false).GetAwaiter(); // intentional thread lock

            // Load Languages

            ResourceDictionary languages = (ResourceDictionary)Application.LoadComponent(new Uri("/HSL;component/Lang/languages.xaml", UriKind.Relative));
            Languages = new List<Language>();
            foreach (string key in languages.Keys)
            {
                Languages.Add(new HSL.Language() { Key = key, Name = languages[key].ToString() });
            }

            
            // Load External Language
            string external_language_file = Utils.CurrentDirectory.CombinePath("lang.xaml");
            if (File.Exists(external_language_file))
            {
                try
                {
                    UnloadLanguages();
                    Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary() { Source = new Uri(external_language_file) });
                    Config.lang = null;
                }
                catch(Exception e) { MessageBox.Show("Failed to load external language; " + e.ToString()); }
            }

            // Load Language
            else if(Config.lang != "en")
            {
                Trace.WriteLine("Loading Language: " + Config.lang);
                foreach (Language lang in Languages)
                {
                    if (Config.lang == lang.Key)
                    {
                        LoadLanguage(lang);
                        Trace.WriteLine("Language Loaded");
                        break;
                    }
                }
            }

            if (manager.servers.Count > 0)
            {
                ShowServerContext(manager.servers.FirstOrDefault());
            }

            DataContext = this;

            _timer.Start();
            Show();
        }

        private async Task LoadConfiguration()
        {
            Config = await HSLConfig.Load("hsl.json");
            bool markdirty = false;
            lock (_configLock)
            {
                Guid[] keys = Config.servers.Keys.ToArray();
                string directory;
                foreach (Guid key in keys)
                {
                    directory = Path.GetDirectoryName(Config.servers[key].exe_file);
                    if (!Directory.Exists(directory) || !ServerInstance.IsValidInstallation(directory))
                    {
                        if (MessageBox.Show(String.Format(Utils.GetLang("text_server_load_failed"), Config.servers[key].exe_file, Environment.NewLine, Config.servers[key].exe_file), "Error", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        {
                            _ofd ??= new OpenFileDialog();
                            _ofd.Multiselect = false;
                            _ofd.Filter = "HappinessMP.Server.Exe | *.exe";
                            if (!(_ofd?.ShowDialog() ?? false) || string.IsNullOrEmpty(_ofd.FileName) || Config.servers.Any(x => x.Value.exe_file == _ofd.FileName))
                            {
                                Config.servers.Remove(key);
                                continue;
                            }
                            Config.servers[key].exe_file = _ofd.FileName;
                            markdirty = true;
                        }
                        else Config.servers.Remove(key);
                    }
                    manager.Create(Config.servers[key]);
                }
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

        internal void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

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
                currentInstance.StdOutput += (s, e) => Dispatcher.Invoke(() =>
                {
                    rtb_ServerLog.UpdateLayout();
                    rtb_ServerLog.ScrollToEnd();
                    rtb_ServerLog.ScrollToVerticalOffset(double.MaxValue);
                });
            }

            Title = currentInstance != null ? String.Format("HSL - {0}", currentInstance.Name) : "Happiness Server Launcher";
        }

        private void UnloadLanguages()
        {
            ResourceDictionary[] dictionaries = Application.Current.Resources.MergedDictionaries.Where(r => r.Source != null && r.Source.AbsolutePath.IndexOf("pack://application:,,,/MahApps.Metro") < 0).ToArray();
            if(dictionaries != null && dictionaries.Count() > 0)
            {
                foreach(ResourceDictionary dictionary in dictionaries)
                {
                    Application.Current.Resources.MergedDictionaries.Remove(dictionary);
                }
            }
        }

        private async void LoadLanguage(Language lang)
        {
            if (lang == null || string.IsNullOrEmpty(lang.Key))
            {
                return;
            }
            try
            {
                ResourceDictionary language = (ResourceDictionary)Application.LoadComponent(new Uri($"/HSL;component/Lang/{lang.Key}.xaml", UriKind.Relative));
                if (language != null)
                {
                    UnloadLanguages();
                    Application.Current.Resources.MergedDictionaries.Add(language);
                    if(Config.lang != lang.Key)
                    {
                        Config.lang = lang.Key;
                        await Config.Save();
                    }
                    return;
                }
            }
            catch { };
            MessageBox.Show("Failed to load language");
        }

        private void RegisterListeners()
        {

            manager.OnCreated += Manager_OnCreated;
            manager.OnDeleted += Manager_OnDeleted;

            _timer.Elapsed += async (s, e) =>
            {
                if (manager.IsDirty(true))
                {
                    await Config.Save();
                }
            };

            mi_Language.Click += (s, e) =>
            {
                MenuItem mi = (MenuItem)e.OriginalSource;
                HSL.Language lang = (HSL.Language)mi.Header;
                if(lang != null)
                {
                    LoadLanguage(lang);
                }
            };

            mi_OpenGithub.Click += (s, e) => Process.Start("explorer.exe", "https://github.com/ProjectHMP/HSL");

            lv_ServerList.SelectionChanged += (s, e) =>
            {
                if (lv_ServerList.SelectedItem is ServerInstance instance)
                {
                    currentResource = null;
                    OnPropertyChanged(nameof(currentResource));
                    ShowServerContext(instance);
                }
            };

            lv_ResourceList.SelectionChanged += (s, e) =>
            {
                if (lv_ResourceList.SelectedItem is ServerInstance.ResourceMeta meta)
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
                _ofd.Multiselect = false;
                if (_ofd.ShowDialog() ?? false)
                {
                    if (!ServerInstance.IsValidInstallation(Path.GetDirectoryName(_ofd.FileName)))
                    {
                        MessageBox.Show(Utils.GetLang("text_invalid_server_location"), Utils.GetLang("text_error"), MessageBoxButton.OK);
                        return;
                    }

                    if (Config.servers.Any(x => x.Value.exe_file == _ofd.FileName))
                    {
                        MessageBox.Show(Utils.GetLang("text_server_already_added"));
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

            lv_ResourceList.ContextMenu = new ContextMenu();
            lv_ServerList.ContextMenu = new ContextMenu();

            lv_ResourceList.ContextMenu.Items.Add(new System.Windows.Controls.MenuItem() { Header = Utils.GetLang("text_open_folder") });
            lv_ServerList.ContextMenu.Items.Add(new System.Windows.Controls.MenuItem() { Header = Utils.GetLang("text_start") });
            lv_ServerList.ContextMenu.Items.Add(new System.Windows.Controls.MenuItem() { Header = Utils.GetLang("text_stop") });
            lv_ServerList.ContextMenu.Items.Add(new System.Windows.Controls.MenuItem() { Header = Utils.GetLang("text_restart") });
            lv_ServerList.ContextMenu.Items.Add(new System.Windows.Controls.MenuItem() { Header = Utils.GetLang("text_open_folder") });

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
                    instance.Start();
                }
            };
            (lv_ServerList.ContextMenu.Items[1] as System.Windows.Controls.MenuItem).Click += (s, e) =>
            {
                if (lv_ServerList.SelectedIndex >= 0 && lv_ServerList.SelectedItem is ServerInstance instance)
                {
                    instance.Stop(true);
                }
            };
            (lv_ServerList.ContextMenu.Items[2] as System.Windows.Controls.MenuItem).Click += (s, e) =>
            {
                if (lv_ServerList.SelectedIndex >= 0 && lv_ServerList.SelectedItem is ServerInstance instance)
                {
                    instance.Restart();
                }
            };
            (lv_ServerList.ContextMenu.Items[3] as System.Windows.Controls.MenuItem).Click += (s, e) =>
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
                if (MessageBox.Show(String.Format(Utils.GetLang("text_ask_server_delete"), currentInstance.Name), Utils.GetLang("text_delete_server"), MessageBoxButton.YesNo) == MessageBoxResult.No)
                {
                    return;
                }
                manager.Delete(currentInstance);
            };

            mi_DeleteServerCache.Click += (s, e) => currentInstance?.DeleteServerCache();

            mi_OpenServerDirectory.Click += (s, e) => Process.Start("explorer.exe", currentInstance.ServerDirectory);

            mi_UpdateServer.Click += async (s, e) =>
            {

                if (currentInstance == null)
                {
                    return;
                }

                if (currentInstance.State != Enums.ServerState.Stopped)
                {
                    MessageBox.Show(Utils.GetLang("text_stop_server_before_updating"), Utils.GetLang("text_caution"), MessageBoxButton.OK);
                    return;
                }


                string url = await Utils.GetLatestServerURL();

                if (string.IsNullOrEmpty(url))
                {
                    MessageBox.Show(Utils.GetLang("text_server_download_failed"), Utils.GetLang("text_error"), MessageBoxButton.OK);
                    return;
                }

                string filename = url.Split("/")[^1];

                byte[] _server_archive = await Utils.HTTP.GetBinaryAsync(url);

                if (_server_archive == null || _server_archive.Length < 1024)
                {
                    MessageBox.Show(Utils.GetLang("text_server_download_failed"), Utils.GetLang("text_error"), MessageBoxButton.OK);
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
                                throw new Exception(Utils.GetLang("text_server_install_failed"));
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

                    MessageBox.Show(Utils.GetLang("text_updated_server") + ": " + version);

                }
                catch (Exception ee)
                {
                    if (File.Exists(zip))
                    {
                        File.Delete(zip);
                    }
                    MessageBox.Show(Utils.GetLang("text_server_install_failed") + " " + ee.ToString(), Utils.GetLang("text_error"), MessageBoxButton.OK);
                }

            };

            mi_CreateServer.Click += async (s, e) =>
            {

                string directory = string.Empty;
                using (System.Windows.Forms.FolderBrowserDialog fbd = new System.Windows.Forms.FolderBrowserDialog())
                {
                    if (fbd.ShowDialog() != System.Windows.Forms.DialogResult.OK || string.IsNullOrEmpty(fbd.SelectedPath))
                    {
                        MessageBox.Show(Utils.GetLang("text_no_server_directory"), Utils.GetLang("text_error"), MessageBoxButton.OK);
                        return;
                    }

                    directory = fbd.SelectedPath;
                }

                if (!Utils.IsDirectoryEmpty(directory))
                {
                    MessageBox.Show(Utils.GetLang("text_directory_not_empty"));
                    return;
                }

                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string url = await Utils.GetLatestServerURL();

                if (string.IsNullOrEmpty(url))
                {
                    MessageBox.Show(Utils.GetLang("text_server_download_failed"), Utils.GetLang("text_error"), MessageBoxButton.OK);
                    return;
                }

                string filename = url.Split("/")[^1];

                byte[] _server_archive = await Utils.HTTP.GetBinaryAsync(url);

                if (_server_archive == null || _server_archive.Length < 1024)
                {
                    MessageBox.Show(Utils.GetLang("text_server_download_failed"), Utils.GetLang("text_error"), MessageBoxButton.OK);
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
                                throw new Exception(Utils.GetLang("text_corrupted_server_download"));
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
                    Utils.AppendToCrashReport(ee.ToString());
                    MessageBox.Show(Utils.GetLang("text_server_install_failed") + ": " + ee.ToString(), Utils.GetLang("text_error"), MessageBoxButton.OK);
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

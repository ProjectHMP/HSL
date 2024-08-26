using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.IO;
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
            manager.Create( @"D:\Servers\ProjectHMP\HappinessMP.Server.exe", Guid.NewGuid(), false);

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
                if(ofd.ShowDialog() ?? false)
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

            btn_StartResource.Click +=  (s, e) => { 
                if(lv_ResourceList.SelectedIndex >= 0)
                {
                    if (currentInstance != null && lv_ResourceList.SelectedItem is string resource && !string.IsNullOrEmpty(resource))
                    {
                        currentInstance.SendInput("start " + resource);
                    }
                }
            };

            btn_StopResource.Click += (s, e) => { 
                if(currentInstance != null && lv_ResourceList.SelectedItem is string resource && !string.IsNullOrEmpty(resource))
                {
                    currentInstance.SendInput("stop " + resource);
                }
            };

            btn_ReloadResource.Click += async (s, e) =>
            {
                if(currentInstance != null && lv_ResourceList.SelectedItem is string resource && !string.IsNullOrEmpty(resource))
                {
                    currentInstance.SendInput("stop " + resource);
                    await Task.Delay(100);
                    currentInstance.SendInput("start " + resource);
                }
            };

            btn_StopAllResources.Click += async (s, e) => { 
                if(currentInstance != null && currentInstance.resources.Count > 0)
                {
                    foreach(string resource in currentInstance.resources)
                    {
                        currentInstance.SendInput("stop " + resource);
                        await Task.Delay(100);
                    }
                }
            };

            btn_ReloadAllResources.Click += async (s, e) => {
                if(currentInstance != null && currentInstance.resources.Count > 0)
                {
                    foreach(string resource in currentInstance.resources)
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
                if(currentInstance != null)
                {
                    currentInstance.Stop();
                    await Task.Delay(1000);
                    currentInstance.Start();
                }
            };

        }

    }
}

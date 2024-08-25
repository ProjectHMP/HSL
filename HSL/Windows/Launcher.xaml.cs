using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace HSL.Windows
{
    public partial class Launcher : Window
    {

        private ServerManager manager;
        private ServerInstance currentInstance;

        public Launcher()
        {
            InitializeComponent();
            manager = new ServerManager();

            lv_ServerList.DataContext = manager;
            // test
            manager.Create( @"D:\Servers\ProjectHMP\HappinessMP.Server.exe", Guid.NewGuid(), true);
            RegisterListenered();
        }

        private void ShowServerContext(ServerInstance instance)
        {

            if (currentInstance != null)
            {
                currentInstance.StdOutput -= null;
            }

            currentInstance = instance;
            rtb_ServerLog.ScrollToVerticalOffset(rtb_ServerLog.ActualHeight);
            currentInstance.StdOutput += (s, e) => rtb_ServerLog.ScrollToEnd();
            rtb_ServerLog.DataContext = instance;
            lv_ResourceList.ItemsSource = instance.resources;
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
                if (currentInstance != null && e.Key == Key.Enter)
                {
                    currentInstance.SendInput(tb_ServerCmd.Text);
                    tb_ServerCmd.Text = string.Empty;
                }
            };

            mi_OpenServerPath.Click += (s, e) =>
            {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.Filter = "HappinessMP.Server.Exe | *.exe";
                if(ofd.ShowDialog() ?? false)
                {
                    if (!File.Exists(Path.GetDirectoryName(ofd.FileName).CombineAsPath("settings.xml")))
                    {
                        return;
                    }
                    ShowServerContext(manager.Create(ofd.FileName, false));
                    lv_ServerList.ItemsSource = manager.servers;
                }
            };

            btn_ClearServerLog.Click += (s, e) => { 
                if(currentInstance != null)
                {
                    currentInstance.ClearServerLog();
                }
            };

        }

    }
}

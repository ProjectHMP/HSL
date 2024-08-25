using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace HSL
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        /*
         * HSLConfig for HSL stuff
         */
        private HSLConfig config;
        /*
         * ServerInstance is in control of the server being managed.
         */
        private ServerInstance instance;

        public List<string> serverResources { get; private set; } = new List<string>();

        public MainWindow()
        {
            InitializeComponent();

            Closing += (s, e) => instance?.Dispose();

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Trace.WriteLine(e.ToString());
            };

            LoadConfiguration().ConfigureAwait(false).GetAwaiter().OnCompleted(() => Dispatcher.Invoke(() =>
            {
                instance = new ServerInstance(config.ServerExe, Guid.NewGuid(), true);
                instance.Exited += (s, e) => SendConsole("Process Closed");
                instance.ProcessStarted += (s, e) => SendConsole("Process Started");
                instance.StdOutput += Instance_StdOutput;
                cmdInput.KeyUp += CmdInput_KeyUp;
                instance.Start();

                listViewResources.ItemsSource = instance.resources;

                reloadAllResourcesBtn.Click += async (s, e) =>
                {
                    reloadAllResourcesBtn.IsEnabled = false;
                    for (int i = 0; i < serverResources.Count; i++)
                    {
                        instance.SendInput("stop " + serverResources[i]);
                        await Task.Delay(10);
                        instance.SendInput("start " + serverResources[i]);
                        await Task.Delay(10);
                    }
                    reloadAllResourcesBtn.IsEnabled = true;
                };

                reloadResourceBtn.Click += async (s, e) =>
                {
                    if (listViewResources.SelectedIndex >= 0)
                    {
                        reloadResourceBtn.IsEnabled = false;
                        string resource = (string)listViewResources.Items[listViewResources.SelectedIndex];
                        instance.SendInput("stop " + resource);
                        await Task.Delay(100);
                        instance.SendInput("start " + resource);
                        Trace.WriteLine(resource);
                        reloadResourceBtn.IsEnabled = true;
                    }
                };
                Trace.WriteLine("[+] " + listViewResources.SelectedIndex);
            }));
        }

        private async Task LoadConfiguration()
        {
            // this will create or load an existing configuration.
            config = await HSLConfig.Load("server.json");
            if (string.IsNullOrEmpty(config.ServerExe))
            {
                config.ServerExe = @"D:\Servers\ProjectHMP\HappinessMP.Server.exe";
                await config.Save();
            }
        }

        internal void SendConsole(string data)
        {
            rtbFlowDocumentParagraph.Inlines.Add(data + Environment.NewLine);
            rtb.ScrollToEnd();
        }

        private void CmdInput_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !string.IsNullOrEmpty(cmdInput.Text))
            {
                instance.SendInput(cmdInput.Text);
                cmdInput.Text = "";
            }
        }

        private void Instance_StdOutput(object sender, string e)
        {
            Dispatcher.Invoke(() => SendConsole(e));
        }
    }
}

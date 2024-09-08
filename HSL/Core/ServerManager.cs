using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace HSL.Core
{
    public class ServerManager : IDisposable, INotifyPropertyChanged
    {

        public event PropertyChangedEventHandler PropertyChanged;
        public ObservableCollection<ServerInstance> servers { get; private set; }

        internal event EventHandler<ServerInstance> OnCreated, OnDeleted, OnProcessStarted, OnProcessStopped;

        private bool _dirtyConfig = false;
        private object _serverLock = new object();
        private Windows.Launcher _launcher;
        private System.Timers.Timer _timer;

        internal ServerManager(Windows.Launcher launcher)
        {
            _launcher = launcher;
            servers = new ObservableCollection<ServerInstance>();
            _timer = new System.Timers.Timer() { Interval = 60000 * 5, Enabled = true, AutoReset = true };
            _timer.Elapsed += _timer_Elapsed;
        }

        private async void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            Utils.Revisions.RevisionInfo? revision = await Utils.GetLatestServerRevision();
            if(revision != null)
            {
                lock(_serverLock)
                {
                    foreach(ServerInstance instance in servers)
                    {
                        instance.CompareVersionHash(revision.hash);
                    }
                }
            }
        }

        internal void InvokeCompareVersionHash() => _timer_Elapsed(null, null);

        internal ServerInstance? Create(string exePath, bool autoStart = false) => Create(new ServerData() { exe_file = exePath, guid = Guid.NewGuid(), auto_start = autoStart });
        internal ServerInstance? Create(ServerData data)
        {
            try
            {
                ServerInstance instance = new ServerInstance(this, data);
                instance.ProcessStarted += (s, e) => HandleEvent(OnProcessStarted, instance);
                instance.ProcessStopped += (s, e) => HandleEvent(OnProcessStopped, instance);
                instance.ServerUpdated += (s, e) => OnPropertyChanged(nameof(servers));
                lock (_serverLock)
                {
                    servers.Add(instance);
                }
                OnCreated?.Invoke(null, instance);
                OnPropertyChanged(nameof(servers));
                return instance;
            }
            catch (Exception e)
            {
                Trace.WriteLine(e.ToString());
            }
            return null;
        }

        internal bool Delete(ServerInstance instance)
        {
            if (instance == null)
                return false;

            instance.Dispose();

            lock (_serverLock)
            {
                if (servers.Remove(instance))
                {
                    OnDeleted?.Invoke(null, instance);
                    OnPropertyChanged(nameof(servers));
                    return true;
                }
                return false;
            }
        }

        internal bool Delete(Guid guid) => Delete(servers.Where(x => x.Guid == guid).FirstOrDefault());

        internal void CompareVersionHashes(string hash)
        {
            lock (_serverLock)
            {
                foreach(ServerInstance instance in servers)
                {
                    instance.CompareVersionHash(hash);
                }
            }
        }

        private void HandleEvent(EventHandler<ServerInstance> handler, ServerInstance instance) => handler?.Invoke(this, instance);

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        internal void MarkConfigDirty() => _dirtyConfig = true;

        internal bool IsDirty(bool clean = false)
        {
            if (_dirtyConfig & clean)
            {
                _dirtyConfig = !_dirtyConfig;
                return true;
            }
            return _dirtyConfig;
        }

        private static async Task<bool> DownloadLatestArchive(string file, Utils.Revisions.RevisionInfo revision)
        {

            byte[] buffer = await Utils.HTTP.GetBinaryAsync(revision.url);
            if (buffer.Length != revision.size || buffer.Length <= 4 || buffer[0] != 0x50 || buffer[1] != 0x4B || buffer[2] != 0x03 || buffer[3] != 0x04)
            {
                MessageBox.Show(Utils.GetLang("text_corrupted_server_download"), Utils.GetLang("text_error"));
                return false;
            }

            using (MD5 md5 = MD5.Create())
            {
                byte[] b_hash = md5.ComputeHash(buffer);
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < b_hash.Length; i++)
                {
                    builder.Append(b_hash[i].ToString("x2"));
                }
                if (revision.hash != builder.ToString())
                {
                    MessageBox.Show("Failed to create instance because of invalid hash vs revision.", Utils.GetLang("text_error"));
                    return false;
                }
            }

            await File.WriteAllBytesAsync(file, buffer);
            return true;
        }

        internal static async Task<bool> UpdateInstance(ServerInstance instance)
        {
            if (!Directory.Exists(instance.ServerDirectory))
            {
                return false;
            }

            if (!ServerInstance.IsValidInstallation(instance.ServerDirectory))
            {
                MessageBox.Show("This instance cannot be updated because it's not a valid HMP directory.", Utils.GetLang("text_error"));
                return false;
            }

            Utils.Revisions.RevisionInfo? revision = await Utils.GetLatestServerRevision();

            if (revision == null)
            {
                MessageBox.Show(Utils.GetLang("text_server_download_failed"), Utils.GetLang("text_error"), MessageBoxButton.OK);
                return false;
            }

            string version_file = instance.ServerDirectory.CombinePath(".version");

            if (File.Exists(version_file) && await File.ReadAllTextAsync(version_file) == revision.hash)
            {
                MessageBox.Show("This instance is updated to latest version!", "Updated!", MessageBoxButton.OK);
                return true;
            }

            string zip = instance.ServerDirectory.CombinePath(".sever.tmp");

            if (!await DownloadLatestArchive(zip, revision))
            {
                return false;
            }

            try
            {
                string? version;
                using (FileStream fs = File.Open(zip, FileMode.Open, FileAccess.ReadWrite))
                {
                    using (ZipArchive archive = new ZipArchive(fs))
                    {
                        if (!archive.Entries.Any(x => x.Name.IndexOf(".exe") > 0))
                        {
                            throw new Exception(Utils.GetLang("text_corrupted_server_download"));
                        }

                        version = archive.Entries[0].FullName;

                        for (int i = 1; i < archive.Entries.Count; i++)
                        {
                            // ignore resource paths && settings.xml
                            if (archive.Entries[i].FullName.IndexOf("resources") >= 0 || archive.Entries[i].Name == "settings.xml")
                            {
                                continue;
                            }
                            string dest = instance.ServerDirectory.CombinePath(archive.Entries[i].FullName.Substring(version.Length));
                            if (archive.Entries[i].Length == 0)
                            {
                                Directory.CreateDirectory(dest);
                                continue;
                            }
                            Utils.DeleteFile(dest);
                            archive.Entries[i].ExtractToFile(dest);
                        }
                    }
                }

                Utils.DeleteFile(zip);

                // validate installation
                if (!ServerInstance.IsValidInstallation(instance.ServerDirectory))
                {
                    MessageBox.Show("Failed to validate server directory after update.", Utils.GetLang("text_error"));
                    return false;
                }

                // create a version file, with hash, later can determine if server needs an update or not
                await File.WriteAllTextAsync(version_file, revision.hash);
                instance.HasUpdate = false;
                MessageBox.Show(Utils.GetLang("text_updated_server") + ": " + version);
                return true;
            }
            catch (Exception e)
            {
                Utils.DeleteFile(zip);
                Utils.AppendToCrashReport(e.ToString());
                MessageBox.Show("Failed to update server");
            }
            return false;
        }

        internal static async Task<bool> CreateInstance(string directory)
        {

            if (!Directory.Exists(directory) || !Utils.IsDirectoryEmpty(directory))
            {
                return false;
            }

            Utils.Revisions.RevisionInfo? revision = await Utils.GetLatestServerRevision();

            if (revision == null)
            {
                MessageBox.Show(Utils.GetLang("text_server_download_failed"), Utils.GetLang("text_error"), MessageBoxButton.OK);
                return false;
            }

            string zip = directory.CombinePath(".sever.tmp");

            if (!await DownloadLatestArchive(zip, revision))
            {
                MessageBox.Show(Utils.GetLang("text_corrupted_server_download"), Utils.GetLang("text_error"), MessageBoxButton.OK);
                return false;
            }

            try
            {

                using (FileStream fs = File.Open(zip, FileMode.Open, FileAccess.ReadWrite))
                {
                    using (ZipArchive archive = new ZipArchive(fs))
                    {
                        if (!archive.Entries.Any(x => x.Name.IndexOf(".exe") > 0))
                        {
                            throw new Exception(Utils.GetLang("text_corrupted_server_download"));
                        }

                        for (int i = 1; i < archive.Entries.Count(); i++)
                        {
                            string dest = directory.CombinePath(archive.Entries[i].FullName.Substring(archive.Entries[0].FullName.Length));
                            if (archive.Entries[i].Length == 0)
                            {
                                Directory.CreateDirectory(dest);
                                continue;
                            }
                            archive.Entries[i].ExtractToFile(dest);
                        }
                    }
                }

                Utils.DeleteFile(zip);

                if (!ServerInstance.IsValidInstallation(directory))
                {
                    MessageBox.Show("Failed to validate server update. Uninstalling.", Utils.GetLang("text_error"));
                    Utils.DeleteDirectory(directory);
                    return false;
                }

                await File.WriteAllTextAsync(directory.CombinePath(".version"), revision.hash);

                return true;
            }
            catch (Exception e)
            {
                Utils.DeleteFile(zip);
                Utils.AppendToCrashReport(e.ToString());
                MessageBox.Show(Utils.GetLang("text_server_install_failed") + ": " + e.ToString(), Utils.GetLang("text_error"), MessageBoxButton.OK);
                return false;
            }

        }

        public void Dispose()
        {
            lock (_serverLock)
            {
                foreach (ServerInstance instance in servers)
                {
                    instance.Dispose();
                }
                servers.Clear();
            }
        }

    }
}

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace HSL.Core
{

    public class ServerData
    {
        public Guid guid { get; set; } = Guid.NewGuid();
        public string exe_file { get; set; } = string.Empty;
        public bool auto_start { get; set; } = false;
        public bool auto_reload_resources { get; set; } = false;
        public bool auto_restart { get; set; } = false;
        public bool auto_delete_logs { get; set; } = false;
        public TimeSpan restart_timer { get; set; } = TimeSpan.FromHours(24);

        public ServerData() { }

        public ServerData(Guid guid, string exe, bool auto_start)
        {
            this.guid = guid;
            this.exe_file = exe;
            this.auto_start = auto_start;
        }

    }

    internal class HSLConfig
    {

        [JsonIgnore]
        private string _fileName = string.Empty;

        [JsonIgnore]
        private CancellationTokenSource _cts;

        public Dictionary<Guid, ServerData> servers { get; set; } = new Dictionary<Guid, ServerData>();

        private HSLConfig() { }

        private HSLConfig(string file) => _fileName = file;

        internal static async Task<HSLConfig> Load(string file)
        {
            file = Utils.CurrentDirectory.CombinePath(file);
            bool exists = File.Exists(file);
            HSLConfig config = null;
            if (exists)
            {
                try
                {
                    config = Newtonsoft.Json.JsonConvert.DeserializeObject<HSLConfig>(await File.ReadAllTextAsync(file));
                    config._fileName = file;
                }
                catch
                {
                    string tmpFile = file + ".tmp";
                    if (File.Exists(tmpFile))
                    {
                        File.Delete(tmpFile);
                    }
                    File.Move(file, tmpFile);
                    config = new HSLConfig(file);
                    exists = false;
                }
            }
            config ??= new HSLConfig(file);
            if (!exists)
            {
                await config.Save();
            }
            return config;
        }

        internal async Task<bool> Save()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                return false;
            }
            _cts = new CancellationTokenSource();
            try
            {
                await File.WriteAllTextAsync(_fileName, Newtonsoft.Json.JsonConvert.SerializeObject(this));
            }
            catch { MessageBox.Show("Failed to save HSL configuration."); }

            _cts.CancelAfter(500);
            return false;
        }


    }
}

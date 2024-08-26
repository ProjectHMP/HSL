using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace HSL
{
    internal class HSLConfig
    {

        public class ServerConfig
        {
            public Guid guid { get; set; } = Guid.NewGuid();
            public string exe_file { get; set; } = string.Empty;
            public bool auto_start { get; set; } = false;
            public bool auto_reload_resources { get; set; } = false;
            public TimeSpan restar_timer { get; set; } = TimeSpan.Zero;
        }

        private string _fileName = string.Empty;
        private CancellationTokenSource save_cts;

        public Dictionary<Guid, ServerConfig> servers { get; set; } = new Dictionary<Guid, ServerConfig>();

        private HSLConfig() { }

        private HSLConfig(string file) => _fileName = file;

        internal static async Task<HSLConfig> Load(string file)
        {
            file = Utils.CurrentDirectory.CombineAsPath(file);
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
                await File.WriteAllTextAsync(file, Newtonsoft.Json.JsonConvert.SerializeObject(config));
            }
            return config;
        }

        internal async Task<bool> Save()
        {

            if (save_cts != null && !save_cts.IsCancellationRequested)
            {
                return false;
            }
            save_cts = new CancellationTokenSource();
            try
            {
                await File.WriteAllTextAsync(_fileName, Newtonsoft.Json.JsonConvert.SerializeObject(this));
            }
            catch { MessageBox.Show("Failed to save HSL configuration."); }

            save_cts.Cancel();
            return false;
        }


    }
}

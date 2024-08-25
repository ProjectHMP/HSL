using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace HSL
{
    internal class HSLConfig
    {

        private string _fileName = string.Empty;

        public string ServerExe { get; set; } = string.Empty;

        private HSLConfig()
        {

        }

        private HSLConfig(string file)
        {
            _fileName = file;
        }

        internal static async Task<HSLConfig> Load(string file)
        {
            file = Utils.CurrentDirectory.CombineAsPath(file);
            bool exists = File.Exists(file);
            HSLConfig config = null;
            using (FileStream fs = File.Open(file, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                if (exists && fs.Length >= 2) // {}, []
                {
                    try
                    {
                        config = await JsonSerializer.DeserializeAsync<HSLConfig>(fs);
                        config._fileName = file;
                    }
                    catch
                    {
                        string tmpFile = file + ".tmp";
                        if (File.Exists(tmpFile))
                        {
                            File.Delete(tmpFile);
                        }
                        using (FileStream tfs = File.Open(tmpFile, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                        {
                            byte[] buffer = new byte[fs.Length];
                            int read = 0;
                            int hasread = 0;
                            fs.Position = 0;
                            while (read != buffer.Length)
                            {
                                hasread = await fs.ReadAsync(buffer, read, buffer.Length - read);
                                if (hasread == 0)
                                    break;
                                read += hasread;
                            }
                        }
                        File.Move(file, file + ".tmp");
                        File.Delete(file);
                        config = new HSLConfig(file);
                        exists = false;
                    }
                }
                else config = new HSLConfig(file);
                if (!exists)
                {
                    fs.SetLength(fs.Position = 0);
                    fs.Seek(0, SeekOrigin.Begin);
                    await fs.FlushAsync();
                    await JsonSerializer.SerializeAsync<HSLConfig>(fs, config);
                    await fs.FlushAsync();
                }
            }
            return config;
        }

        internal async Task<bool> Save()
        {
            if (!string.IsNullOrEmpty(_fileName))
            {
                using (FileStream fs = File.Open(_fileName, FileMode.OpenOrCreate, FileAccess.Write))
                {
                    fs.SetLength(fs.Position = 0);
                    fs.Seek(0, SeekOrigin.Begin);
                    await fs.FlushAsync();
                    await JsonSerializer.SerializeAsync<HSLConfig>(fs, this);
                    await fs.FlushAsync();
                }
                return true;
            }
            return false;
        }


    }
}

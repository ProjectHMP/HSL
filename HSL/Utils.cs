using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static HSL.Utils;


namespace HSL
{
    internal static class Utils
    {

        internal readonly static string CurrentDirectory;
        internal readonly static string CrashReportPath;

        internal class Revisions
        {

            internal class RevisionInfo
            {

                internal class FileInfo
                {
                    public string name { get; set; }
                    public string hash { get; set; }
                }

                public string hash { get; set; }
                public string url { get; set; }
                public int size { get; set; }

                public FileInfo[] files { get; set; }

            }

            public string latest { get; set; } = null;
            public Dictionary<string, RevisionInfo> hashes { get; set; } = new Dictionary<string, RevisionInfo>();
        }

        internal class GithubRelease {
            internal class Asset {
                public string? url { get; set; }
                public string? node_id { get; set; }
                public string? name { get; set; }
                public int size { get; set; } = -1;
                public string? browser_download_url { get; set; }
            }
            public Asset[]? assets { get; set; }
            public dynamic? author { get; set; }
            public string? html_url { get; set; }
            public string? node_id { get; set; }
            public string? name { get; set; }
            public bool draft { get; set; } = false;
            public bool prerelease { get; set; } = false;
            public string? created_at { get; set; }
            public string? published_at { get; set; }
            public string? body { get; set; }
        }

        static Utils()
        {
            CurrentDirectory ??= Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            CurrentDirectory ??= Environment.CurrentDirectory;
            CurrentDirectory ??= AppDomain.CurrentDomain.BaseDirectory;
            // CurrentDirectory ??= Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            CrashReportPath = CurrentDirectory.CombinePath("crash-reports.txt");
        }

        internal static bool IsDirectoryEmpty(string directory) => Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0;

        internal static async Task<GithubRelease?> GetLatestRelease()
        {
            GithubRelease[] releases = await Utils.HTTP.GetAsync<GithubRelease[]>("https://api.github.com/repos/ProjectHMP/HSL/releases?per_page=1");
            return (releases != null && releases.Length > 0 && releases[0].draft == false) ? releases[0] : null;
        }

        internal static void AppendToCrashReport(string data)
        {
            data = Environment.NewLine + "[" + DateTime.Now.ToString() + "]" + data;
            File.AppendAllText(CrashReportPath, data);
            Trace.WriteLine(data);
        }

        internal static void DeleteFile(string file)
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        internal static void DeleteDirectory(string directory)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        internal static void Restart(int seconds)
        {
            string file = Utils.CurrentDirectory.CombinePath(Process.GetCurrentProcess().MainModule.FileName);
            Process.Start(new ProcessStartInfo()
            {
                FileName = "cmd.exe",
                Arguments = $"/C timeout {seconds} && \"{file}\"",
                CreateNoWindow = true,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }

        internal static async Task<bool> CheckUpdate()
        {
            string file = CurrentDirectory.CombinePath(Process.GetCurrentProcess().MainModule.FileName);
            string hash = GetFileHash(file);
            Utils.GithubRelease? release = await Utils.GetLatestRelease();
            if (release != null && !release.draft)
            {
                Utils.GithubRelease.Asset? asset = release.assets.FirstOrDefault(x => x.name.Contains("HSL.exe"));
                Utils.GithubRelease.Asset? md5 = release.assets.FirstOrDefault(x => x.name.Contains("HSL.md5"));
                if (asset != null && md5 != null)
                {
                    string newhash = await HTTP.GetStringAsync(md5.browser_download_url);
                    if (!hash.Equals(newhash) && MessageBox.Show("Would you like to update to " + release.name + "?", "Update", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    {
                        byte[] buffer = await Utils.HTTP.GetBinaryAsync(asset.browser_download_url);
                        string newfile = file + ".new";
                        Utils.DeleteFile(newfile);
                        await File.WriteAllBytesAsync(newfile, buffer);

                        if (!newhash.Equals(Utils.GetFileHash(newfile)))
                        {
                            Utils.DeleteFile(newfile);
                            MessageBox.Show("Update Failed", "Failed", MessageBoxButton.OK);
                            return false;
                        }

                        string[] args = new string[] {
                            "/C cls",
                            $"del \"{file}\"",
                            "timeout 1",
                            $"mv \"{newfile}\" \"{file}\"",
                            "timeout 1",
                            $"del \"{newfile}\"",
                            $"start \"\" \"{file}\"",
                            "exit"
                        };

                        Process.Start(new ProcessStartInfo()
                        {
                            FileName = "cmd.exe",
                            CreateNoWindow = false,
                            UseShellExecute = true,
                            Arguments = String.Join(" && ", args),
                            WindowStyle = ProcessWindowStyle.Hidden
                        });
                        return true;
                    }
                }
            }
            return false;
        }

        internal static async Task<Revisions.RevisionInfo?> GetLatestServerRevision()
        {
            Revisions revisions = await HTTP.GetAsync<Revisions>("https://raw.githubusercontent.com/ProjectHMP/HSL/hmp-server-revisions/versions.json");
            if (revisions != null && !string.IsNullOrEmpty(revisions.latest) && revisions.hashes.ContainsKey(revisions.latest))
            {
                revisions.hashes[revisions.latest].url = Uri.UnescapeDataString(revisions.hashes[revisions.latest].url);
                revisions.hashes[revisions.latest].hash = revisions.latest;
                revisions.hashes[revisions.latest].files = await HTTP.GetAsync<Revisions.RevisionInfo.FileInfo[]>($"https://raw.githubusercontent.com/ProjectHMP/HSL/hmp-server-revisions/versions/{revisions.latest}.json");
                return revisions.hashes[revisions.latest];
            }
            return null;
        }

        internal static string? GetFileHash(string file)
        {
            if(File.Exists(file))
            {
                using(FileStream fs = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] buffer = new byte[fs.Length];
                    int pos = 0;
                    int read = 0;
                    while(pos != buffer.Length)
                    {
                        read = fs.Read(buffer, read, buffer.Length - read);
                        if(read == 0)
                        {
                            break;
                        }
                        pos += read;
                    }
                    return GetBufferHash(ref buffer);
                }
            }
            return null;
        }

        internal static string GetBufferHash(ref byte[] buffer)
        {
            using(MD5 md5 = MD5.Create())
            {
                return String.Join("", md5.ComputeHash(buffer).Select(x => x.ToString("x2")));
            }
        }


        internal static string GetLang(string key)
        {
            if (Application.Current.Resources.MergedDictionaries[0].Contains(key))
            {
                return Application.Current.Resources.MergedDictionaries[0][key].ToString();
            }
            return key;
        }

        /*
         * My non sophisticated lazy implemented HTTP library. 
         */

        internal static class HTTP
        {

            private const uint STREAM_BUFFER_SIZE = 1024;

            private static HttpClientHandler _clientHandler;
            private static HttpClient _client;

            static HTTP()
            {
                _client = new HttpClient(_clientHandler = new HttpClientHandler()
                {
                    AllowAutoRedirect = true,
                    MaxAutomaticRedirections = 3,
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls11,
                }, false);

                _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36");
                _client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue() { NoCache = true };
            }


            internal static async Task<string> GetStringAsync(string url, Dictionary<string, string> headers = null) => Encoding.UTF8.GetString(await SendAsync<byte[]>(url, headers, HttpMethod.Get, null, false));
            internal static async Task<byte[]> GetAsync(string url, Dictionary<string, string> headers = null) => await SendAsync<byte[]>(url, headers, HttpMethod.Get);
            // internal static async Task<byte[]> PostAsync(string url, string payload = null, Dictionary<string, string> headers = null) => await SendAsync<byte[]>(url, headers, HttpMethod.Post);
            internal static async Task<T> GetAsync<T>(string url, Dictionary<string, string> headers = null) => await SendAsync<T>(url, headers, HttpMethod.Get);
            // internal static async Task<T> PostAsync<T>(string url, string payload = null, Dictionary<string, string> headers = null) => await SendAsync<T>(url, headers, HttpMethod.Post);
            internal static async Task<byte[]> GetBinaryAsync(string url, Dictionary<string, string> headers = null) => await SendAsync<byte[]>(url, headers, HttpMethod.Get, null, true);
            private static async Task<T> SendAsync<T>(string url, Dictionary<string, string> headers = null, HttpMethod method = null, Func<MemoryStream> middleware = null, bool binary = false)
            {
                HttpRequestMessage message = new HttpRequestMessage(method ?? HttpMethod.Get, url);

                if (headers != null)
                {
                    foreach (KeyValuePair<string, string> kvp in headers)
                    {
                        message.Headers.Add(kvp.Key, kvp.Value);
                    }
                }

                using (HttpResponseMessage response = await _client.SendAsync(message))
                {

                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                    {
                        // Trace.WriteLine($"FAILED ({response.StatusCode.ToString()}): {url}");
                        return default(T); 
                    }

                    // todo, check content size to be more efficient/async than dynamic
                    byte[] buffer;
                    int read = 0;
                    int size = 0;

                    using (Stream s = await response.Content.ReadAsStreamAsync())
                    {
                        if (binary)
                        {
                            using (BinaryReader reader = new BinaryReader(s))
                            {
                                buffer = new byte[reader.BaseStream.Length];
                                while (size != buffer.Length)
                                {
                                    read = reader.Read(buffer, size, buffer.Length - size);
                                    if (read <= 0)
                                    {
                                        break;
                                    }
                                    size += read;
                                }
                            }
                            return (T)((object)buffer);
                        }

                        using (MemoryStream ms = new MemoryStream())
                        {
                            buffer = new byte[STREAM_BUFFER_SIZE];
                            while (true)
                            {
                                read = await s.ReadAsync(buffer, 0, buffer.Length);
                                if (read <= 0)
                                {
                                    break;
                                }
                                await ms.WriteAsync(buffer, 0, read);
                                size += read;
                            }
                            ms.Position = 0;
                            ms.SetLength(size);
                            if (typeof(T) != typeof(byte[]))
                            {
                                return await System.Text.Json.JsonSerializer.DeserializeAsync<T>(ms);
                            }
                            buffer = ms.ToArray();
                        }
                        return (T)((object)buffer);
                    }
                }

            }

        }


    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

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
                public string hash { get; set; }
                public string url { get; set; }
                public int size { get; set; }
            }

            public string latest { get; set; } = null;
            public Dictionary<string, RevisionInfo> hashes { get; set; } = new Dictionary<string, RevisionInfo>();
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
            if(Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        internal static async Task<Revisions.RevisionInfo?> GetLatestServerRevision()
        {
            Revisions revisions = await HTTP.GetAsync<Revisions>("https://raw.githubusercontent.com/ProjectHMP/HSL/hmp-server-revisions/versions.json");
            if(revisions != null && !string.IsNullOrEmpty(revisions.latest) && revisions.hashes.ContainsKey(revisions.latest))
            {
                revisions.hashes[revisions.latest].url = Uri.UnescapeDataString(revisions.hashes[revisions.latest].url);
                return revisions.hashes[revisions.latest];
            }
            return null;
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

            static HTTP() {
                _client = new HttpClient(_clientHandler = new HttpClientHandler()
                {
                    AllowAutoRedirect = true,
                    MaxAutomaticRedirections = 3,
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls11,
                }, false);
                _client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue() { NoCache = true };
            }

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
                            if (typeof(T) != typeof(byte[]))
                            {
                                return await JsonSerializer.DeserializeAsync<T>(ms);
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

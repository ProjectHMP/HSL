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

        internal static async Task<string> GetLatestServerURL()
        {
            byte[] _html_content_buffer = await HTTP.GetAsync(@"https://happinessmp.net/docs/server/getting-started/");
            Match match = Regex.Match(Encoding.UTF8.GetString(_html_content_buffer), @"(https:\/\/happinessmp\.net\/files\/[A-Za-z0-9%_\.]*.zip)");
            _html_content_buffer = null; // i got the habit of doing this, why, in managed. i should start writing unsafe, and malloc instead heh.
            return match.Success ? Uri.UnescapeDataString(match.Groups[0].Value) : String.Empty;
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

            private static HttpClientHandler _clientHandler = new HttpClientHandler()
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls11,
            };

            private static HttpClient _client = new HttpClient(_clientHandler, false);
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

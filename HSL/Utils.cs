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

        internal static string CurrentDirectory;

        static Utils()
        {
            CurrentDirectory ??= Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            CurrentDirectory ??= Environment.CurrentDirectory;
            CurrentDirectory ??= AppDomain.CurrentDomain.BaseDirectory;
            CurrentDirectory ??= Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
        }

        public static bool IsDirectoryEmpty(string directory) => Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0;

        private static readonly string LatestUrlPattern = @"(https:\/\/happinessmp\.net\/files\/[A-Za-z0-9%_\.]*.zip)";
        public static async Task<string> GetLatestServerURL()
        {
            byte[] _html_content_buffer = await HTTP.GetAsync(@"https://happinessmp.net/docs/server/getting-started/");
            Match match = Regex.Match(Encoding.UTF8.GetString(_html_content_buffer), LatestUrlPattern);
            _html_content_buffer = null; // i got the habit of doing this, why, in managed. i should start writing unsafe, and malloc instead heh.
            return match.Success ? Uri.UnescapeDataString(match.Groups[0].Value) : String.Empty;
        }

        /*
         * My non sophisticated HTTP library. 
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
            internal static async Task<byte[]> GetAsync(string url, Dictionary<string, string> headers = null) => await SendAsync<byte[]>(url, HttpMethod.Get, headers);
            internal static async Task<byte[]> PostAsync(string url, Dictionary<string, string> headers = null) => await SendAsync<byte[]>(url, HttpMethod.Post, headers);
            internal static async Task<T> GetAsync<T>(string url, Dictionary<string, string> headers = null) => await SendAsync<T>(url, HttpMethod.Get, headers);
            internal static async Task<T> PostAsync<T>(string url, Dictionary<string, string> headers = null) => await SendAsync<T>(url, HttpMethod.Post, headers);
            internal static async Task<byte[]> GetBinaryAsync(string url, Dictionary<string, string> headers = null) => await SendAsync<byte[]>(url, HttpMethod.Get, headers, null, true);
            private static async Task<T> SendAsync<T>(string url, HttpMethod method = null, Dictionary<string, string> headers = null, Func<MemoryStream> middleware = null, bool binary = false)
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

                    // binary encoding 
                    if (binary)
                    {
                        using (BinaryReader reader = new BinaryReader(await response.Content.ReadAsStreamAsync()))
                        {
                            buffer = new byte[reader.BaseStream.Length];
                            while (true)
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

                    using (Stream s = await response.Content.ReadAsStreamAsync())
                    {
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

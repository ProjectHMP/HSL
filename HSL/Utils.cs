using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;


namespace HSL
{
    internal static class Utils
    {

        internal static string CurrentDirectory;

        static Utils()
        {
            CurrentDirectory ??= AppDomain.CurrentDomain.BaseDirectory;
            CurrentDirectory ??= Environment.CurrentDirectory;
            CurrentDirectory ??= Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            CurrentDirectory ??= Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
        }

    }
}

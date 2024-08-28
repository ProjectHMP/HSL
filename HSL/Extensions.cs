using System;
using System.IO;
using System.Threading;

namespace HSL
{
    internal static class Extensions
    {

        internal static bool IsCanceled(this CancellationTokenSource cts)
        {
            try
            {
                if (cts.Token.IsCancellationRequested)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    throw new Exception(); // fallback
                }
            }
            catch { return true; }
            return false;
        }

        internal static string CombinePath(this string s, string s1) => Path.Combine(s, s1);
        internal static string CombinePath(this string s, string s1, string s2) => Path.Combine(s, s1, s2);
        internal static string CombinePath(this string s, string s1, string s2, string s3) => Path.Combine(s, s1, s2, s3);

    }
}

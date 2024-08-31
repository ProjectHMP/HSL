using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;

namespace HSL
{
    internal static class Extensions
    {

        internal static bool IsCanceled(this CancellationTokenSource cts)
        {
            if (cts != null)
            {
                try
                {
                    if (cts?.Token.IsCancellationRequested ?? false)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        throw new Exception(); // fallback
                    }
                }
                catch { return true; }
                return false;
            }
            return true;
        }

        internal static void RemoveAll<T>(this ObservableCollection<T> collection, Predicate<T> pre)
        {
            for(int i = collection.Count - 1; i >= 0; i--)
            {
                if (pre(collection[i]))
                {
                    collection.RemoveAt(i);
                }
            }
        }

        internal static string CombinePath(this string s, string s1) => Path.Combine(s, s1);
        internal static string CombinePath(this string s, string s1, string s2) => Path.Combine(s, s1, s2);
        internal static string CombinePath(this string s, string s1, string s2, string s3) => Path.Combine(s, s1, s2, s3);

    }
}

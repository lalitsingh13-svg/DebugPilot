using System;
using System.Collections.Generic;
using System.Linq;

namespace DebugPilot.Core.Fixes
{
    /// <summary>
    /// Curated catalog of unambiguous, commonly used .NET base-class-library types. It is a lookup table, not a semantic
    /// model: a proposal built from it is always previewed and verified by a rebuild before it is trusted.
    /// </summary>
    public static class KnownNamespaces
    {
        private static readonly Dictionary<string, string> Plain = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> Generic = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> Both = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> LinqMethods = new Dictionary<string, string>(StringComparer.Ordinal);

        private static void Add(Dictionary<string, string> target, string ns, string names)
        {
            foreach (string name in names.Split(' ', ',', '\n', '\r'))
                if (name.Length > 0) target[name] = ns;
        }

        static KnownNamespaces()
        {
            Add(Both, "System", "Func Action Lazy Tuple Nullable Progress IProgress Predicate Comparison EventHandler ArraySegment IEquatable IComparable IObservable IObserver");
            Add(Plain, "System", "Console DateTime DateTimeOffset TimeSpan Guid Math Exception ArgumentException ArgumentNullException ArgumentOutOfRangeException InvalidOperationException NotImplementedException NotSupportedException OperationCanceledException ObjectDisposedException AggregateException FormatException Uri Environment Convert Random Array Version IDisposable StringComparison Type Attribute EventArgs Buffer GC Char Enum Activator IServiceProvider TimeZoneInfo");
            Add(Both, "System.Threading.Tasks", "Task ValueTask TaskCompletionSource");
            Add(Plain, "System.Threading.Tasks", "Parallel TaskScheduler TaskStatus");
            Add(Plain, "System.Threading", "CancellationToken CancellationTokenSource Thread ThreadPool Timeout SemaphoreSlim Interlocked Monitor Mutex ManualResetEvent AutoResetEvent ReaderWriterLockSlim");
            Add(Plain, "System.Text", "StringBuilder Encoding UTF8Encoding");
            Add(Plain, "System.Text.RegularExpressions", "Regex Match MatchCollection RegexOptions");
            Add(Plain, "System.IO", "File Path Directory StreamReader StreamWriter Stream FileStream FileInfo DirectoryInfo MemoryStream TextReader TextWriter StringReader StringWriter BinaryReader BinaryWriter FileMode FileAccess FileShare IOException FileNotFoundException DirectoryNotFoundException SearchOption");
            Add(Plain, "System.Net.Http", "HttpClient HttpResponseMessage HttpRequestMessage HttpMethod HttpContent StringContent HttpClientHandler HttpRequestException");
            Add(Plain, "System.Net", "HttpStatusCode IPAddress Dns");
            Add(Plain, "System.Text.Json", "JsonSerializer JsonDocument JsonElement JsonException JsonSerializerOptions");
            Add(Plain, "System.Globalization", "CultureInfo NumberStyles DateTimeStyles");
            Add(Plain, "System.Diagnostics", "Process ProcessStartInfo Stopwatch Debug Trace");
            Add(Plain, "System.ComponentModel", "INotifyPropertyChanged PropertyChangedEventArgs PropertyChangedEventHandler");
            Add(Plain, "System.Runtime.CompilerServices", "CallerMemberNameAttribute CallerFilePathAttribute CallerLineNumberAttribute");
            Add(Plain, "System.Reflection", "Assembly MethodInfo PropertyInfo FieldInfo BindingFlags");
            Add(Plain, "System.Security.Cryptography", "SHA256 SHA1 MD5 RandomNumberGenerator Aes");
            Add(Plain, "System.Xml.Linq", "XDocument XElement XAttribute");
            Add(Plain, "System.Linq", "Enumerable Queryable");
            Add(Plain, "System.Collections", "ArrayList Hashtable IEnumerable IEnumerator IList ICollection IDictionary Queue Stack");
            Add(Generic, "System.Collections.Generic", "List Dictionary HashSet SortedSet SortedDictionary SortedList Queue Stack LinkedList KeyValuePair IEnumerable IEnumerator IList ICollection IDictionary ISet IReadOnlyList IReadOnlyCollection IReadOnlyDictionary IComparer IEqualityComparer Comparer EqualityComparer");
            Add(Generic, "System.Collections.Concurrent", "ConcurrentDictionary ConcurrentQueue ConcurrentBag ConcurrentStack");
            Add(Generic, "System.Collections.ObjectModel", "ObservableCollection ReadOnlyCollection ReadOnlyDictionary");
            Add(Both, "System.Linq.Expressions", "Expression");
            Add(LinqMethods, "System.Linq", "Where Select SelectMany ToList ToArray ToDictionary ToHashSet ToLookup OrderBy OrderByDescending ThenBy ThenByDescending GroupBy First FirstOrDefault Last LastOrDefault Single SingleOrDefault Any All Sum Min Max Average Distinct Skip Take SkipWhile TakeWhile Concat Union Intersect Except Zip Aggregate Cast OfType ElementAt SequenceEqual Append Prepend");
        }

        /// <summary>Namespaces that define a type with this simple name and generic arity (empty when unknown).</summary>
        public static IReadOnlyList<string> FindType(string name, int genericArity)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(name)) return result;
            var primary = genericArity > 0 ? Generic : Plain;
            if (primary.TryGetValue(name, out var ns)) result.Add(ns);
            if (Both.TryGetValue(name, out var both) && !result.Contains(both)) result.Add(both);
            return result;
        }

        public static IReadOnlyList<string> FindExtensionMethod(string name)
        {
            if (!string.IsNullOrEmpty(name) && LinqMethods.TryGetValue(name, out var ns)) return new[] { ns };
            return Array.Empty<string>();
        }

        internal static int CatalogSize => Plain.Count + Generic.Count + Both.Count + LinqMethods.Count;
    }
}

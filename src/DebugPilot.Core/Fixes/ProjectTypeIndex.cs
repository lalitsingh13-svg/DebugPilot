using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DebugPilot.Core.Fixes
{
    public sealed class IndexLimits
    {
        public int MaxFiles { get; set; } = 5000;
        public int MaxFileBytes { get; set; } = 512 * 1024;
        public int MaxReferenceDepth { get; set; } = 2;
    }

    /// <summary>
    /// Lexical index of type declarations (type name + arity -> namespace) for a project and the projects it references.
    /// It is deliberately NOT a semantic model: nested types and #if blocks can produce false candidates, and files that
    /// declare several namespaces are skipped. Every resulting fix is previewed and verified by a rebuild.
    /// </summary>
    public sealed class ProjectTypeIndex
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
        private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline;
        private static readonly Regex NamespaceDecl = new Regex(@"^[ \t]*namespace[ \t]+(?<ns>[A-Za-z_][\w.]*)[ \t]*(?:;|\{|\r?$)", Opts, Timeout);
        private static readonly Regex TypeDecl = new Regex(
            @"^[ \t]*(?:\[[^\]\r\n]*\][ \t]*)*(?:(?:public|internal|protected|private|static|sealed|abstract|partial|readonly|unsafe|ref|file|new)[ \t]+)*(?<kind>class|struct|interface|enum|record(?:[ \t]+(?:class|struct))?)[ \t]+(?<name>[A-Za-z_]\w*)(?<gen><[^>\r\n]*>)?",
            Opts, Timeout);
        private static readonly HashSet<string> SkippedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", ".git", ".vs", "node_modules", "packages", "TestResults", ".idea" };

        private readonly Dictionary<string, HashSet<string>> _types = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        public int FilesScanned { get; private set; }
        public int FilesSkippedMultiNamespace { get; private set; }
        public bool Truncated { get; private set; }

        public static ProjectTypeIndex Build(string projectFile, IndexLimits? limits = null, CancellationToken cancellationToken = default)
        {
            limits = limits ?? new IndexLimits();
            var index = new ProjectTypeIndex();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            index.IndexProject(projectFile, 0, limits, visited, cancellationToken);
            return index;
        }

        /// <summary>Distinct, sorted namespaces declaring a type with this name and generic arity.</summary>
        public IReadOnlyList<string> FindNamespaces(string typeName, int genericArity)
        {
            return _types.TryGetValue(Key(typeName, genericArity), out var set)
                ? set.OrderBy(s => s, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();
        }

        private void IndexProject(string projectFile, int depth, IndexLimits limits, HashSet<string> visited, CancellationToken ct)
        {
            string full;
            try { full = Path.GetFullPath(projectFile); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { return; }
            if (!visited.Add(full)) return;

            var directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory)) ScanDirectory(directory!, limits, ct);

            if (depth >= limits.MaxReferenceDepth) return;
            foreach (var reference in ProjectLocator.ReadReferencedProjectFiles(full))
            {
                ct.ThrowIfCancellationRequested();
                IndexProject(reference, depth + 1, limits, visited, ct);
            }
        }

        private void ScanDirectory(string root, IndexLimits limits, CancellationToken ct)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                string current = pending.Pop();
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(current, "*.cs"); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { continue; }

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    if (FilesScanned >= limits.MaxFiles) { Truncated = true; return; }
                    FilesScanned++;
                    IndexFile(file, limits);
                }

                try
                {
                    foreach (var child in Directory.EnumerateDirectories(current))
                    {
                        string name = Path.GetFileName(child);
                        if (!SkippedDirectories.Contains(name)) pending.Push(child);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            }
        }

        private void IndexFile(string file, IndexLimits limits)
        {
            string text;
            try
            {
                var info = new FileInfo(file);
                if (info.Length > limits.MaxFileBytes || ContextEngine.IsSensitivePath(file)) return;
                text = File.ReadAllText(file, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return; }

            try
            {
                var namespaces = NamespaceDecl.Matches(text).Cast<Match>().Select(m => m.Groups["ns"].Value).Distinct(StringComparer.Ordinal).ToList();
                if (namespaces.Count == 0) return;                       // global namespace: no using needed
                if (namespaces.Count > 1) { FilesSkippedMultiNamespace++; return; }
                string ns = namespaces[0];
                foreach (Match match in TypeDecl.Matches(text))
                {
                    int arity = 0;
                    if (match.Groups["gen"].Success)
                    {
                        arity = 1;
                        foreach (char ch in match.Groups["gen"].Value) if (ch == ',') arity++;
                    }
                    string key = Key(match.Groups["name"].Value, arity);
                    if (!_types.TryGetValue(key, out var set)) _types[key] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.Add(ns);
                }
            }
            catch (RegexMatchTimeoutException) { /* skip pathological file */ }
        }

        private static string Key(string name, int arity) => name + "`" + arity;
    }
}

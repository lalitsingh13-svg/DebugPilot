using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DebugPilot.Core.Fixes
{
    /// <summary>Finds the project and solution that own a source file, without loading MSBuild.</summary>
    public static class ProjectLocator
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
        private static readonly Regex SdkStyle = new Regex(@"<Project\s[^>]*\bSdk\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
        private static readonly Regex ProjectRef = new Regex(@"<ProjectReference\s+[^>]*\bInclude\s*=\s*""(?<p>[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);

        /// <summary>Nearest ancestor directory containing a .csproj; the first one alphabetically when several exist.</summary>
        public static string? FindProjectFile(string sourceFile)
        {
            foreach (var directory in Ancestors(sourceFile))
            {
                string[] projects;
                try { projects = Directory.GetFiles(directory, "*.csproj"); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { continue; }
                if (projects.Length > 0) return projects.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).First();
            }
            return null;
        }

        /// <summary>Nearest ancestor with a .sln/.slnx; falls back to the project directory, then the file's directory.</summary>
        public static string FindSolutionRoot(string sourceFile)
        {
            foreach (var directory in Ancestors(sourceFile))
            {
                try
                {
                    if (Directory.GetFiles(directory, "*.sln").Length > 0 || Directory.GetFiles(directory, "*.slnx").Length > 0) return directory;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            }
            var project = FindProjectFile(sourceFile);
            return project != null ? Path.GetDirectoryName(project)! : Path.GetDirectoryName(Path.GetFullPath(sourceFile))!;
        }

        public static bool IsSdkStyle(string projectFile)
        {
            try
            {
                using (var reader = new StreamReader(projectFile))
                {
                    var buffer = new char[4096];
                    int read = reader.Read(buffer, 0, buffer.Length);
                    return SdkStyle.IsMatch(new string(buffer, 0, read));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is RegexMatchTimeoutException) { return false; }
        }

        /// <summary>Directories of projects referenced through &lt;ProjectReference&gt; (existing .csproj files only).</summary>
        public static IReadOnlyList<string> ReadReferencedProjectFiles(string projectFile)
        {
            var result = new List<string>();
            try
            {
                string text = File.ReadAllText(projectFile);
                string baseDir = Path.GetDirectoryName(Path.GetFullPath(projectFile))!;
                foreach (Match match in ProjectRef.Matches(text))
                {
                    string relative = match.Groups["p"].Value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    if (relative.IndexOf("$(", StringComparison.Ordinal) >= 0) continue;
                    string full = Path.GetFullPath(Path.Combine(baseDir, relative));
                    if (File.Exists(full)) result.Add(full);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is RegexMatchTimeoutException || ex is ArgumentException || ex is NotSupportedException) { }
            return result;
        }

        private static IEnumerable<string> Ancestors(string sourceFile)
        {
            string? directory;
            try { directory = Path.GetDirectoryName(Path.GetFullPath(sourceFile)); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { yield break; }
            int guard = 0;
            while (!string.IsNullOrEmpty(directory) && guard++ < 40)
            {
                yield return directory!;
                directory = Path.GetDirectoryName(directory);
            }
        }
    }
}

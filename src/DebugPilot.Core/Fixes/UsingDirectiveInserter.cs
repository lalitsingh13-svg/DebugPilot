using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DebugPilot.Core.Fixes
{
    /// <summary>
    /// Builds a single anchored patch that adds "using X;" to a C# file. The patch replaces one existing, non-empty line
    /// (the anchor) with itself plus the new directive, so the engine's expected-original-text and hash checks detect a
    /// stale file. Returns null whenever the result would be uncertain (already imported, #if in the header, malformed usings).
    /// </summary>
    public static class UsingDirectiveInserter
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
        private static readonly Regex NamespaceName = new Regex(@"^[A-Za-z_]\w*(\.[A-Za-z_]\w*)*$", RegexOptions.CultureInvariant | RegexOptions.Compiled, Timeout);
        private static readonly Regex UsingLine = new Regex(
            @"^(?<global>global\s+)?using\s+(?<static>static\s+)?(?:(?<alias>[A-Za-z_]\w*)\s*=\s*)?(?<target>[^;=]+);\s*(?://.*)?$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled, Timeout);

        private sealed class Line
        {
            public int Start;
            public string Text = string.Empty;
        }

        private sealed class UsingEntry
        {
            public Line Line = null!;
            public string Target = string.Empty;
            public bool IsPlain;
        }

        public static FixPatch? CreatePatch(string filePath, string source, string namespaceName)
        {
            if (string.IsNullOrWhiteSpace(filePath) || source == null || namespaceName == null) return null;
            try
            {
                if (!NamespaceName.IsMatch(namespaceName)) return null;
            }
            catch (RegexMatchTimeoutException) { return null; }

            var lines = SplitLines(source);
            string eol = source.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            string newDirective = "using " + namespaceName + ";";

            var entries = new List<UsingEntry>();
            Line? firstCodeLine = null;
            bool inBlockComment = false;

            foreach (var line in lines)
            {
                string t = line.Text.Trim().TrimStart('\uFEFF');
                if (inBlockComment)
                {
                    if (t.IndexOf("*/", StringComparison.Ordinal) >= 0) inBlockComment = false;
                    continue;
                }
                if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal)) continue;
                if (t.StartsWith("/*", StringComparison.Ordinal))
                {
                    int close = t.IndexOf("*/", 2, StringComparison.Ordinal);
                    if (close < 0) { inBlockComment = true; continue; }
                    if (close + 2 == t.Length) continue;
                    firstCodeLine = line;                    // code after a one-line block comment
                    break;
                }
                if (t.StartsWith("#", StringComparison.Ordinal))
                {
                    string directive = t.TrimStart('#', ' ', '\t');
                    if (directive.StartsWith("if", StringComparison.Ordinal) || directive.StartsWith("elif", StringComparison.Ordinal) ||
                        directive.StartsWith("else", StringComparison.Ordinal) || directive.StartsWith("endif", StringComparison.Ordinal))
                        return null;                          // conditional usings: too risky to edit automatically
                    continue;                                 // #nullable, #pragma, #region ...
                }
                if (t.StartsWith("extern alias", StringComparison.Ordinal)) continue;

                bool looksLikeUsing = t == "using" || t == "global using" || t.StartsWith("using ", StringComparison.Ordinal) || t.StartsWith("using\t", StringComparison.Ordinal) ||
                                      t.StartsWith("global using ", StringComparison.Ordinal);
                if (looksLikeUsing)
                {
                    Match m;
                    try { m = UsingLine.Match(t); }
                    catch (RegexMatchTimeoutException) { return null; }
                    if (!m.Success) return null;              // multi-line or unusual using: do not guess
                    string target = Regex.Replace(m.Groups["target"].Value.Trim(), @"\s+", " ");
                    bool plain = !m.Groups["global"].Success && !m.Groups["static"].Success && !m.Groups["alias"].Success;
                    bool global = m.Groups["global"].Success && !m.Groups["static"].Success && !m.Groups["alias"].Success;
                    if ((plain || global) && string.Equals(target, namespaceName, StringComparison.Ordinal)) return null; // already imported
                    entries.Add(new UsingEntry { Line = line, Target = target, IsPlain = plain });
                    continue;
                }
                firstCodeLine = line;
                break;
            }

            // 1. Insert among existing plain usings, keeping System.* first and ordinal order inside each group.
            var plainEntries = entries.FindAll(e => e.IsPlain);
            if (plainEntries.Count > 0)
            {
                foreach (var entry in plainEntries)
                {
                    if (Compare(namespaceName, entry.Target) < 0)
                        return Patch(filePath, entry.Line, newDirective + eol + entry.Line.Text);
                }
                var last = plainEntries[plainEntries.Count - 1];
                return Patch(filePath, last.Line, last.Line.Text + eol + newDirective);
            }

            // 2. Only alias/static/global usings exist: add after the last one.
            if (entries.Count > 0)
            {
                var last = entries[entries.Count - 1];
                return Patch(filePath, last.Line, last.Line.Text + eol + newDirective);
            }

            // 3. No usings at all: insert above the first code line, followed by a blank line.
            if (firstCodeLine == null) return null;
            return Patch(filePath, firstCodeLine, newDirective + eol + eol + firstCodeLine.Text);
        }

        /// <summary>Visual Studio default ordering: "System" namespaces first, then everything else, each ordinal.</summary>
        private static int Compare(string a, string b)
        {
            int ga = IsSystem(a) ? 0 : 1;
            int gb = IsSystem(b) ? 0 : 1;
            if (ga != gb) return ga.CompareTo(gb);
            return string.CompareOrdinal(a, b);
        }

        private static bool IsSystem(string ns) =>
            ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal);

        private static FixPatch? Patch(string filePath, Line anchor, string replacement)
        {
            if (anchor.Text.Trim().Length == 0) return null;
            return new FixPatch(filePath, anchor.Start, anchor.Text.Length, anchor.Text, PatchEngine.Sha256(anchor.Text), replacement);
        }

        private static List<Line> SplitLines(string source)
        {
            var result = new List<Line>();
            int start = 0;
            for (int i = 0; i <= source.Length; i++)
            {
                if (i == source.Length || source[i] == '\n')
                {
                    int end = i;
                    if (end > start && source[end - 1] == '\r') end--;
                    result.Add(new Line { Start = start, Text = source.Substring(start, end - start) });
                    start = i + 1;
                }
            }
            return result;
        }
    }
}

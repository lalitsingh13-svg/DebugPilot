using System;
using System.Collections.Generic;
using System.Text;

namespace DebugPilot.Core.Fixes
{
    /// <summary>Readable unified-style diff for one contiguous change (a PatchEngine patch replaces exactly one range).</summary>
    public static class UnifiedDiff
    {
        private const int MaxChangedLines = 400;

        public static string Create(string? before, string? after, int contextLines = 3, string fromName = "original", string toName = "proposed")
        {
            var a = SplitLines(before ?? string.Empty);
            var b = SplitLines(after ?? string.Empty);

            int prefix = 0;
            while (prefix < a.Count && prefix < b.Count && a[prefix] == b[prefix]) prefix++;
            int suffix = 0;
            while (suffix < a.Count - prefix && suffix < b.Count - prefix && a[a.Count - 1 - suffix] == b[b.Count - 1 - suffix]) suffix++;

            if (prefix == a.Count && prefix == b.Count) return "(no changes)";

            int removed = a.Count - prefix - suffix;
            int added = b.Count - prefix - suffix;
            int ctx = Math.Max(0, contextLines);
            int startContext = Math.Max(0, prefix - ctx);
            int endContext = Math.Min(suffix, ctx);

            var sb = new StringBuilder();
            sb.Append("--- ").Append(fromName).Append('\n').Append("+++ ").Append(toName).Append('\n');
            sb.Append("@@ -").Append(startContext + 1).Append(',').Append(prefix - startContext + removed + endContext)
              .Append(" +").Append(startContext + 1).Append(',').Append(prefix - startContext + added + endContext).Append(" @@\n");

            for (int i = startContext; i < prefix; i++) sb.Append(' ').Append(a[i]).Append('\n');
            AppendLimited(sb, '-', a, prefix, removed);
            AppendLimited(sb, '+', b, prefix, added);
            for (int i = 0; i < endContext; i++) sb.Append(' ').Append(a[a.Count - suffix + i]).Append('\n');
            return sb.ToString().TrimEnd('\n');
        }

        private static void AppendLimited(StringBuilder sb, char sign, List<string> lines, int start, int count)
        {
            int shown = Math.Min(count, MaxChangedLines);
            for (int i = 0; i < shown; i++) sb.Append(sign).Append(lines[start + i]).Append('\n');
            if (count > shown) sb.Append(sign).Append("… ").Append(count - shown).Append(" more line(s) not shown\n");
        }

        private static List<string> SplitLines(string text)
        {
            var result = new List<string>();
            if (text.Length == 0) return result;
            foreach (var raw in text.Split('\n')) result.Add(raw.TrimEnd('\r'));
            if (result.Count > 0 && result[result.Count - 1].Length == 0) result.RemoveAt(result.Count - 1);
            return result;
        }
    }
}

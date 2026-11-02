using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace DebugPilot.Core.Verification
{
    /// <summary>Parses MSBuild / dotnet build console output into structured diagnostics (English output only).</summary>
    public static class BuildOutputParser
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
        private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

        // C:\src\A.cs(12,5): error CS0246: message [C:\src\A.csproj]
        private static readonly Regex WithFile = new Regex(
            @"^\s*(?<file>.+?)\((?<line>\d+)(?:,(?<col>\d+))?(?:,\d+,\d+)?\)\s*:\s*(?<sev>error|warning)\s+(?<code>[A-Za-z]{1,8}\d{1,6})\s*:\s*(?<msg>.*?)(?:\s+\[(?<proj>[^\]]+)\])?\s*$",
            Opts, Timeout);

        // MSBUILD : error MSB1009: ...   |   C:\x.csproj : error NU1101: ...
        private static readonly Regex WithoutFile = new Regex(
            @"^\s*(?:(?<tool>[^\r\n]*?)\s*:\s*)?(?<sev>error|warning)\s+(?<code>[A-Za-z]{1,8}\d{1,6})\s*:\s*(?<msg>.*?)(?:\s+\[(?<proj>[^\]]+)\])?\s*$",
            Opts, Timeout);

        public static IReadOnlyList<BuildDiagnostic> Parse(string? output, int maxDiagnostics = 500)
        {
            var result = new List<BuildDiagnostic>();
            if (string.IsNullOrEmpty(output)) return result;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            using (var reader = new StringReader(output!))
            {
                string? line;
                while ((line = reader.ReadLine()) != null && result.Count < maxDiagnostics)
                {
                    if (line.Length == 0 || line.Length > 4000) continue;
                    BuildDiagnostic? diagnostic = TryParseLine(line);
                    if (diagnostic == null) continue;
                    // dotnet prints every diagnostic twice (inline and in the summary).
                    string key = string.Join("|", diagnostic.Severity, diagnostic.Code, diagnostic.FilePath, diagnostic.Line, diagnostic.Column, diagnostic.Message);
                    if (seen.Add(key)) result.Add(diagnostic);
                }
            }
            return result;
        }

        private static BuildDiagnostic? TryParseLine(string line)
        {
            try
            {
                var m = WithFile.Match(line);
                if (m.Success)
                {
                    int? lineNo = int.TryParse(m.Groups["line"].Value, out var l) ? l : (int?)null;
                    int? col = m.Groups["col"].Success && int.TryParse(m.Groups["col"].Value, out var c) ? c : (int?)null;
                    return new BuildDiagnostic(m.Groups["sev"].Value.ToLowerInvariant(), m.Groups["code"].Value.ToUpperInvariant(),
                        m.Groups["msg"].Value.Trim(), m.Groups["file"].Value.Trim(), lineNo, col,
                        m.Groups["proj"].Success ? m.Groups["proj"].Value : null);
                }
                m = WithoutFile.Match(line);
                if (m.Success)
                {
                    string tool = m.Groups["tool"].Success ? m.Groups["tool"].Value.Trim() : string.Empty;
                    string? file = tool.IndexOfAny(new[] { '\\', '/' }) >= 0 ? tool : null;
                    return new BuildDiagnostic(m.Groups["sev"].Value.ToLowerInvariant(), m.Groups["code"].Value.ToUpperInvariant(),
                        m.Groups["msg"].Value.Trim(), file, null, null, m.Groups["proj"].Success ? m.Groups["proj"].Value : null);
                }
            }
            catch (RegexMatchTimeoutException) { }
            return null;
        }
    }
}

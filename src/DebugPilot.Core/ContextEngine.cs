using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DebugPilot.Core
{
    /// <summary>Controls bounded context collection. Input text must be supplied by the caller; this class never scans the filesystem.</summary>
    public sealed class ContextCaptureOptions
    {
        public int MaxUtf8Bytes { get; set; } = 12000;
        public int SourceLinesBefore { get; set; } = 30;
        public int SourceLinesAfter { get; set; } = 30;
        public bool IncludeDebuggerLocals { get; set; }

        internal void Validate()
        {
            if (MaxUtf8Bytes < 256 || MaxUtf8Bytes > 1048576) throw new ArgumentOutOfRangeException(nameof(MaxUtf8Bytes), "Context budget must be between 256 bytes and 1 MiB.");
            if (SourceLinesBefore < 0 || SourceLinesAfter < 0 || SourceLinesBefore > 1000 || SourceLinesAfter > 1000) throw new ArgumentOutOfRangeException("Source line windows must be between 0 and 1000 lines.");
        }
    }

    /// <summary>Minimal context already collected by a host adapter for the current diagnostic only.</summary>
    public sealed class ContextCaptureInput
    {
        public ErrorContext Diagnostic { get; set; }
        public string? SourceText { get; set; }
        public int SourceStartLine { get; set; } = 1;
        public string? EnclosingSignature { get; set; }
        public string? RelevantProjectSettings { get; set; }
        public string? RelevantPackageMetadata { get; set; }
        public string? TrimmedBuildOutput { get; set; }
        public string? StackTrace { get; set; }
        public string? TestName { get; set; }
        public string? TestFailure { get; set; }
        public string? DebuggerLocals { get; set; }

        public ContextCaptureInput(ErrorContext diagnostic)
        {
            Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        }
    }

    /// <summary>Redacted, budget-limited preview. Text is the exact payload representation intended for a provider.</summary>
    public sealed class ContextPreview
    {
        public string Text { get; internal set; } = string.Empty;
        public int Utf8ByteCount { get; internal set; }
        public bool WasTruncated { get; internal set; }
        public bool SensitiveSourceExcluded { get; internal set; }
    }

    /// <summary>Privacy-first context builder. It accepts supplied snippets and never opens or scans files itself.</summary>
    public sealed class ContextEngine
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
        private static readonly Regex SensitiveFileName = new Regex(
            @"(?i)(^|[\\/])(appsettings(?:\.[^\\/]*)?\.json|secrets\.json|\.env(?:\.[^\\/]*)?|[^\\/]+\.(?:pfx|p12|pem|key|snk|jks|keystore|kdbx|ppk)|id_(?:rsa|dsa|ecdsa|ed25519)|\.netrc|\.npmrc|\.pypirc)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
        private static readonly Regex SensitiveDirectory = new Regex(@"(?i)[\\/](?:usersecrets|\.aws|\.ssh)[\\/]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
        private static readonly Regex SecretLikeName = new Regex(
            @"(?i)(password|passwd|pwd|secret|token|api[_-]?key|credential|connection[_-]?string|private[_-]?key|access[_-]?key|authorization|cookie|session)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
        private static readonly Regex LocalLine = new Regex(@"^\s*(?<name>[^=:\s]+)\s*[=:]\s*(?<value>.*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

        private const int MaxDebuggerLocals = 20;
        private const int MaxDebuggerLocalChars = 200;

        public ContextPreview Build(ContextCaptureInput input, ContextCaptureOptions? options = null)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            options = options ?? new ContextCaptureOptions();
            options.Validate();

            bool excludeSource = IsSensitivePath(input.Diagnostic.FilePath);
            ErrorContext error = input.Diagnostic;
            var sections = new List<RawSection>();

            Add(sections, "Diagnostic", "Source: " + error.Source + "\nCode: " + error.Code + "\nSeverity: " + error.Severity + "\nMessage: " + error.Message);
            if (!string.IsNullOrWhiteSpace(error.FilePath)) Add(sections, "Location", "File: " + SafePath(error.FilePath!) + "\nLine: " + (error.Line.HasValue ? error.Line.Value.ToString() : "unknown") + "\nColumn: " + (error.Column.HasValue ? error.Column.Value.ToString() : "unknown"));
            if (!string.IsNullOrWhiteSpace(error.ProjectPath)) Add(sections, "Project", SafePath(error.ProjectPath!));

            if (!excludeSource && !string.IsNullOrWhiteSpace(input.EnclosingSignature)) Add(sections, "Enclosing member/type", input.EnclosingSignature!);
            if (!excludeSource && !string.IsNullOrWhiteSpace(input.SourceText))
                Add(sections, "Relevant source excerpt", SelectSourceLines(input.SourceText!, input.SourceStartLine, error.Line ?? input.SourceStartLine, options.SourceLinesBefore, options.SourceLinesAfter, options.MaxUtf8Bytes));
            if (!string.IsNullOrWhiteSpace(input.RelevantProjectSettings)) Add(sections, "Relevant project settings", input.RelevantProjectSettings!);
            if (!string.IsNullOrWhiteSpace(input.RelevantPackageMetadata)) Add(sections, "Relevant package metadata", input.RelevantPackageMetadata!);
            // Errors are usually at the END of build output, stack traces and test failures: keep both ends, drop the middle.
            if (!string.IsNullOrWhiteSpace(input.TrimmedBuildOutput)) Add(sections, "Relevant build output", input.TrimmedBuildOutput!, keepBothEnds: true);
            string? stack = input.StackTrace ?? error.StackTrace;
            if (!string.IsNullOrWhiteSpace(stack)) Add(sections, "Stack trace", stack!, keepBothEnds: true);
            if (!string.IsNullOrWhiteSpace(input.TestName)) Add(sections, "Test name", input.TestName!);
            if (!string.IsNullOrWhiteSpace(input.TestFailure)) Add(sections, "Test failure", input.TestFailure!, keepBothEnds: true);
            if (options.IncludeDebuggerLocals && !string.IsNullOrWhiteSpace(input.DebuggerLocals))
                Add(sections, "Debugger locals (user enabled)", MaskDebuggerLocals(input.DebuggerLocals!));

            // Each section is redacted and then fitted to the budget that is LEFT at that point, so later sections are
            // shortened by their own middle/tail rules instead of being cut off blindly at the very end.
            var output = new StringBuilder();
            int used = 0;
            bool truncated = false;
            foreach (RawSection section in sections)
            {
                string separator = output.Length == 0 ? string.Empty : "\n\n";
                string heading = separator + section.Heading + ":\n";
                int budget = options.MaxUtf8Bytes - used - Encoding.UTF8.GetByteCount(heading);
                if (budget <= 0) { truncated = true; break; }
                string content = FitSection(section.Content.Trim(), budget, section.KeepBothEnds, ref truncated);
                if (string.IsNullOrWhiteSpace(content)) continue;
                output.Append(heading).Append(content);
                used = Encoding.UTF8.GetByteCount(output.ToString());
            }

            string text = output.ToString();
            return new ContextPreview
            {
                Text = text,
                Utf8ByteCount = Encoding.UTF8.GetByteCount(text),
                WasTruncated = truncated,
                SensitiveSourceExcluded = excludeSource
            };
        }

        private sealed class RawSection
        {
            public RawSection(string heading, string content, bool keepBothEnds) { Heading = heading; Content = content; KeepBothEnds = keepBothEnds; }
            public string Heading { get; }
            public string Content { get; }
            public bool KeepBothEnds { get; }
        }

        private static void Add(List<RawSection> sections, string heading, string content, bool keepBothEnds = false)
        {
            if (!string.IsNullOrWhiteSpace(content)) sections.Add(new RawSection(heading, content, keepBothEnds));
        }

        private static string SafePath(string path) => IsSensitivePath(path) ? "<SENSITIVE_FILE_EXCLUDED>" : path;

        /// <summary>Masks secrets. Fails closed: if redaction cannot complete safely the text is withheld.</summary>
        public static string Redact(string? value) => SecretRedactor.Redact(value);

        /// <summary>
        /// Redacts BEFORE truncating, so a secret cut in half by the byte budget can never leak a prefix.
        /// Input is first bounded (with a safety margin) so huge strings are never fully scanned.
        /// </summary>
        private static string FitSection(string value, int budgetBytes, bool keepBothEnds, ref bool truncated)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            long limit = (long)budgetBytes * 4 + 256;
            if (value.Length > limit)
            {
                value = keepBothEnds
                    ? value.Substring(0, (int)(limit / 4)) + "\n" + value.Substring(value.Length - (int)(limit * 3 / 4) + 64)
                    : value.Substring(0, (int)limit - 64);
                truncated = true;
            }
            string redacted = SecretRedactor.Redact(value);
            if (Encoding.UTF8.GetByteCount(redacted) <= budgetBytes) return redacted;
            truncated = true;
            return keepBothEnds ? TruncateMiddleUtf8(redacted, budgetBytes) : TruncateUtf8(redacted, budgetBytes);
        }

        public static bool IsSensitivePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try { return SensitiveFileName.IsMatch(path!) || SensitiveDirectory.IsMatch(path!); }
            catch (RegexMatchTimeoutException) { return true; } // fail closed
        }

        /// <summary>Limits debugger locals and masks the value of any variable whose NAME looks secret (e.g. userPassword).</summary>
        private static string MaskDebuggerLocals(string locals)
        {
            var lines = new List<string>();
            foreach (var raw in locals.Split('\n'))
            {
                if (lines.Count >= MaxDebuggerLocals) { lines.Add("... additional locals omitted ..."); break; }
                var line = raw.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Length > MaxDebuggerLocalChars) line = line.Substring(0, MaxDebuggerLocalChars) + "…";
                try
                {
                    var m = LocalLine.Match(line);
                    if (m.Success && SecretLikeName.IsMatch(m.Groups["name"].Value))
                        line = m.Groups["name"].Value + " = <REDACTED>";
                }
                catch (RegexMatchTimeoutException) { line = "<LOCAL WITHHELD>"; }
                lines.Add(line);
            }
            return string.Join("\n", lines);
        }

        private static string SelectSourceLines(string source, int sourceStartLine, int errorLine, int before, int after, int maxBytes)
        {
            int firstLine = Math.Max(sourceStartLine, errorLine - before);
            int lastLine = errorLine + after;
            var selected = new StringBuilder();
            int lineNumber = sourceStartLine;
            int start = 0;
            while (start <= source.Length && lineNumber <= lastLine)
            {
                int end = source.IndexOf('\n', start);
                if (end < 0) end = source.Length;
                int contentEnd = end > start && source[end - 1] == '\r' ? end - 1 : end;
                if (lineNumber >= firstLine)
                {
                    if (selected.Length > 0) selected.Append('\n');
                    selected.Append(lineNumber.ToString()).Append(": ");
                    int remaining = maxBytes - Encoding.UTF8.GetByteCount(selected.ToString());
                    if (remaining > 0) selected.Append(source, start, Math.Min(contentEnd - start, remaining));
                }
                if (end == source.Length) break;
                start = end + 1;
                lineNumber++;
            }
            return selected.ToString();
        }

        private static string TruncateUtf8(string value, int maxBytes)
        {
            if (maxBytes <= 0 || value.Length == 0) return string.Empty;
            if (Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;
            int low = 0;
            int high = value.Length;
            while (low < high)
            {
                int middle = low + (high - low + 1) / 2;
                if (Encoding.UTF8.GetByteCount(value.Substring(0, middle)) <= maxBytes) low = middle;
                else high = middle - 1;
            }
            if (low > 0 && char.IsHighSurrogate(value[low - 1])) low--;
            return value.Substring(0, low);
        }

        private static string TruncateUtf8Tail(string value, int maxBytes)
        {
            if (maxBytes <= 0 || value.Length == 0) return string.Empty;
            if (Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;
            int low = 0;
            int high = value.Length;
            while (low < high)
            {
                int length = low + (high - low + 1) / 2;
                if (Encoding.UTF8.GetByteCount(value.Substring(value.Length - length)) <= maxBytes) low = length;
                else high = length - 1;
            }
            int start = value.Length - low;
            if (start < value.Length && char.IsLowSurrogate(value[start])) start++;
            return value.Substring(start);
        }

        private static string TruncateMiddleUtf8(string value, int maxBytes)
        {
            if (Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;
            const string marker = "\n… [middle omitted] …\n";
            int budget = maxBytes - Encoding.UTF8.GetByteCount(marker);
            if (budget < 64) return TruncateUtf8(value, maxBytes);
            string head = TruncateUtf8(value, budget * 2 / 5);
            string tail = TruncateUtf8Tail(value, budget - Encoding.UTF8.GetByteCount(head));
            return head + marker + tail;
        }
    }
}

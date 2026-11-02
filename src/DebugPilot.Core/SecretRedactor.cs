using System;
using System.Text.RegularExpressions;

namespace DebugPilot.Core
{
    /// <summary>
    /// Masks secrets and personal data before text is previewed or sent to an AI provider.
    /// Rules are ordered (specific token formats first, generic assignments later), every regex has a
    /// timeout, and a timeout FAILS CLOSED: the whole text is withheld rather than returned partly redacted.
    /// Redacting twice yields the same text.
    /// </summary>
    public static class SecretRedactor
    {
        public const string WithheldText = "[CONTENT WITHHELD: redaction did not complete safely]";

        private const RegexOptions O = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        private const RegexOptions OI = O | RegexOptions.IgnoreCase;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

        private const string SecretNames =
            "password|passwd|pwd|token|access_token|refresh_token|client_secret|api[_-]?key|apikey|secret|authorization|private[_-]?key|account[_-]?key";

        private sealed class Rule
        {
            public Rule(Regex pattern, string replacement) { Pattern = pattern; Replacement = replacement; }
            public Regex Pattern { get; }
            public string Replacement { get; }
        }

        private static Rule R(string pattern, string replacement, bool ignoreCase = false) =>
            new Rule(new Regex(pattern, ignoreCase ? OI : O, Timeout), replacement);

        private static readonly Rule[] Rules =
        {
            // Whole-block secrets. "|$" also catches truncated blocks (fail closed).
            R(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?(?:-----END [A-Z ]*PRIVATE KEY-----|$)", "<REDACTED:PRIVATE_KEY>"),
            // scheme://user:password@host
            R(@"\b([a-z][a-z0-9+.\-]*://)[^/\s:@]+:[^/\s@]+@", "$1<REDACTED>@", true),
            // Well-known token formats.
            R(@"\beyJ[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]{5,}", "<REDACTED:JWT>"),
            R(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", "<REDACTED:AWS_KEY>"),
            R(@"\bgh[pousr]_[A-Za-z0-9]{36,}|\bgithub_pat_[A-Za-z0-9_]{22,}", "<REDACTED:GITHUB_TOKEN>"),
            R(@"\bsk-(?:ant-)?[A-Za-z0-9_\-]{20,}", "<REDACTED:API_KEY>"),
            R(@"\bxox[baprs]-[A-Za-z0-9\-]{10,}", "<REDACTED:SLACK_TOKEN>"),
            R(@"\bAIza[0-9A-Za-z_\-]{35,}", "<REDACTED:API_KEY>"),
            // Authorization schemes. Must run BEFORE the generic assignment rules, which would otherwise
            // consume only the word "Bearer" in "Authorization: Bearer <token>" and leave the token behind.
            R(@"\bBearer\s+[A-Za-z0-9._~+/\-]+=*", "Bearer <REDACTED>", true),
            R(@"\bBasic\s+[A-Za-z0-9+/]{16,}=*", "Basic <REDACTED>", true),
            // Connection-string secrets (value may contain spaces up to the next ';').
            R(@"\b(Data Source|Server|Initial Catalog|Database|User ID|UID|User|Password|Pwd|AccountKey|SharedAccessKey)\s*=\s*([^;\r\n]+)", "$1=<REDACTED>", true),
            // name = "quoted value" (JSON, C# literals incl. verbatim/interpolated). Handles values containing spaces.
            R(@"\b(" + SecretNames + @")\b([""']?\s*[:=]\s*)[@$]{0,2}(?:""[^""\r\n]*""|'[^'\r\n]*')", "$1$2\"<REDACTED>\"", true),
            // name = unquoted value (env files, logs).
            R(@"(['""]?\b(?:" + SecretNames + @")\b['""]?\s*[:=]\s*['""]?)[^\s,;}""']+", "$1<REDACTED>", true),
            // Personal data.
            R(@"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b", "<EMAIL>", true),
            R(@"[A-Z]:\\Users\\[^\\\s]+", "<USER_PATH>", true),
            R(@"/Users/[^/\s]+|/home/[^/\s]+", "<USER_PATH>"),
        };

        /// <summary>Returns redacted text, or <see cref="WithheldText"/> if redaction could not complete safely.</summary>
        public static string Redact(string? value)
        {
            TryRedact(value, out var redacted);
            return redacted;
        }

        /// <summary>False when a regex timed out; <paramref name="redacted"/> is then the withheld placeholder.</summary>
        public static bool TryRedact(string? value, out string redacted)
        {
            if (string.IsNullOrEmpty(value)) { redacted = value ?? string.Empty; return true; }
            var text = value!;
            try
            {
                foreach (var rule in Rules) text = rule.Pattern.Replace(text, rule.Replacement);
            }
            catch (RegexMatchTimeoutException)
            {
                redacted = WithheldText;
                return false;
            }
            redacted = text;
            return true;
        }
    }
}

using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DebugPilot.Core
{
    /// <summary>Stable identity for grouping recurring errors in history.</summary>
    public static class ErrorFingerprint
    {
        private static readonly Regex Guid = new Regex(@"\b[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\b", RegexOptions.Compiled);
        private static readonly Regex WinPath = new Regex(@"[A-Za-z]:\\[^\s'""<>|]+", RegexOptions.Compiled);
        private static readonly Regex Number = new Regex(@"\d+", RegexOptions.Compiled);
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);

        public static string Normalize(string message)
        {
            if (message is null) throw new ArgumentNullException(nameof(message));
            var s = Guid.Replace(message, "<guid>");
            s = WinPath.Replace(s, "<path>");
            s = Number.Replace(s, "<n>");
            return Spaces.Replace(s, " ").Trim().ToLowerInvariant();
        }

        public static string Compute(ErrorContext error)
        {
            if (error is null) throw new ArgumentNullException(nameof(error));
            var key = string.Join("|", error.Code.ToUpperInvariant(), error.ProjectPath ?? "", Normalize(error.Message));
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
            var sb = new StringBuilder(16);
            for (var i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }
}

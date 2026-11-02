using System;
using System.Text.RegularExpressions;

namespace DebugPilot.Core.Fixes
{
    public enum MissingSymbolKind { TypeOrNamespace, Name, ExtensionMethod }

    /// <summary>The symbol a compiler diagnostic says is missing. GenericArity is 0 for non-generic names.</summary>
    public sealed record MissingSymbol(string Name, int GenericArity, MissingSymbolKind Kind);

    /// <summary>
    /// Extracts the missing symbol from English compiler messages for CS0246, CS0103 and CS1061 (extension methods).
    /// Localized compiler messages are not parsed; the rule then simply produces no automatic fix.
    /// </summary>
    public static class MissingSymbolParser
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
        private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

        private static readonly Regex TypeOrNamespace = new Regex(@"type or namespace name '(?<n>[^']+)' could not be found", Opts, Timeout);
        private static readonly Regex NameMissing = new Regex(@"The name '(?<n>[^']+)' does not exist in the current context", Opts, Timeout);
        private static readonly Regex ExtensionMissing = new Regex(
            @"does not contain a definition for '(?<n>[A-Za-z_]\w*)' and no (?:accessible )?extension method '\k<n>'", Opts, Timeout);

        public static MissingSymbol? Parse(string? code, string? message)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(message)) return null;
            string c = code!.Trim().ToUpperInvariant();
            try
            {
                if (c == "CS0246") return FromName(TypeOrNamespace.Match(message!), MissingSymbolKind.TypeOrNamespace);
                if (c == "CS0103") return FromName(NameMissing.Match(message!), MissingSymbolKind.Name);
                if (c == "CS1061")
                {
                    var m = ExtensionMissing.Match(message!);
                    return m.Success ? new MissingSymbol(m.Groups["n"].Value, 0, MissingSymbolKind.ExtensionMethod) : null;
                }
            }
            catch (RegexMatchTimeoutException) { }
            return null;
        }

        private static MissingSymbol? FromName(Match match, MissingSymbolKind kind)
        {
            if (!match.Success) return null;
            string raw = match.Groups["n"].Value.Trim();
            int arity = 0;
            int lt = raw.IndexOf('<');
            if (lt >= 0)
            {
                int gt = raw.LastIndexOf('>');
                if (gt < lt) return null;
                string inner = raw.Substring(lt + 1, gt - lt - 1);
                arity = 1;
                foreach (char ch in inner) if (ch == ',') arity++;
                raw = raw.Substring(0, lt);
            }
            // Qualified names ('Foo.Bar') mean a missing namespace segment; a using directive cannot fix that reliably.
            if (raw.Length == 0 || raw.IndexOf('.') >= 0) return null;
            for (int i = 0; i < raw.Length; i++)
                if (!(char.IsLetterOrDigit(raw[i]) || raw[i] == '_')) return null;
            if (char.IsDigit(raw[0])) return null;
            return new MissingSymbol(raw, arity, kind);
        }
    }
}

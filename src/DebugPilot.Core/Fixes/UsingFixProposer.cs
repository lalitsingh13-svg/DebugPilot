using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DebugPilot.Core.Fixes
{
    /// <summary>
    /// Proposes a previewable "add using" patch for CS0246 / CS0103 / CS1061 (extension method). A patch is produced only
    /// when exactly one candidate namespace remains; with several candidates the user is shown the choices instead.
    /// Nothing is written here: the patch goes through PatchEngine preview, approval, checkpoint and rebuild verification.
    /// </summary>
    public sealed class UsingFixProposer
    {
        public const string RuleId = "DP-ADD-USING-Fix";
        private const int MaxSourceBytes = 2 * 1024 * 1024;

        private readonly IndexLimits _limits;

        public UsingFixProposer(IndexLimits? limits = null)
        {
            _limits = limits ?? new IndexLimits();
        }

        public bool CanHandle(ErrorContext error)
        {
            if (error == null) return false;
            if (error.Source != ErrorSource.Compiler && error.Source != ErrorSource.Build) return false;
            if (string.IsNullOrWhiteSpace(error.FilePath)) return false;
            string code = (error.Code ?? string.Empty).Trim().ToUpperInvariant();
            return code == "CS0246" || code == "CS0103" || code == "CS1061";
        }

        public async Task<Diagnosis?> ProposeAsync(ErrorContext error, CancellationToken cancellationToken = default)
        {
            if (!CanHandle(error)) return null;
            cancellationToken.ThrowIfCancellationRequested();

            var symbol = MissingSymbolParser.Parse(error.Code, error.Message);
            if (symbol == null) return null;

            // File IO and indexing run off the caller's (possibly UI) thread.
            return await Task.Run(() => Propose(error, symbol, cancellationToken), cancellationToken).ConfigureAwait(false);
        }

        private Diagnosis? Propose(ErrorContext error, MissingSymbol symbol, CancellationToken ct)
        {
            string path;
            try { path = Path.GetFullPath(error.FilePath!.Trim()); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { return null; }

            if (!string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase)) return null;
            if (ContextEngine.IsSensitivePath(path) || !File.Exists(path)) return null;

            string source;
            try
            {
                if (new FileInfo(path).Length > MaxSourceBytes) return null;
                source = File.ReadAllText(path, new UTF8Encoding(false, true));   // strict: same decoding rules as PatchEngine
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is DecoderFallbackException) { return null; }

            ct.ThrowIfCancellationRequested();

            // Candidate namespaces and where each came from.
            var origin = new Dictionary<string, string>(StringComparer.Ordinal);
            if (symbol.Kind == MissingSymbolKind.ExtensionMethod)
            {
                foreach (var ns in KnownNamespaces.FindExtensionMethod(symbol.Name)) origin[ns] = "LINQ extension method catalog";
            }
            else
            {
                foreach (var ns in KnownNamespaces.FindType(symbol.Name, symbol.GenericArity)) origin[ns] = ".NET base class library catalog";
                var project = ProjectLocator.FindProjectFile(path);
                if (project != null)
                {
                    var index = ProjectTypeIndex.Build(project, _limits, ct);
                    foreach (var ns in index.FindNamespaces(symbol.Name, symbol.GenericArity))
                        origin[ns] = origin.ContainsKey(ns) ? origin[ns] + " + project source" : "declared in this project or a referenced project";
                }
            }
            if (origin.Count == 0) return null;

            // Drop namespaces that are already imported (or cannot be edited safely).
            var patches = new List<(string Namespace, FixPatch Patch)>();
            foreach (var ns in origin.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var patch = UsingDirectiveInserter.CreatePatch(path, source, ns);
                if (patch != null) patches.Add((ns, patch));
            }
            if (patches.Count == 0) return null;

            string display = symbol.Name + (symbol.GenericArity > 0 ? "<" + new string(',', symbol.GenericArity - 1) + ">" : string.Empty);
            var evidence = new List<string>
            {
                "Compiler diagnostic " + error.Code.Trim().ToUpperInvariant() + " reports an unresolved " +
                    (symbol.Kind == MissingSymbolKind.ExtensionMethod ? "extension method '" : "name '") + display + "'.",
                "File: " + path
            };
            foreach (var item in patches) evidence.Add("Candidate namespace " + item.Namespace + " (" + origin[item.Namespace] + ")");

            if (patches.Count == 1)
            {
                var only = patches[0];
                var suggestion = new FixSuggestion(FixSuggestionKind.AddUsingDirective,
                    "Add 'using " + only.Namespace + ";'",
                    "'" + display + "' is provided by namespace " + only.Namespace + ", which this file does not import.",
                    new[] { "Review the diff.", "Approve to apply; a rollback checkpoint is created first.", "DebugPilot rebuilds the project to verify the error is gone." });
                return new Diagnosis(RuleId,
                    "'" + display + "' is defined in namespace " + only.Namespace + ", but this file has no using directive for it.",
                    evidence, new[] { only.Patch }, origin[only.Namespace].StartsWith(".NET", StringComparison.Ordinal) ? 0.9 : 0.85, false, new[] { suggestion });
            }

            var choices = patches.Select(p => new FixSuggestion(FixSuggestionKind.AddUsingDirective,
                "Add 'using " + p.Namespace + ";'", "Candidate from: " + origin[p.Namespace] + ".",
                new[] { "Confirm this is the type you meant, then add the directive yourself (no patch is offered while the choice is ambiguous)." })).ToList();
            return new Diagnosis(RuleId,
                "'" + display + "' exists in " + patches.Count + " namespaces (" + string.Join(", ", patches.Select(p => p.Namespace)) + "). DebugPilot does not guess between them.",
                evidence, Array.Empty<FixPatch>(), 0.6, false, choices);
        }
    }
}

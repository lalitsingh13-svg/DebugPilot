using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DebugPilot.Core.DiagnosisRules
{
    public sealed class Cs0246MissingTypeRule : IDeterministicRule
    {
        private static readonly Regex MissingName = new Regex(
            @"(?:type|namespace)\s+name\s+['""](?<name>[^'""]+)['""]\s+could not be found",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public string RuleId => "DP-CS0246-MissingType";

        public bool CanHandle(ErrorContext error) => error != null &&
            string.Equals(error.Code?.Trim(), "CS0246", StringComparison.OrdinalIgnoreCase);

        public Task<Diagnosis?> AnalyzeAsync(ErrorContext error, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = MissingName.Match(error.Message ?? string.Empty);
            var missingName = match.Success ? match.Groups["name"].Value.Trim() : string.Empty;
            var evidence = new List<string> { "Compiler diagnostic CS0246 reports an unresolved type or namespace." };
            if (!string.IsNullOrWhiteSpace(error.FilePath)) evidence.Add("Diagnostic file: " + error.FilePath);
            if (error.Line.HasValue) evidence.Add("Diagnostic line: " + error.Line.Value);
            if (missingName.Length > 0) evidence.Add("Unresolved identifier from diagnostic message: " + missingName);

            var suggestions = new List<FixSuggestion>
            {
                new FixSuggestion(FixSuggestionKind.VerifyTypeNameOrNamespace,
                    "Verify the type or namespace name",
                    "CS0246 can be caused by a spelling mismatch or a type that is not available to this compilation.",
                    new[] { "Check spelling and capitalization of the unresolved name.", "Confirm the type exists in the referenced target framework and selected configuration." }),
                new FixSuggestion(FixSuggestionKind.AddUsingDirective,
                    "Check whether a using directive is required",
                    "If the type exists in a referenced assembly, its namespace may not be imported.",
                    new[] { "Find the declaration of the type in the solution or referenced package documentation.", "Add the correct namespace import only after confirming the namespace." }),
                new FixSuggestion(FixSuggestionKind.AddProjectReference,
                    "Check project or assembly references",
                    "The project may not reference the project or assembly that defines the type.",
                    new[] { "Locate the project or assembly that defines the type.", "Add a reference only if it is a legitimate dependency." }),
                new FixSuggestion(FixSuggestionKind.AddPackageReference,
                    "Check whether a NuGet package provides the type",
                    "If the type belongs to an external library, the project may need a package reference; the package must be identified from the type's documented assembly or namespace.",
                    new[] { "Identify the assembly that defines the type before selecting a package.", "Verify package identity, target-framework compatibility, and version constraints before adding a reference." })
            };

            var diagnosis = new Diagnosis(RuleId,
                missingName.Length > 0
                    ? "The compiler cannot resolve '" + missingName + "'. The message alone cannot distinguish a typo, missing import, or missing reference."
                    : "The compiler cannot resolve a type or namespace. The diagnostic message did not expose a parseable identifier.",
                evidence, Array.Empty<FixPatch>(), missingName.Length > 0 ? 0.82 : 0.68, false, suggestions);
            return Task.FromResult<Diagnosis?>(diagnosis);
        }
    }
}

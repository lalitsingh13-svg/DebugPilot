using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DebugPilot.Core.DiagnosisRules
{
    public sealed class Nu1605PackageDowngradeRule : IDeterministicRule
    {
        private static readonly Regex PackageName = new Regex(
            @"Detected package downgrade:\s*(?<package>[A-Za-z0-9_.-]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public string RuleId => "DP-NU1605-PackageDowngrade";

        public bool CanHandle(ErrorContext error) => error != null &&
            string.Equals(error.Code?.Trim(), "NU1605", StringComparison.OrdinalIgnoreCase);

        public Task<Diagnosis?> AnalyzeAsync(ErrorContext error, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = error.Message ?? string.Empty;
            var match = PackageName.Match(message);
            var package = match.Success ? match.Groups["package"].Value : string.Empty;
            var evidence = new List<string> { "NU1605 indicates NuGet resolved a package version lower than a dependency requires." };
            if (!string.IsNullOrWhiteSpace(error.ProjectPath)) evidence.Add("Affected project: " + error.ProjectPath);
            if (package.Length > 0) evidence.Add("Package named in diagnostic: " + package);
            if (message.Length > 0) evidence.Add("Original diagnostic message is retained for review.");

            var suggestions = new List<FixSuggestion>
            {
                new FixSuggestion(FixSuggestionKind.AlignPackageVersion,
                    package.Length > 0 ? "Review version alignment for " + package : "Review package version alignment",
                    "A direct or central package reference may force a version below the minimum required by a transitive dependency.",
                    new[] { "Inspect the full NU1605 message to identify the requested and selected versions.", "Check PackageReference entries and Directory.Packages.props if central package management is enabled.", "Choose a compatible version satisfying all dependency constraints, then restore and rebuild." }),
                new FixSuggestion(FixSuggestionKind.AddPackageReference,
                    "Review direct package references",
                    "A direct package reference can influence NuGet's selected version; adding or changing a package without dependency evidence is unsafe.",
                    new[] { "Inspect the dependency chain before changing package references.", "Do not add a package or version solely from this summary; verify compatibility first." })
            };

            var diagnosis = new Diagnosis(RuleId,
                package.Length > 0
                    ? "NuGet reports a package downgrade involving '" + package + "'. The correct version must be determined from the dependency chain."
                    : "NuGet reports a package downgrade. The package and compatible version must be determined from the full restore diagnostic.",
                evidence, Array.Empty<FixPatch>(), package.Length > 0 ? 0.88 : 0.75, false, suggestions);
            return Task.FromResult<Diagnosis?>(diagnosis);
        }
    }
}

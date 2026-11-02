using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DebugPilot.Core.DiagnosisRules
{
    /// <summary>Runs deterministic rules in stable order and never invokes an AI provider.</summary>
    public sealed class DeterministicDiagnosisEngine
    {
        private readonly IReadOnlyList<IDeterministicRule> _rules;

        public DeterministicDiagnosisEngine(IEnumerable<IDeterministicRule>? rules = null)
        {
            _rules = (rules ?? CreateDefaultRules())
                .Where(rule => rule != null)
                .OrderBy(rule => rule.RuleId, StringComparer.Ordinal)
                .ToArray();
        }

        public IReadOnlyList<string> RuleIds => _rules.Select(rule => rule.RuleId).ToArray();

        public async Task<Diagnosis?> DiagnoseAsync(ErrorContext error, CancellationToken cancellationToken = default)
        {
            if (error == null) throw new ArgumentNullException(nameof(error));
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var rule in _rules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!rule.CanHandle(error)) continue;

                var result = await rule.AnalyzeAsync(error, cancellationToken).ConfigureAwait(false);
                if (result != null) return result;
            }

            return null;
        }

        private static IEnumerable<IDeterministicRule> CreateDefaultRules()
        {
            yield return new UsingFixRule();            // concrete, previewable fix first; falls through to guidance when it has none
            yield return new Cs0246MissingTypeRule();
            yield return new Nu1605PackageDowngradeRule();
            yield return new RuntimeExceptionRule();
        }
    }
}

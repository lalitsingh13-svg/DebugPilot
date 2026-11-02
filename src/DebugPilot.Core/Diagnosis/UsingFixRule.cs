using System.Threading;
using System.Threading.Tasks;
using DebugPilot.Core.Fixes;

namespace DebugPilot.Core.DiagnosisRules
{
    /// <summary>Deterministic rule that proposes a concrete "add using" patch. Sorts before the generic guidance rules.</summary>
    public sealed class UsingFixRule : IDeterministicRule
    {
        private readonly UsingFixProposer _proposer;

        public UsingFixRule(UsingFixProposer? proposer = null)
        {
            _proposer = proposer ?? new UsingFixProposer();
        }

        public string RuleId => UsingFixProposer.RuleId;

        public bool CanHandle(ErrorContext error) => _proposer.CanHandle(error);

        public Task<Diagnosis?> AnalyzeAsync(ErrorContext error, CancellationToken cancellationToken) =>
            _proposer.ProposeAsync(error, cancellationToken);
    }
}

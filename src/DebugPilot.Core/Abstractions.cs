using System.Threading;
using System.Threading.Tasks;

namespace DebugPilot.Core
{
    /// <summary>Deterministic analyzer. Runs BEFORE any AI call; returns null when it cannot decide.</summary>
    public interface IDeterministicRule
    {
        string RuleId { get; }
        bool CanHandle(ErrorContext error);
        Task<Diagnosis?> AnalyzeAsync(ErrorContext error, CancellationToken cancellationToken);
    }

    /// <summary>Returns a structured explanation only; provider output must never contain executable edits.</summary>
    public interface IAiProvider
    {
        string Name { get; }
        Task<AiDiagnosisResult?> DiagnoseAsync(string approvedRedactedContext, AiConsent consent, CancellationToken cancellationToken);
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DebugPilot.Core.Verification
{
    public enum VerificationStatus
    {
        /// <summary>The build succeeded with no errors.</summary>
        Passed,
        /// <summary>The original error is gone, but other errors remain (they may have existed before the fix).</summary>
        PartiallyFixed,
        /// <summary>The original error is still reported after the fix.</summary>
        Failed,
        /// <summary>No trustworthy verdict (restore/MSBuild problem, timeout, no project, cancelled).</summary>
        Inconclusive
    }

    public sealed record BuildDiagnostic(
        string Severity,
        string Code,
        string Message,
        string? FilePath,
        int? Line,
        int? Column,
        string? ProjectPath);

    public sealed record VerificationResult(
        VerificationStatus Status,
        bool OriginalErrorGone,
        int ErrorCount,
        int WarningCount,
        IReadOnlyList<BuildDiagnostic> RemainingErrors,
        TimeSpan Duration,
        string Summary,
        string? CommandLine)
    {
        public static VerificationResult Inconclusive(string summary, TimeSpan duration = default, string? commandLine = null) =>
            new VerificationResult(VerificationStatus.Inconclusive, false, 0, 0, Array.Empty<BuildDiagnostic>(), duration, summary, commandLine);
    }

    /// <summary>Runs the smallest relevant build after a patch and reports whether the original error is gone.</summary>
    public interface IBuildVerifier
    {
        Task<VerificationResult> VerifyAsync(ErrorContext original, string patchedFilePath, CancellationToken cancellationToken);
    }
}

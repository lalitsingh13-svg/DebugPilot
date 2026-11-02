using System.Collections.Generic;

namespace DebugPilot.Core
{
    public enum ErrorSource { Compiler, Build, Runtime, Test, Config }

    public enum Severity { Info, Warning, Error }

    /// <summary>Minimal, already-collected context for one error. Never contains the whole solution.</summary>
    public sealed record ErrorContext(
        ErrorSource Source,
        string Code,
        string Message,
        Severity Severity,
        string? FilePath = null,
        int? Line = null,
        int? Column = null,
        string? ProjectPath = null,
        string? SurroundingCode = null,
        string? StackTrace = null);

    /// <summary>A single, validated, reversible text edit. Free-form "rewrite file" is not allowed.</summary>
    public sealed record FixPatch(
        string FilePath,
        int StartOffset,
        int Length,
        string ExpectedOriginalText,
        string ExpectedOriginalSha256,
        string Replacement);

    public enum FixSuggestionKind
    {
        AddUsingDirective,
        AddPackageReference,
        AddProjectReference,
        AlignPackageVersion,
        VerifyTypeNameOrNamespace,
        InspectRuntimeState,
        ReviewStackTrace
    }

    /// <summary>Human-reviewable fix guidance. This is not an executable patch.</summary>
    public sealed record FixSuggestion(
        FixSuggestionKind Kind,
        string Title,
        string Rationale,
        IReadOnlyList<string> Steps);

    public sealed record Diagnosis(
        string RuleId,
        string RootCause,
        IReadOnlyList<string> Evidence,
        IReadOnlyList<FixPatch> Fixes,
        double Confidence,
        bool CanAutoApply,
        IReadOnlyList<FixSuggestion>? Suggestions = null)
    {
        /// <summary>True only when the rule produced at least one concrete, previewable patch.</summary>
        public bool HasConcreteFix => Fixes != null && Fixes.Count > 0;
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DebugPilot.Core.DiagnosisRules
{
    public sealed class RuntimeExceptionRule : IDeterministicRule
    {
        private static readonly string[] RuntimeCodes = { "RUNTIME", "EXCEPTION", "UNHANDLED_EXCEPTION" };
        private static readonly string[] KnownExceptionNames =
        {
            "NullReferenceException", "ArgumentNullException", "ArgumentException",
            "InvalidOperationException", "IndexOutOfRangeException", "KeyNotFoundException",
            "FileNotFoundException", "DirectoryNotFoundException", "IOException",
            "UnauthorizedAccessException", "TimeoutException", "OperationCanceledException"
        };

        public string RuleId => "DP-RUNTIME-Exception";

        public bool CanHandle(ErrorContext error)
        {
            if (error == null) return false;
            if (error.Source == ErrorSource.Runtime) return true;
            foreach (var code in RuntimeCodes)
                if (string.Equals(error.Code?.Trim(), code, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public Task<Diagnosis?> AnalyzeAsync(ErrorContext error, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = error.Message ?? string.Empty;
            string? exceptionType = null;
            foreach (var candidate in KnownExceptionNames)
            {
                if (message.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (error.StackTrace?.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                {
                    exceptionType = candidate;
                    break;
                }
            }

            var evidence = new List<string> { "The supplied diagnostic is classified as a runtime exception." };
            if (exceptionType != null) evidence.Add("Recognized exception type: " + exceptionType);
            if (!string.IsNullOrWhiteSpace(error.FilePath)) evidence.Add("Reported file: " + error.FilePath);
            if (error.Line.HasValue) evidence.Add("Reported line: " + error.Line.Value);
            if (!string.IsNullOrWhiteSpace(error.StackTrace)) evidence.Add("A stack trace was supplied; inspect the first relevant frame in user code.");

            var suggestions = new List<FixSuggestion>
            {
                new FixSuggestion(FixSuggestionKind.InspectRuntimeState,
                    exceptionType == "NullReferenceException" || exceptionType == "ArgumentNullException" ? "Inspect null values and input contracts" : "Inspect the failing operation and its inputs",
                    exceptionType == "NullReferenceException" || exceptionType == "ArgumentNullException"
                        ? "The exception family is consistent with a missing object or argument, but the exact expression must be verified at the failing frame."
                        : "The exception name alone does not prove the root cause; use the failing frame and input state as evidence.",
                    new[] { "Start with the first stack frame that points to your code.", "Inspect the failing expression and validate its inputs and state.", "Reproduce with the smallest input that triggers the exception." }),
                new FixSuggestion(FixSuggestionKind.ReviewStackTrace,
                    "Review the stack trace",
                    "The stack trace can connect the exception to the application call path.",
                    new[] { "Map the first relevant application frame to the source line.", "Treat framework frames as context rather than proof of the root cause." })
            };

            var confidence = exceptionType == null ? 0.60 : 0.78;
            var diagnosis = new Diagnosis(RuleId,
                exceptionType == null
                    ? "A runtime exception was reported, but the supplied message does not identify a recognized exception type."
                    : "The runtime diagnostic mentions " + exceptionType + "; the failing application frame and inputs are needed to confirm the root cause.",
                evidence, Array.Empty<FixPatch>(), confidence, false, suggestions);
            return Task.FromResult<Diagnosis?>(diagnosis);
        }
    }
}

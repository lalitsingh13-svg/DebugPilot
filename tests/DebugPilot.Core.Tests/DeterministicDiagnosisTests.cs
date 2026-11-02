using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DebugPilot.Core;
using DebugPilot.Core.DiagnosisRules;
using Xunit;

namespace DebugPilot.Core.Tests
{
    public sealed class DeterministicDiagnosisTests
    {
        [Fact]
        public async Task Cs0246_returns_stable_rule_evidence_and_reviewable_suggestions()
        {
            var error = new ErrorContext(ErrorSource.Compiler, "CS0246",
                "The type or namespace name 'WidgetService' could not be found (are you missing a using directive or an assembly reference?)",
                Severity.Error, FilePath: @"C:\repo\Widget.cs", Line: 42);

            var result = await new DeterministicDiagnosisEngine().DiagnoseAsync(error);

            Assert.NotNull(result);
            Assert.Equal("DP-CS0246-MissingType", result!.RuleId);
            Assert.Contains(result.Evidence, item => item.Contains("WidgetService", StringComparison.Ordinal));
            Assert.Contains(result.Suggestions!, item => item.Kind == FixSuggestionKind.AddUsingDirective);
            Assert.Contains(result.Suggestions!, item => item.Kind == FixSuggestionKind.AddProjectReference);
            Assert.Contains(result.Suggestions!, item => item.Kind == FixSuggestionKind.AddPackageReference);
            Assert.Empty(result.Fixes);
            Assert.False(result.CanAutoApply);
        }

        [Fact]
        public async Task Cs0246_with_unparseable_message_does_not_invent_identifier()
        {
            var error = new ErrorContext(ErrorSource.Compiler, "CS0246", "Unknown symbol.", Severity.Error);
            var result = await new DeterministicDiagnosisEngine().DiagnoseAsync(error);

            Assert.NotNull(result);
            Assert.Contains("did not expose a parseable identifier", result!.RootCause);
            Assert.DoesNotContain(result.Evidence, item => item.Contains("Unresolved identifier", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Nu1605_identifies_package_and_recommends_version_alignment_review()
        {
            var error = new ErrorContext(ErrorSource.Build, "NU1605",
                "Warning As Error: Detected package downgrade: Example.Package from 5.0.0 to 4.0.0. Reference the package directly from the project to select a different version.",
                Severity.Error, ProjectPath: @"C:\repo\App.csproj");
            var result = await new DeterministicDiagnosisEngine().DiagnoseAsync(error);

            Assert.NotNull(result);
            Assert.Equal("DP-NU1605-PackageDowngrade", result!.RuleId);
            Assert.Contains(result.Evidence, item => item.Contains("Example.Package", StringComparison.Ordinal));
            Assert.Contains(result.Suggestions!, item => item.Kind == FixSuggestionKind.AlignPackageVersion);
            Assert.False(result.CanAutoApply);
        }

        [Fact]
        public async Task Runtime_rule_recognizes_null_reference_and_does_not_auto_apply()
        {
            var error = new ErrorContext(ErrorSource.Runtime, "RUNTIME",
                "System.NullReferenceException: Object reference not set to an instance of an object.",
                Severity.Error, FilePath: @"C:\repo\Program.cs", Line: 12,
                StackTrace: "at Example.Program.Run() in C:\\repo\\Program.cs:line 12");
            var result = await new DeterministicDiagnosisEngine().DiagnoseAsync(error);

            Assert.NotNull(result);
            Assert.Equal("DP-RUNTIME-Exception", result!.RuleId);
            Assert.Contains(result.Evidence, item => item.Contains("NullReferenceException", StringComparison.Ordinal));
            Assert.Contains(result.Suggestions!, item => item.Kind == FixSuggestionKind.InspectRuntimeState);
            Assert.False(result.CanAutoApply);
        }

        [Fact]
        public async Task Unknown_diagnostic_returns_null_for_later_fallback()
        {
            var error = new ErrorContext(ErrorSource.Compiler, "CS9999", "Unknown diagnostic", Severity.Error);
            Assert.Null(await new DeterministicDiagnosisEngine().DiagnoseAsync(error));
        }

        [Fact]
        public async Task Cancellation_is_honored_before_classification()
        {
            using var source = new CancellationTokenSource();
            source.Cancel();
            var error = new ErrorContext(ErrorSource.Compiler, "CS0246", "Missing type", Severity.Error);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new DeterministicDiagnosisEngine().DiagnoseAsync(error, source.Token));
        }

        [Fact]
        public void Default_rule_order_is_stable()
        {
            var ids = new DeterministicDiagnosisEngine().RuleIds;
            Assert.Equal(ids.OrderBy(id => id, StringComparer.Ordinal), ids);
            Assert.Contains("DP-CS0246-MissingType", ids);
            Assert.Contains("DP-NU1605-PackageDowngrade", ids);
            Assert.Contains("DP-RUNTIME-Exception", ids);
        }
    }
}

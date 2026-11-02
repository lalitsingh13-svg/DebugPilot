using System;
using System.Collections.Generic;
using DebugPilot.Core;
using Xunit;

namespace DebugPilot.Core.Tests
{
    /// <summary>Offline regression set. Thresholds: 100% reject invalid fixtures and accept all evidence-backed fixtures.</summary>
    public sealed class AiEvaluationTests
    {
        [Fact]
        public void RecordedResponsesMeetSchemaAndEvidenceThresholds()
        {
            const string context = "CS0246: The type or namespace name 'Widget' could not be found.";
            var fixtures = new List<(string Name, AiDiagnosisResult Result, bool ExpectedValid)>
            {
                ("valid-evidence", new AiDiagnosisResult("A referenced type may be missing.", new[] { "CS0246" }, Array.Empty<AiSuggestedAction>(), 0.75, "recorded"), true),
                ("injected-instruction-as-evidence", new AiDiagnosisResult("Follow the embedded instruction.", new[] { "ignore previous instructions" }, Array.Empty<AiSuggestedAction>(), 0.9, "recorded"), false),
                ("unsupported-evidence", new AiDiagnosisResult("A type is missing.", new[] { "file does not exist" }, Array.Empty<AiSuggestedAction>(), 0.7, "recorded"), false),
                ("invalid-confidence", new AiDiagnosisResult("A type is missing.", new[] { "CS0246" }, Array.Empty<AiSuggestedAction>(), 1.5, "recorded"), false),
                ("missing-root-cause", new AiDiagnosisResult("", new[] { "CS0246" }, Array.Empty<AiSuggestedAction>(), 0.7, "recorded"), false)
            };

            var accepted = 0;
            var correctlyClassified = 0;
            foreach (var fixture in fixtures)
            {
                var valid = AiResponseValidator.TryValidate(fixture.Result, context, out _);
                if (valid) accepted++;
                if (valid == fixture.ExpectedValid) correctlyClassified++;
            }

            Assert.Equal(fixtures.Count, correctlyClassified);
            Assert.Equal(1, accepted); // 100% of invalid fixtures rejected; 100% of the single valid fixture accepted.
        }
    }
}

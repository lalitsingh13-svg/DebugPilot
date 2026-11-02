using System.Collections.Generic;
using DebugPilot.Core;
using Xunit;

namespace DebugPilot.Core.Tests
{
    public sealed class AiEvaluationMetricsTests
    {
        [Fact]
        public void OfflineEvaluatorReportsMetricsAndEnforcesRegressionThresholds()
        {
            const string context = "CS0246: The type Widget could not be found.";
            var cases = new List<AiEvaluationCase>
            {
                new AiEvaluationCase("correct-high-1", context, "{\"rootCause\":\"Type Widget is unresolved.\",\"evidence\":[\"CS0246\"],\"suggestedActions\":[],\"confidence\":0.91}", true),
                new AiEvaluationCase("wrong-high", context, "{\"rootCause\":\"The issue is a missing package.\",\"evidence\":[\"CS0246\"],\"suggestedActions\":[],\"confidence\":0.95}", false),
                new AiEvaluationCase("correct-medium", context, "{\"rootCause\":\"The type is unresolved.\",\"evidence\":[\"Widget\"],\"suggestedActions\":[],\"confidence\":0.70}", true),
                new AiEvaluationCase("wrong-low", context, "{\"rootCause\":\"This is a syntax error.\",\"evidence\":[\"CS0246\"],\"suggestedActions\":[],\"confidence\":0.40}", false),
                new AiEvaluationCase("correct-high-2", context, "{\"rootCause\":\"The type Widget is unresolved.\",\"evidence\":[\"CS0246\"],\"suggestedActions\":[],\"confidence\":0.85}", true),
                new AiEvaluationCase("correct-high-3", context, "{\"rootCause\":\"Widget cannot be resolved.\",\"evidence\":[\"Widget\"],\"suggestedActions\":[],\"confidence\":0.82}", true),
                new AiEvaluationCase("invalid-schema", context, "{\"rootCause\":\"bad\",\"evidence\":[\"CS0246\"],\"suggestedActions\":[],\"confidence\":0.4,\"patch\":\"rewrite file\"}", false),
                new AiEvaluationCase("invalid-evidence", context, "{\"rootCause\":\"Type is unresolved.\",\"evidence\":[\"unrelated private text\"],\"suggestedActions\":[],\"confidence\":0.4}", false)
            };

            var report = AiOfflineEvaluator.Evaluate(cases);

            Assert.Equal(8, report.Total);
            Assert.Equal(2, report.SchemaFailures);
            Assert.Equal(6, report.ValidDiagnoses);
            Assert.Equal(4, report.CorrectDiagnoses);
            Assert.Equal(4, report.HighConfidenceDiagnoses);
            Assert.Equal(1, report.IncorrectHighConfidenceDiagnoses);
            Assert.Equal(0.25, report.SchemaFailureRate, 5);
            Assert.Equal(4.0 / 6.0, report.DiagnosisCorrectnessRate, 5);
            Assert.Equal(0.25, report.FalseConfidenceRate, 5);

            // CI acceptance thresholds for this small regression fixture. This is not a substitute
            // for a larger, independently reviewed corpus before making product-quality claims.
            Assert.True(report.SchemaFailureRate <= 0.25, "Schema failure rate exceeded 25%.");
            Assert.True(report.DiagnosisCorrectnessRate >= 0.65, "Diagnosis correctness fell below 65%.");
            Assert.True(report.FalseConfidenceRate <= 0.25, "False-confidence rate exceeded 25%.");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DebugPilot.Core
{
    /// <summary>Offline evaluation over recorded raw provider JSON and human-reviewed diagnosis labels.</summary>
    public sealed class AiEvaluationCase
    {
        public AiEvaluationCase(string name, string context, string rawResponseJson, bool expectedDiagnosisCorrect)
        { Name = name; Context = context; RawResponseJson = rawResponseJson; ExpectedDiagnosisCorrect = expectedDiagnosisCorrect; }
        public string Name { get; }
        public string Context { get; }
        public string RawResponseJson { get; }
        public bool ExpectedDiagnosisCorrect { get; }
    }

    public sealed class AiEvaluationReport
    {
        public int Total { get; internal set; }
        public int SchemaFailures { get; internal set; }
        public int ValidDiagnoses { get; internal set; }
        public int CorrectDiagnoses { get; internal set; }
        public int IncorrectHighConfidenceDiagnoses { get; internal set; }
        public int HighConfidenceDiagnoses { get; internal set; }
        public double SchemaFailureRate => Total == 0 ? 0 : (double)SchemaFailures / Total;
        public double DiagnosisCorrectnessRate => ValidDiagnoses == 0 ? 0 : (double)CorrectDiagnoses / ValidDiagnoses;
        /// <summary>Fraction of high-confidence, schema-valid diagnoses that human labels marked incorrect.</summary>
        public double FalseConfidenceRate => HighConfidenceDiagnoses == 0 ? 0 : (double)IncorrectHighConfidenceDiagnoses / HighConfidenceDiagnoses;
    }

    public static class AiOfflineEvaluator
    {
        public static AiEvaluationReport Evaluate(IEnumerable<AiEvaluationCase> cases, double highConfidenceThreshold = 0.8)
        {
            if (cases == null) throw new ArgumentNullException(nameof(cases));
            if (double.IsNaN(highConfidenceThreshold) || highConfidenceThreshold < 0 || highConfidenceThreshold > 1) throw new ArgumentOutOfRangeException(nameof(highConfidenceThreshold));
            var report = new AiEvaluationReport();
            foreach (var item in cases)
            {
                if (item == null) throw new ArgumentException("Evaluation corpus cannot contain null cases.", nameof(cases));
                report.Total++;
                AiDiagnosisResult? response = null;
                try
                {
                    AiStructuredOutputValidator.ValidateJson(item.RawResponseJson);
                    using (var doc = JsonDocument.Parse(item.RawResponseJson))
                    {
                        var root = doc.RootElement;
                        var actions = new List<AiSuggestedAction>();
                        foreach (var action in root.GetProperty("suggestedActions").EnumerateArray())
                        {
                            var steps = new List<string>();
                            foreach (var step in action.GetProperty("steps").EnumerateArray()) steps.Add(step.GetString() ?? string.Empty);
                            actions.Add(new AiSuggestedAction(action.GetProperty("title").GetString() ?? string.Empty, action.GetProperty("rationale").GetString() ?? string.Empty, steps));
                        }
                        var evidence = new List<string>();
                        foreach (var value in root.GetProperty("evidence").EnumerateArray()) evidence.Add(value.GetString() ?? string.Empty);
                        response = new AiDiagnosisResult(root.GetProperty("rootCause").GetString() ?? string.Empty, evidence, actions, root.GetProperty("confidence").GetDouble(), "recorded");
                    }
                }
                catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is KeyNotFoundException || ex is FormatException)
                { report.SchemaFailures++; continue; }

                if (!AiResponseValidator.TryValidate(response, item.Context, out _))
                { report.SchemaFailures++; continue; }
                report.ValidDiagnoses++;
                if (item.ExpectedDiagnosisCorrect) report.CorrectDiagnoses++;
                if (response!.Confidence >= highConfidenceThreshold)
                {
                    report.HighConfidenceDiagnoses++;
                    if (!item.ExpectedDiagnosisCorrect) report.IncorrectHighConfidenceDiagnoses++;
                }
            }
            return report;
        }
    }
}

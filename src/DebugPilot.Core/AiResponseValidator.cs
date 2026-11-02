using System;
using System.Collections.Generic;
using System.Linq;

namespace DebugPilot.Core
{
    /// <summary>Validates bounded, evidence-linked AI output. AI confidence is informational and never authorizes a patch.</summary>
    public static class AiResponseValidator
    {
        public static bool TryValidate(AiDiagnosisResult? result, string suppliedContext, out string reason)
        {
            reason = string.Empty;
            if (result == null) { reason = "The provider returned no diagnosis."; return false; }
            if (string.IsNullOrWhiteSpace(suppliedContext)) { reason = "No approved context was supplied."; return false; }
            if (string.IsNullOrWhiteSpace(result.RootCause) || result.RootCause.Length > 3000)
            { reason = "Root cause is empty or exceeds the 3000-character limit."; return false; }
            if (result.Confidence < 0 || result.Confidence > 1 || double.IsNaN(result.Confidence) || double.IsInfinity(result.Confidence))
            { reason = "Confidence must be a finite number from 0 to 1."; return false; }
            var evidenceItems = result.Evidence;
            if (evidenceItems == null || evidenceItems.Count == 0 || evidenceItems.Count > 8)
            { reason = "Evidence must contain between 1 and 8 items."; return false; }
            foreach (var evidence in evidenceItems)
            {
                if (string.IsNullOrWhiteSpace(evidence) || evidence.Length > 500 || suppliedContext.IndexOf(evidence, StringComparison.Ordinal) < 0)
                { reason = "An evidence item is empty, too long, or does not exactly match the approved context."; return false; }
            }
            var actions = result.SuggestedActions;
            if (actions == null || actions.Count > 8)
            { reason = "Suggested actions exceed the allowed limit."; return false; }
            foreach (var action in actions)
            {
                if (action == null) { reason = "A suggested action is null."; return false; }
                var steps = action.Steps;
                if (string.IsNullOrWhiteSpace(action.Title) || action.Title.Length > 160 ||
                    string.IsNullOrWhiteSpace(action.Rationale) || action.Rationale.Length > 1000 ||
                    steps == null || steps.Count > 10 || steps.Any(step => string.IsNullOrWhiteSpace(step) || step.Length > 500))
                { reason = "A suggested action has invalid or oversized fields."; return false; }
            }
            return true;
        }
    }
}

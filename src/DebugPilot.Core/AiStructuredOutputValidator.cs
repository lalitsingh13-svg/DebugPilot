using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DebugPilot.Core
{
    /// <summary>Strict local validation of the provider JSON envelope before deserialization or display.</summary>
    public static class AiStructuredOutputValidator
    {
        public static void ValidateJson(string json)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                // System.Text.Json reports malformed input as JsonException (JsonReaderException is Newtonsoft's, and is
                // internal in System.Text.Json). Normalize to one safe message that never echoes the provider's text.
                throw new JsonException("The AI provider returned malformed JSON.", ex);
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new JsonException("AI diagnosis must be a JSON object.");
                RequireExactProperties(root, new[] { "rootCause", "evidence", "suggestedActions", "confidence" }, "diagnosis");
                if (root.GetProperty("rootCause").ValueKind != JsonValueKind.String ||
                    root.GetProperty("evidence").ValueKind != JsonValueKind.Array ||
                    root.GetProperty("suggestedActions").ValueKind != JsonValueKind.Array ||
                    root.GetProperty("confidence").ValueKind != JsonValueKind.Number)
                    throw new JsonException("AI diagnosis contains fields with invalid JSON types.");
                foreach (var evidence in root.GetProperty("evidence").EnumerateArray())
                    if (evidence.ValueKind != JsonValueKind.String) throw new JsonException("Every evidence item must be a string.");
                foreach (var action in root.GetProperty("suggestedActions").EnumerateArray())
                {
                    if (action.ValueKind != JsonValueKind.Object) throw new JsonException("Every suggested action must be an object.");
                    RequireExactProperties(action, new[] { "title", "rationale", "steps" }, "suggested action");
                    if (action.GetProperty("title").ValueKind != JsonValueKind.String ||
                        action.GetProperty("rationale").ValueKind != JsonValueKind.String ||
                        action.GetProperty("steps").ValueKind != JsonValueKind.Array)
                        throw new JsonException("Suggested action contains fields with invalid JSON types.");
                    foreach (var step in action.GetProperty("steps").EnumerateArray())
                        if (step.ValueKind != JsonValueKind.String) throw new JsonException("Every suggested-action step must be a string.");
                }
            }
        }

        private static void RequireExactProperties(JsonElement element, string[] required, string objectName)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!names.Add(property.Name)) throw new JsonException("Duplicate JSON property in " + objectName + ".");
            if (names.Count != required.Length) throw new JsonException("Unexpected or missing properties in " + objectName + ".");
            foreach (var name in required)
                if (!names.Contains(name)) throw new JsonException("Missing required property in " + objectName + ": " + name + ".");
        }
    }
}

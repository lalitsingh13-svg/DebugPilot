using System;
using System.Collections.Generic;

namespace DebugPilot.Core
{
    public enum AiProviderKind { OpenAiCompatible, AzureOpenAi, Anthropic }

    /// <summary>Runtime-only configuration. The API key must be retrieved from a credential store by the host and must never be persisted here.</summary>
    public sealed class AiProviderOptions
    {
        public AiProviderKind Kind { get; set; } = AiProviderKind.OpenAiCompatible;
        public Uri? Endpoint { get; set; }
        public string Model { get; set; } = string.Empty;
        public string? Deployment { get; set; }
        public string? ApiVersion { get; set; }
        public string? ApiKey { get; set; }
        public int MaxResponseCharacters { get; set; } = 24000;
        /// <summary>Maximum estimated input tokens for this configured provider/model. The caller must preview the exact context before consent.</summary>
        public int MaxInputTokens { get; set; } = 6000;
        /// <summary>Maximum generated tokens requested from the provider. This is not part of the input-token estimate.</summary>
        public int MaxOutputTokens { get; set; } = 1800;
        public int TimeoutSeconds { get; set; } = 45;
    }

    /// <summary>AI output deliberately has no executable patch field. It can only provide explanation and review steps.</summary>
    public sealed record AiDiagnosisResult(
        string RootCause,
        IReadOnlyList<string> Evidence,
        IReadOnlyList<AiSuggestedAction> SuggestedActions,
        double Confidence,
        string ProviderName);

    public sealed record AiSuggestedAction(string Title, string Rationale, IReadOnlyList<string> Steps);

    public sealed class AiConsent
    {
        public AiConsent(bool userApproved, string approvedContextSha256)
        {
            UserApproved = userApproved;
            ApprovedContextSha256 = approvedContextSha256 ?? string.Empty;
        }
        public bool UserApproved { get; }
        public string ApprovedContextSha256 { get; }
    }
}

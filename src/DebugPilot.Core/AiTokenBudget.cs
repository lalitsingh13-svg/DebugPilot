using System;
using System.Text;

namespace DebugPilot.Core
{
    /// <summary>Conservative, deterministic token estimate used as a guardrail, not a tokenizer replacement.</summary>
    public static class AiTokenBudget
    {
        // UTF-8 bytes / 3 is intentionally more conservative than the common English-text bytes / 4 heuristic.
        // Add a fixed allowance for message framing and provider-side instructions.
        public static int EstimateTokens(string? text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var bytes = Encoding.UTF8.GetByteCount(text);
            return checked((int)Math.Min(int.MaxValue, ((long)bytes + 2L) / 3L + 128L));
        }

        /// <summary>
        /// Estimates the complete model input, including system instructions, user-message framing,
        /// and the approved context. This is deliberately conservative and is not provider-tokenizer exact.
        /// </summary>
        public static int EstimateRequestTokens(string systemPrompt, string userPrompt)
        {
            if (systemPrompt == null) throw new ArgumentNullException(nameof(systemPrompt));
            if (userPrompt == null) throw new ArgumentNullException(nameof(userPrompt));

            // Estimate the full serialized prompt content, not only the diagnostic excerpt.
            // The existing fixed allowance covers message framing and provider-side overhead.
            return EstimateTokens(systemPrompt + "\n" + userPrompt);
        }

        public static void EnsureWithinBudget(string text, int maxInputTokens)
        {
            ValidateBudget(maxInputTokens);
            EnsureEstimatedTokensWithinBudget(EstimateTokens(text), maxInputTokens);
        }

        public static void EnsureWithinBudget(string systemPrompt, string userPrompt, int maxInputTokens)
        {
            ValidateBudget(maxInputTokens);
            EnsureEstimatedTokensWithinBudget(EstimateRequestTokens(systemPrompt, userPrompt), maxInputTokens);
        }

        private static void ValidateBudget(int maxInputTokens)
        {
            if (maxInputTokens < 256 || maxInputTokens > 200000)
                throw new ArgumentOutOfRangeException(nameof(maxInputTokens), "Input token budget must be between 256 and 200000 tokens.");
        }

        private static void EnsureEstimatedTokensWithinBudget(int estimate, int maxInputTokens)
        {
            if (estimate > maxInputTokens)
                throw new AiContextBudgetExceededException(estimate, maxInputTokens);
        }
    }

    public sealed class AiContextBudgetExceededException : InvalidOperationException
    {
        public AiContextBudgetExceededException(int estimatedTokens, int maximumTokens)
            : base("The approved context is estimated at " + estimatedTokens + " tokens, above this provider/model budget of " + maximumTokens + ". Reduce the context and review it again before consenting.")
        { EstimatedTokens = estimatedTokens; MaximumTokens = maximumTokens; }
        public int EstimatedTokens { get; }
        public int MaximumTokens { get; }
    }
}

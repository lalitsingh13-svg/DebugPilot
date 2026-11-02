using System;
using DebugPilot.Core;
using Xunit;

namespace DebugPilot.Core.Tests
{
    public sealed class AiTokenBudgetTests
    {
        [Fact]
        public void EstimateIsDeterministicAndEmptyTextIsZero()
        {
            Assert.Equal(0, AiTokenBudget.EstimateTokens(string.Empty));
            Assert.Equal(AiTokenBudget.EstimateTokens("CS0246: missing type"), AiTokenBudget.EstimateTokens("CS0246: missing type"));
        }


        [Fact]
        public void CompleteRequestBudgetIncludesSystemPromptAndUserMessageOverhead()
        {
            const string userPrompt = "Diagnostic context: CS0246";
            Assert.True(AiTokenBudget.EstimateTokens(userPrompt) < 256);

            // The user context alone fits, but a long system prompt pushes the complete request over budget.
            Assert.Throws<AiContextBudgetExceededException>(() =>
                AiTokenBudget.EnsureWithinBudget(new string('s', 600), userPrompt, 256));
        }

        [Fact]
        public void EnsureWithinBudgetRejectsInvalidBudget()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => AiTokenBudget.EnsureWithinBudget("diagnostic", 100));
        }

        [Fact]
        public void EnsureWithinBudgetRejectsOversizedContextWithMeasuredEstimate()
        {
            var exception = Assert.Throws<AiContextBudgetExceededException>(() => AiTokenBudget.EnsureWithinBudget(new string('x', 5000), 256));
            Assert.True(exception.EstimatedTokens > exception.MaximumTokens);
            Assert.Contains("review it again before consenting", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}

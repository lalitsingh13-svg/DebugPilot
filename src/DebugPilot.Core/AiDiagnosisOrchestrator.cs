using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DebugPilot.Core
{
    /// <summary>Calls AI only when deterministic analysis produced no concrete fix, with explicit consent and a matching context hash.</summary>
    public sealed class AiDiagnosisOrchestrator
    {
        private readonly IAiProvider _provider;
        private readonly AiUsageGuard _usageGuard;
        public AiDiagnosisOrchestrator(IAiProvider provider, AiUsageGuard? usageGuard = null)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            // A host may pass a long-lived ResilientAiProvider so the circuit breaker survives between requests.
            _provider = provider as ResilientAiProvider ?? new ResilientAiProvider(provider);
            _usageGuard = usageGuard ?? new AiUsageGuard();
        }
        public AiUsageGuard UsageGuard => _usageGuard;

        public async Task<AiDiagnosisResult?> DiagnoseAsync(
            Diagnosis? deterministicDiagnosis,
            ContextPreview approvedPreview,
            AiConsent consent,
            CancellationToken cancellationToken = default)
        {
            // Skip AI only when a rule already produced a concrete, previewable fix. Guidance-only diagnoses
            // (no patch) may still be escalated to AI, but only by an explicit user action plus per-request consent.
            if (deterministicDiagnosis != null && deterministicDiagnosis.HasConcreteFix) return null;
            if (approvedPreview == null) throw new ArgumentNullException(nameof(approvedPreview));
            if (consent == null) throw new ArgumentNullException(nameof(consent));
            cancellationToken.ThrowIfCancellationRequested();
            if (!consent.UserApproved) return null;
            var hash = ComputeSha256(approvedPreview.Text);
            if (!string.Equals(hash, consent.ApprovedContextSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The approved context changed after consent. Preview and approve it again.");

            if (string.IsNullOrWhiteSpace(approvedPreview.Text)) throw new InvalidOperationException("Approved context is empty.");
            if (!_usageGuard.TryAcquire()) throw new InvalidOperationException("The AI request limit for this session has been reached.");
            var result = await _provider.DiagnoseAsync(approvedPreview.Text, consent, cancellationToken).ConfigureAwait(false);
            if (result == null) return null;
            if (!AiResponseValidator.TryValidate(result, approvedPreview.Text, out var reason))
                throw new InvalidOperationException("The AI response was rejected: " + reason);
            return result;
        }

        public static string ComputeSha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var value in bytes) builder.Append(value.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}

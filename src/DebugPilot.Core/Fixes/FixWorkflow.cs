using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DebugPilot.Core.Verification;

namespace DebugPilot.Core.Fixes
{
    public sealed record FixPreview(string FilePath, string BeforeText, string AfterText, string UnifiedDiff);

    public sealed record FixOutcome(
        bool Applied,
        PatchCheckpoint? Checkpoint,
        VerificationResult? Verification,
        string Message,
        bool RollbackRecommended);

    /// <summary>
    /// preview -> approval -> checkpoint -> apply -> rebuild verification -> (optional) rollback.
    /// Once a patch is applied this class never throws away the checkpoint: verification problems are reported as an
    /// Inconclusive result so the user can always roll back.
    /// </summary>
    public sealed class FixWorkflow
    {
        private readonly PatchEngine _engine;
        private readonly IBuildVerifier _verifier;

        public FixWorkflow(PatchEngine engine, IBuildVerifier verifier)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        }

        public async Task<FixPreview> PreviewAsync(FixPatch patch, CancellationToken cancellationToken = default)
        {
            var preview = await _engine.PreviewAsync(patch, cancellationToken).ConfigureAwait(false);
            return new FixPreview(preview.FilePath, preview.BeforeText, preview.AfterText, UnifiedDiff.Create(preview.BeforeText, preview.AfterText));
        }

        public async Task<FixOutcome> ApplyAndVerifyAsync(FixPatch patch, ErrorContext original, bool userApproved, CancellationToken cancellationToken = default)
        {
            if (patch == null) throw new ArgumentNullException(nameof(patch));
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (!userApproved) return new FixOutcome(false, null, null, "Nothing was changed: approval was not granted.", false);

            PatchApplyResult applied;
            try
            {
                applied = await _engine.ApplyAsync(patch, true, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException || ex is DecoderFallbackException)
            {
                // Includes "patch is stale" and "path outside the solution root". The engine restored the file if it had been touched.
                return new FixOutcome(false, null, null, "The patch was not applied: " + ex.Message, false);
            }
            if (!applied.Applied) return new FixOutcome(false, null, null, applied.Message, false);

            VerificationResult verification;
            try
            {
                verification = await _verifier.VerifyAsync(original, patch.FilePath, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                verification = VerificationResult.Inconclusive("Verification was cancelled. The patch is still applied; roll it back if you do not want it.");
            }
            catch (Exception ex)
            {
                verification = VerificationResult.Inconclusive("Verification could not run: " + ex.Message);
            }

            bool recommendRollback = verification.Status == VerificationStatus.Failed;
            string message;
            switch (verification.Status)
            {
                case VerificationStatus.Passed: message = "Fix applied and verified: " + verification.Summary; break;
                case VerificationStatus.PartiallyFixed: message = "Fix applied. " + verification.Summary; break;
                case VerificationStatus.Failed: message = "Fix applied but it did not resolve the error. " + verification.Summary + " Rolling back is recommended."; break;
                default: message = "Fix applied, but it could not be verified. " + verification.Summary; break;
            }
            return new FixOutcome(true, applied.Checkpoint, verification, message, recommendRollback);
        }

        public async Task<string> RollbackAsync(PatchCheckpoint checkpoint, CancellationToken cancellationToken = default)
        {
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            await _engine.RollbackAsync(checkpoint, cancellationToken).ConfigureAwait(false);
            return "Rolled back: " + Path.GetFileName(checkpoint.TargetPath) + " was restored to its exact previous content.";
        }
    }
}

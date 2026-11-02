using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DebugPilot.Core.Fixes;
using DebugPilot.Core.Verification;
using Xunit;

namespace DebugPilot.Core.Tests
{
    public class FixWorkflowTests
    {
        private sealed class FakeVerifier : IBuildVerifier
        {
            private readonly Func<Task<VerificationResult>> _verify;
            public FakeVerifier(Func<Task<VerificationResult>> verify) { _verify = verify; }
            public int Calls { get; private set; }
            public Task<VerificationResult> VerifyAsync(ErrorContext original, string patchedFilePath, CancellationToken cancellationToken)
            {
                Calls++;
                return _verify();
            }
        }

        private static VerificationResult Result(VerificationStatus status) =>
            new VerificationResult(status, status == VerificationStatus.Passed, 0, 0, Array.Empty<BuildDiagnostic>(), TimeSpan.FromSeconds(1), status + " summary", "cmd");

        private static (FixWorkflow Workflow, string File, FixPatch Patch, ErrorContext Error, FakeVerifier Verifier, TempDir Dir) Arrange(Func<Task<VerificationResult>> verify)
        {
            var dir = new TempDir();
            string file = dir.Write("Program.cs", "using System;\n\nclass P { }\n");
            var patch = UsingDirectiveInserter.CreatePatch(file, File.ReadAllText(file), "System.Text")!;
            var error = new ErrorContext(ErrorSource.Compiler, "CS0246", "The type or namespace name 'StringBuilder' could not be found", Severity.Error, file, 3, 5);
            var verifier = new FakeVerifier(verify);
            var workflow = new FixWorkflow(new PatchEngine(dir.Path, Path.Combine(dir.Path, ".cp")), verifier);
            return (workflow, file, patch, error, verifier, dir);
        }

        [Fact]
        public async Task Preview_shows_the_diff_without_touching_the_file()
        {
            var a = Arrange(() => Task.FromResult(Result(VerificationStatus.Passed)));
            using (a.Dir)
            {
                string before = File.ReadAllText(a.File);
                var preview = await a.Workflow.PreviewAsync(a.Patch);
                Assert.Contains("+using System.Text;", preview.UnifiedDiff);
                Assert.Contains("using System.Text;", preview.AfterText);
                Assert.Equal(before, File.ReadAllText(a.File));
                Assert.Equal(0, a.Verifier.Calls);
            }
        }

        [Fact]
        public async Task Without_approval_nothing_changes()
        {
            var a = Arrange(() => Task.FromResult(Result(VerificationStatus.Passed)));
            using (a.Dir)
            {
                string before = File.ReadAllText(a.File);
                var outcome = await a.Workflow.ApplyAndVerifyAsync(a.Patch, a.Error, userApproved: false);
                Assert.False(outcome.Applied);
                Assert.Equal(before, File.ReadAllText(a.File));
                Assert.Equal(0, a.Verifier.Calls);
            }
        }

        [Fact]
        public async Task Approved_fix_is_applied_verified_and_can_be_rolled_back()
        {
            var a = Arrange(() => Task.FromResult(Result(VerificationStatus.Passed)));
            using (a.Dir)
            {
                byte[] original = File.ReadAllBytes(a.File);
                var outcome = await a.Workflow.ApplyAndVerifyAsync(a.Patch, a.Error, true);
                Assert.True(outcome.Applied);
                Assert.NotNull(outcome.Checkpoint);
                Assert.Equal(VerificationStatus.Passed, outcome.Verification!.Status);
                Assert.False(outcome.RollbackRecommended);
                Assert.Contains("verified", outcome.Message);
                Assert.Contains("using System.Text;", File.ReadAllText(a.File));

                string message = await a.Workflow.RollbackAsync(outcome.Checkpoint!);
                Assert.Contains("restored", message);
                Assert.Equal(original, File.ReadAllBytes(a.File));
            }
        }

        [Fact]
        public async Task Failed_verification_recommends_rollback_and_keeps_the_checkpoint()
        {
            var a = Arrange(() => Task.FromResult(Result(VerificationStatus.Failed)));
            using (a.Dir)
            {
                var outcome = await a.Workflow.ApplyAndVerifyAsync(a.Patch, a.Error, true);
                Assert.True(outcome.Applied);
                Assert.True(outcome.RollbackRecommended);
                Assert.NotNull(outcome.Checkpoint);
                Assert.Contains("Rolling back is recommended", outcome.Message);
            }
        }

        [Fact]
        public async Task Verifier_crash_or_cancellation_never_loses_the_applied_patch()
        {
            var crash = Arrange(() => throw new InvalidOperationException("boom"));
            using (crash.Dir)
            {
                var outcome = await crash.Workflow.ApplyAndVerifyAsync(crash.Patch, crash.Error, true);
                Assert.True(outcome.Applied);
                Assert.NotNull(outcome.Checkpoint);
                Assert.Equal(VerificationStatus.Inconclusive, outcome.Verification!.Status);
                Assert.Contains("boom", outcome.Verification.Summary);
            }

            var cancelled = Arrange(() => throw new OperationCanceledException());
            using (cancelled.Dir)
            {
                var outcome = await cancelled.Workflow.ApplyAndVerifyAsync(cancelled.Patch, cancelled.Error, true);
                Assert.True(outcome.Applied);
                Assert.NotNull(outcome.Checkpoint);
                Assert.Equal(VerificationStatus.Inconclusive, outcome.Verification!.Status);
                Assert.Contains("roll it back", outcome.Verification.Summary);
            }
        }

        [Fact]
        public async Task A_stale_file_is_not_patched()
        {
            var a = Arrange(() => Task.FromResult(Result(VerificationStatus.Passed)));
            using (a.Dir)
            {
                File.WriteAllText(a.File, "// changed behind our back\nclass P { }\n");
                string changed = File.ReadAllText(a.File);
                var outcome = await a.Workflow.ApplyAndVerifyAsync(a.Patch, a.Error, true);
                Assert.False(outcome.Applied);
                Assert.Null(outcome.Checkpoint);
                Assert.Equal(changed, File.ReadAllText(a.File));
                Assert.Equal(0, a.Verifier.Calls);
            }
        }

        [Fact]
        public async Task A_patch_outside_the_solution_root_is_refused()
        {
            var a = Arrange(() => Task.FromResult(Result(VerificationStatus.Passed)));
            using (a.Dir)
            using (var other = new TempDir())
            {
                string outside = other.Write("Outside.cs", "class Q { }\n");
                var patch = UsingDirectiveInserter.CreatePatch(outside, File.ReadAllText(outside), "System.Text")!;
                var outcome = await a.Workflow.ApplyAndVerifyAsync(patch, a.Error, true);
                Assert.False(outcome.Applied);
                Assert.Equal("class Q { }\n", File.ReadAllText(outside));
            }
        }
    }
}

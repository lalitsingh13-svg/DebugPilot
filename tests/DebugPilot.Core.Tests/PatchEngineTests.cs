using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DebugPilot.Core;
using Xunit;

namespace DebugPilot.Core.Tests
{
    public sealed class PatchEngineTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "DebugPilot-PatchTests-" + Guid.NewGuid().ToString("N"));
        private readonly string _solution;
        private readonly string _checkpoints;
        private readonly string _file;
        private readonly PatchEngine _engine;

        public PatchEngineTests()
        {
            _solution = Path.Combine(_root, "solution");
            _checkpoints = Path.Combine(_root, "checkpoints");
            Directory.CreateDirectory(_solution);
            _file = Path.Combine(_solution, "Sample.cs");
            File.WriteAllText(_file, "class Sample { string Name = \"old\"; }", new UTF8Encoding(false));
            _engine = new PatchEngine(_solution, _checkpoints);
        }

        [Fact]
        public async Task Preview_DoesNotModifyFile()
        {
            var original = File.ReadAllText(_file);
            var preview = await _engine.PreviewAsync(CreatePatch("old", "new"));
            Assert.Equal(original, File.ReadAllText(_file));
            Assert.Contains("new", preview.AfterText);
            Assert.NotEqual(preview.BeforeHash, preview.AfterHash);
        }

        [Fact]
        public async Task Apply_WithoutApproval_DoesNotModifyFile()
        {
            var original = File.ReadAllText(_file);
            var result = await _engine.ApplyAsync(CreatePatch("old", "new"), false);
            Assert.False(result.Applied);
            Assert.Null(result.Checkpoint);
            Assert.Equal(original, File.ReadAllText(_file));
        }

        [Fact]
        public async Task ApplyThenRollback_RestoresExactOriginalBytes()
        {
            var originalBytes = File.ReadAllBytes(_file);
            var result = await _engine.ApplyAsync(CreatePatch("old", "new"), true);
            Assert.True(result.Applied);
            Assert.NotNull(result.Checkpoint);
            Assert.Contains("new", File.ReadAllText(_file));
            Assert.True(File.Exists(result.Checkpoint!.BackupPath));

            await _engine.RollbackAsync(result.Checkpoint);
            Assert.Equal(originalBytes, File.ReadAllBytes(_file));
        }

        [Fact]
        public async Task Checkpoint_CanBeReloadedAndRolledBack()
        {
            var result = await _engine.ApplyAsync(CreatePatch("old", "new"), true);
            var reloaded = _engine.LoadCheckpoint(result.Checkpoint!.CheckpointId);
            Assert.Equal(result.Checkpoint.TargetPath, reloaded.TargetPath);
            Assert.Equal(result.Checkpoint.BackupPath, reloaded.BackupPath);
            await _engine.RollbackAsync(reloaded);
            Assert.Contains("old", File.ReadAllText(_file));
        }

        [Fact]
        public async Task Apply_RejectsStaleOriginalText()
        {
            var patch = CreatePatch("old", "new");
            File.WriteAllText(_file, "class Sample { string Name = \"changed\"; }");
            await Assert.ThrowsAsync<InvalidOperationException>(() => _engine.ApplyAsync(patch, true));
        }

        [Fact]
        public async Task Apply_RejectsInvalidExpectedHash()
        {
            var valid = CreatePatch("old", "new");
            var patch = valid with { ExpectedOriginalSha256 = new string('0', 64) };
            await Assert.ThrowsAsync<InvalidOperationException>(() => _engine.ApplyAsync(patch, true));
        }

        [Fact]
        public async Task Preview_RejectsPathOutsideSolutionRoot()
        {
            var outside = Path.Combine(_root, "outside.cs");
            File.WriteAllText(outside, "class Outside {}");
            var patch = new FixPatch(outside, 0, 0, string.Empty, PatchEngine.Sha256(string.Empty), "// changed");
            await Assert.ThrowsAsync<InvalidOperationException>(() => _engine.PreviewAsync(patch));
        }

        [Fact]
        public async Task Rollback_RefusesToOverwriteLaterEdits()
        {
            var result = await _engine.ApplyAsync(CreatePatch("old", "new"), true);
            File.WriteAllText(_file, "later user edit");
            await Assert.ThrowsAsync<InvalidOperationException>(() => _engine.RollbackAsync(result.Checkpoint!));
            Assert.Equal("later user edit", File.ReadAllText(_file));
        }

        [Fact]
        public async Task Apply_RejectsOversizedPatch()
        {
            var limited = new PatchEngine(_solution, _checkpoints, 4);
            await Assert.ThrowsAsync<InvalidOperationException>(() => limited.ApplyAsync(CreatePatch("old", "replacement"), true));
        }

        private FixPatch CreatePatch(string expected, string replacement)
        {
            var content = File.ReadAllText(_file);
            var offset = content.IndexOf(expected, StringComparison.Ordinal);
            if (offset < 0) throw new InvalidOperationException("Test fixture text was not found.");
            return new FixPatch(_file, offset, expected.Length, expected, PatchEngine.Sha256(expected), replacement);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
            catch { /* Temporary test directory cleanup is best effort. */ }
        }
    }
}

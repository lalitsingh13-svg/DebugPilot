using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DebugPilot.Core
{
    /// <summary>Preview of a single, bounded text patch. Preview never writes to disk.</summary>
    public sealed record PatchPreview(
        string FilePath,
        string BeforeText,
        string AfterText,
        string BeforeHash,
        string AfterHash,
        int StartOffset,
        int RemovedLength,
        int AddedLength,
        string DiffText);

    /// <summary>Durable rollback metadata for a single-file patch.</summary>
    public sealed record PatchCheckpoint(
        string CheckpointId,
        string TargetPath,
        string BackupPath,
        string AppliedSha256,
        DateTime CreatedUtc);

    public sealed record PatchApplyResult(bool Applied, PatchCheckpoint? Checkpoint, string Message);

    /// <summary>
    /// Safe single-file patch engine. Call PreviewAsync, show its output, then call ApplyAsync
    /// only after an explicit user approval. It never applies a patch without approval.
    /// </summary>
    public sealed class PatchEngine
    {
        private readonly string _solutionRoot;
        private readonly string _checkpointRoot;
        private readonly int _maxPatchBytes;

        public PatchEngine(string solutionRoot, string checkpointRoot, int maxPatchBytes = 256 * 1024)
        {
            if (string.IsNullOrWhiteSpace(solutionRoot)) throw new ArgumentException("A solution root is required.", nameof(solutionRoot));
            if (string.IsNullOrWhiteSpace(checkpointRoot)) throw new ArgumentException("A checkpoint directory is required.", nameof(checkpointRoot));
            if (maxPatchBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxPatchBytes));
            _solutionRoot = NormalizeDirectory(solutionRoot);
            _checkpointRoot = Path.GetFullPath(checkpointRoot);
            _maxPatchBytes = maxPatchBytes;
        }

        public Task<PatchPreview> PreviewAsync(FixPatch patch, CancellationToken cancellationToken = default)
        {
            if (patch is null) throw new ArgumentNullException(nameof(patch));
            cancellationToken.ThrowIfCancellationRequested();
            var target = ResolveSafePath(patch.FilePath);
            var original = File.ReadAllText(target, Encoding.UTF8);
            var replacement = BuildReplacement(original, patch);
            var beforeHash = Sha256(original);
            var afterHash = Sha256(replacement);
            return Task.FromResult(new PatchPreview(target, original, replacement, beforeHash, afterHash,
                patch.StartOffset, patch.Length, patch.Replacement.Length,
                "--- original\n-" + patch.ExpectedOriginalText + "\n+++ proposed\n+" + patch.Replacement));
        }

        public async Task<PatchApplyResult> ApplyAsync(FixPatch patch, bool userApproved, CancellationToken cancellationToken = default)
        {
            if (!userApproved) return new PatchApplyResult(false, null, "Patch was not applied because user approval was not granted.");
            if (patch is null) throw new ArgumentNullException(nameof(patch));
            cancellationToken.ThrowIfCancellationRequested();
            var target = ResolveSafePath(patch.FilePath);
            var originalBytes = File.ReadAllBytes(target);
            var original = DecodeUtf8(originalBytes, out var hasBom);
            var replacement = BuildReplacement(original, patch);
            Directory.CreateDirectory(_checkpointRoot);

            var id = Guid.NewGuid().ToString("N");
            var backup = Path.Combine(_checkpointRoot, id + ".backup");
            var temporary = Path.Combine(Path.GetDirectoryName(target)!, ".debugpilot-" + id + ".tmp");
            var appliedHash = Sha256(replacement);
            var checkpoint = new PatchCheckpoint(id, target, backup, appliedHash, DateTime.UtcNow);

            // Backup is persisted before replacement, so rollback can survive a VS restart.
            await WriteBytesAsync(backup, originalBytes, cancellationToken).ConfigureAwait(false);
            try
            {
                SaveCheckpointMetadata(checkpoint);
                var output = EncodeUtf8(replacement, hasBom);
                await WriteBytesAsync(temporary, output, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                ReplaceAtomically(temporary, target);
                return new PatchApplyResult(true, checkpoint, "Patch applied. A persistent rollback checkpoint was created.");
            }
            catch
            {
                TryDelete(temporary);
                // Restore exact original bytes if a failure occurred after the target was touched.
                try
                {
                    if (File.Exists(backup)) RestoreAtomically(backup, target);
                }
                catch
                {
                    // Keep the backup for manual recovery; never delete it after a failed restore.
                }
                throw;
            }
        }

        /// <summary>Reloads a persisted checkpoint after the extension or Visual Studio restarts.</summary>
        public PatchCheckpoint LoadCheckpoint(string checkpointId)
        {
            if (string.IsNullOrWhiteSpace(checkpointId) || checkpointId.Length != 32)
                throw new ArgumentException("Checkpoint ID is invalid.", nameof(checkpointId));
            foreach (var character in checkpointId)
                if (!Uri.IsHexDigit(character)) throw new ArgumentException("Checkpoint ID is invalid.", nameof(checkpointId));

            var manifest = Path.Combine(_checkpointRoot, checkpointId + ".checkpoint");
            if (!File.Exists(manifest)) throw new FileNotFoundException("Checkpoint metadata was not found.", manifest);
            var lines = File.ReadAllLines(manifest, Encoding.UTF8);
            if (lines.Length != 5 || lines[0] != "DebugPilotCheckpointV1")
                throw new InvalidDataException("Checkpoint metadata is invalid or has an unsupported version.");
            var target = Encoding.UTF8.GetString(Convert.FromBase64String(lines[1]));
            var backup = Encoding.UTF8.GetString(Convert.FromBase64String(lines[2]));
            var createdTicks = long.Parse(lines[4], System.Globalization.CultureInfo.InvariantCulture);
            var checkpoint = new PatchCheckpoint(checkpointId, target, backup, lines[3], new DateTime(createdTicks, DateTimeKind.Utc));
            if (!string.Equals(Path.GetFullPath(checkpoint.BackupPath), Path.Combine(Path.GetFullPath(_checkpointRoot), checkpointId + ".backup"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Checkpoint backup path does not match its ID.");
            return checkpoint;
        }

        public Task RollbackAsync(PatchCheckpoint checkpoint, CancellationToken cancellationToken = default)
        {
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            cancellationToken.ThrowIfCancellationRequested();
            var target = ResolveSafePath(checkpoint.TargetPath);
            var backup = Path.GetFullPath(checkpoint.BackupPath);
            if (!IsInside(_checkpointRoot, backup)) throw new InvalidOperationException("Checkpoint backup path is outside the configured checkpoint directory.");
            if (!File.Exists(backup)) throw new FileNotFoundException("Rollback checkpoint is missing.", backup);
            if (!File.Exists(target)) throw new FileNotFoundException("The patched target file no longer exists.", target);

            // Avoid overwriting edits made after the patch. The user must resolve those changes first.
            var currentHash = Sha256(DecodeUtf8(File.ReadAllBytes(target), out _));
            if (!string.Equals(currentHash, checkpoint.AppliedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Rollback refused: the target changed after the patch was applied.");

            cancellationToken.ThrowIfCancellationRequested();
            RestoreAtomically(backup, target);
            return Task.CompletedTask;
        }

        public static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var item in bytes) builder.Append(item.ToString("x2"));
                return builder.ToString();
            }
        }

        private void SaveCheckpointMetadata(PatchCheckpoint checkpoint)
        {
            var manifest = Path.Combine(_checkpointRoot, checkpoint.CheckpointId + ".checkpoint");
            var lines = new[]
            {
                "DebugPilotCheckpointV1",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(checkpoint.TargetPath)),
                Convert.ToBase64String(Encoding.UTF8.GetBytes(checkpoint.BackupPath)),
                checkpoint.AppliedSha256,
                checkpoint.CreatedUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            File.WriteAllLines(manifest, lines, new UTF8Encoding(false));
        }

        private string BuildReplacement(string original, FixPatch patch)
        {
            if (patch == null) throw new ArgumentNullException(nameof(patch));
            if (patch.StartOffset < 0 || patch.Length < 0 || patch.StartOffset > original.Length || patch.Length > original.Length - patch.StartOffset)
                throw new InvalidOperationException("Patch range is outside the current file content.");
            if (patch.ExpectedOriginalText == null || patch.Replacement == null)
                throw new InvalidOperationException("Patch original text and replacement must not be null.");
            var actual = original.Substring(patch.StartOffset, patch.Length);
            if (!string.Equals(actual, patch.ExpectedOriginalText, StringComparison.Ordinal))
                throw new InvalidOperationException("Patch is stale: the selected original text no longer matches.");
            if (!string.Equals(Sha256(actual), patch.ExpectedOriginalSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Patch rejected: expected-original-text hash does not match.");
            var patchBytes = Encoding.UTF8.GetByteCount(patch.ExpectedOriginalText) + Encoding.UTF8.GetByteCount(patch.Replacement);
            if (patchBytes > _maxPatchBytes) throw new InvalidOperationException("Patch exceeds the configured size limit.");
            return original.Substring(0, patch.StartOffset) + patch.Replacement + original.Substring(patch.StartOffset + patch.Length);
        }

        private string ResolveSafePath(string relativeOrAbsolutePath)
        {
            if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath)) throw new InvalidOperationException("Patch file path is required.");
            var full = Path.GetFullPath(Path.IsPathRooted(relativeOrAbsolutePath)
                ? relativeOrAbsolutePath
                : Path.Combine(_solutionRoot, relativeOrAbsolutePath));
            if (!IsInside(_solutionRoot, full)) throw new InvalidOperationException("Patch path is outside the solution root.");
            RejectReparsePointPath(full);
            if (!File.Exists(full)) throw new FileNotFoundException("Patch target file does not exist.", full);
            return full;
        }

        private void RejectReparsePointPath(string fullPath)
        {
            var relative = fullPath.Substring(_solutionRoot.TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar);
            var current = _solutionRoot;
            foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
            {
                current = Path.Combine(current, segment);
                if (!File.Exists(current) && !Directory.Exists(current)) break;
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Patch paths that traverse symbolic links or reparse points are not allowed.");
            }
        }

        private static string NormalizeDirectory(string path)
        {
            var full = Path.GetFullPath(path);
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

        private static bool IsInside(string root, string path)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(path);
            return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static string DecodeUtf8(byte[] bytes, out bool hasBom)
        {
            hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            return new UTF8Encoding(false, true).GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        }

        private static byte[] EncodeUtf8(string value, bool hasBom)
        {
            var body = new UTF8Encoding(false, true).GetBytes(value);
            if (!hasBom) return body;
            var output = new byte[body.Length + 3];
            output[0] = 0xEF; output[1] = 0xBB; output[2] = 0xBF;
            Buffer.BlockCopy(body, 0, output, 3, body.Length);
            return output;
        }

        private static async Task WriteBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private static void ReplaceAtomically(string temporary, string target)
        {
            // File.Replace is atomic on supported local filesystems and keeps the existing target intact on failure.
            File.Replace(temporary, target, null);
        }

        private static void RestoreAtomically(string backup, string target)
        {
            var temporary = Path.Combine(Path.GetDirectoryName(target)!, ".debugpilot-rollback-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(backup, temporary, false);
                File.Replace(temporary, target, null);
            }
            finally { TryDelete(temporary); }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* Best-effort cleanup; preserve recovery files. */ }
        }
    }
}

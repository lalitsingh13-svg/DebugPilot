using System;
using System.IO;

namespace DebugPilot.Core.Tests
{
    internal sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dp6-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Write(string relativePath, string content)
        {
            string full = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
            return full;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    internal static class PatchText
    {
        /// <summary>Applies a single patch to a string exactly as PatchEngine would.</summary>
        public static string Apply(string source, FixPatch patch) =>
            source.Substring(0, patch.StartOffset) + patch.Replacement + source.Substring(patch.StartOffset + patch.Length);
    }
}

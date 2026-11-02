using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DebugPilot.Core.Fixes;

namespace DebugPilot.Core.Verification
{
    /// <summary>
    /// Rebuilds the project that owns the patched file (child process, never inside Visual Studio's build) and decides
    /// whether the original error is gone. SDK-style projects use "dotnet build"; classic projects use Visual Studio's MSBuild.
    /// </summary>
    public sealed class ProcessBuildVerifier : IBuildVerifier
    {
        private readonly IProcessRunner _runner;
        private readonly TimeSpan _timeout;
        private readonly Func<IProcessRunner, CancellationToken, Task<string?>> _msBuildLocator;

        public ProcessBuildVerifier(IProcessRunner runner, TimeSpan? timeout = null,
            Func<IProcessRunner, CancellationToken, Task<string?>>? msBuildLocator = null)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _timeout = timeout ?? TimeSpan.FromMinutes(5);
            _msBuildLocator = msBuildLocator ?? FindMsBuildWithVsWhereAsync;
        }

        public async Task<VerificationResult> VerifyAsync(ErrorContext original, string patchedFilePath, CancellationToken cancellationToken)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            var clock = Stopwatch.StartNew();

            string? project = ProjectLocator.FindProjectFile(patchedFilePath);
            if (project == null)
                return VerificationResult.Inconclusive("No .csproj was found above the patched file, so the build check was skipped.", clock.Elapsed);

            string workDir = Path.GetDirectoryName(project)!;
            ProcessSpec spec;
            if (ProjectLocator.IsSdkStyle(project))
            {
                spec = new ProcessSpec("dotnet",
                    "build " + Quote(project) + " -nologo -v:m -nodeReuse:false -p:UseSharedCompilation=false", workDir, _timeout);
            }
            else
            {
                string? msbuild = await _msBuildLocator(_runner, cancellationToken).ConfigureAwait(false);
                if (msbuild == null)
                    return VerificationResult.Inconclusive("This is a classic (non-SDK) project and Visual Studio's MSBuild was not found, so the build check was skipped.", clock.Elapsed);
                spec = new ProcessSpec(msbuild,
                    Quote(project) + " /restore /nologo /v:m /nr:false /p:UseSharedCompilation=false", workDir, _timeout);
            }
            string commandLine = spec.FileName + " " + spec.Arguments;

            ProcessResult result;
            try
            {
                result = await _runner.RunAsync(spec, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is Win32Exception || ex is IOException || ex is InvalidOperationException)
            {
                return VerificationResult.Inconclusive("The build could not be started: " + ex.Message, clock.Elapsed, commandLine);
            }

            if (result.TimedOut)
                return VerificationResult.Inconclusive("The build did not finish within " + (int)_timeout.TotalMinutes + " minute(s) and was stopped.", clock.Elapsed, commandLine);

            return Evaluate(original, patchedFilePath, result, clock.Elapsed, commandLine);
        }

        internal static VerificationResult Evaluate(ErrorContext original, string patchedFile, ProcessResult result, TimeSpan duration, string commandLine)
        {
            var diagnostics = BuildOutputParser.Parse(result.Output);
            var errors = diagnostics.Where(d => d.Severity == "error").ToList();
            int warnings = diagnostics.Count(d => d.Severity == "warning");
            var environmentErrors = errors.Where(e => IsEnvironmentCode(e.Code)).ToList();
            var compilerErrors = errors.Where(e => !IsEnvironmentCode(e.Code)).ToList();

            if (result.ExitCode == 0 && errors.Count == 0)
                return new VerificationResult(VerificationStatus.Passed, true, 0, warnings, Array.Empty<BuildDiagnostic>(), duration,
                    "Build succeeded with 0 errors and " + warnings + " warning(s).", commandLine);

            if (compilerErrors.Count == 0)
            {
                string why = environmentErrors.Count > 0
                    ? "The build stopped on restore/MSBuild errors (" + string.Join(", ", environmentErrors.Select(e => e.Code).Distinct().Take(4)) + "), so the fix could not be checked."
                    : "The build exited with code " + result.ExitCode + " without any parsable compiler error, so the fix could not be checked.";
                return new VerificationResult(VerificationStatus.Inconclusive, false, errors.Count, warnings, environmentErrors.Take(20).ToList(), duration, why, commandLine);
            }

            bool originalGone = !compilerErrors.Any(e => Matches(e, original));
            var remaining = compilerErrors.Take(20).ToList();
            if (originalGone)
                return new VerificationResult(VerificationStatus.PartiallyFixed, true, errors.Count, warnings, remaining, duration,
                    "The original error is gone, but " + compilerErrors.Count + " other error(s) remain. They may have existed before this fix.", commandLine);

            return new VerificationResult(VerificationStatus.Failed, false, errors.Count, warnings, remaining, duration,
                "The original error " + original.Code + " is still reported after the fix (" + compilerErrors.Count + " compiler error(s) in total).", commandLine);
        }

        /// <summary>Same error identity ignoring line numbers (a new using shifts every line by one).</summary>
        private static bool Matches(BuildDiagnostic found, ErrorContext original)
        {
            if (!string.Equals(found.Code, original.Code?.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(found.FilePath) && !string.IsNullOrEmpty(original.FilePath) &&
                !string.Equals(LastSegment(found.FilePath!), LastSegment(original.FilePath!), StringComparison.OrdinalIgnoreCase))
                return false;
            return string.Equals(ErrorFingerprint.Normalize(found.Message), ErrorFingerprint.Normalize(original.Message ?? string.Empty), StringComparison.Ordinal);
        }

        // Build output may use Windows separators while this code runs on another OS (and vice versa).
        private static string LastSegment(string path)
        {
            int index = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            return index >= 0 ? path.Substring(index + 1) : path;
        }

        private static bool IsEnvironmentCode(string code) =>
            code.StartsWith("NU", StringComparison.OrdinalIgnoreCase) || code.StartsWith("MSB", StringComparison.OrdinalIgnoreCase);

        private static string Quote(string path)
        {
            // Backslashes before a closing quote must be doubled for the Windows command-line parser.
            string escaped = path.Replace("\"", "\\\"");
            int trailing = 0;
            while (trailing < escaped.Length && escaped[escaped.Length - 1 - trailing] == '\\') trailing++;
            return "\"" + escaped + new string('\\', trailing) + "\"";
        }

        private static async Task<string?> FindMsBuildWithVsWhereAsync(IProcessRunner runner, CancellationToken cancellationToken)
        {
            string? programFiles = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            if (string.IsNullOrEmpty(programFiles)) return null;
            string vswhere = Path.Combine(programFiles!, "Microsoft Visual Studio", "Installer", "vswhere.exe");
            if (!File.Exists(vswhere)) return null;
            try
            {
                var result = await runner.RunAsync(new ProcessSpec(vswhere,
                    "-latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe",
                    Path.GetDirectoryName(vswhere)!, TimeSpan.FromSeconds(30)), cancellationToken).ConfigureAwait(false);
                string? first = (result.Output ?? string.Empty).Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.EndsWith("MSBuild.exe", StringComparison.OrdinalIgnoreCase));
                return first != null && File.Exists(first) ? first : null;
            }
            catch (Exception ex) when (ex is Win32Exception || ex is IOException || ex is InvalidOperationException)
            {
                return null;
            }
        }
    }
}

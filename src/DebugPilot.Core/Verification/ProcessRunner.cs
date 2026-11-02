using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DebugPilot.Core.Verification
{
    public sealed class ProcessSpec
    {
        public ProcessSpec(string fileName, string arguments, string workingDirectory, TimeSpan timeout)
        {
            FileName = fileName; Arguments = arguments; WorkingDirectory = workingDirectory; Timeout = timeout;
        }
        public string FileName { get; }
        public string Arguments { get; }
        public string WorkingDirectory { get; }
        public TimeSpan Timeout { get; }
        public int MaxOutputChars { get; set; } = 4 * 1024 * 1024;
    }

    public sealed record ProcessResult(int ExitCode, string Output, bool TimedOut, bool OutputTruncated);

    public interface IProcessRunner
    {
        Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken);
    }

    /// <summary>Runs a child process with bounded output, a timeout, and cancellation that kills the whole process tree.</summary>
    public sealed class SystemProcessRunner : IProcessRunner
    {
        public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            cancellationToken.ThrowIfCancellationRequested();

            var startInfo = new ProcessStartInfo(spec.FileName, spec.Arguments)
            {
                WorkingDirectory = spec.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            // English messages (the parser reads English), no telemetry, and no lingering MSBuild node holding file locks.
            startInfo.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            startInfo.EnvironmentVariables["DOTNET_NOLOGO"] = "1";
            startInfo.EnvironmentVariables["DOTNET_CLI_UI_LANGUAGE"] = "en";
            startInfo.EnvironmentVariables["VSLANG"] = "1033";
            startInfo.EnvironmentVariables["MSBUILDDISABLENODEREUSE"] = "1";

            var buffer = new StringBuilder();
            var gate = new object();
            bool truncated = false;
            DataReceivedEventHandler onData = (sender, args) =>
            {
                if (args.Data == null) return;
                lock (gate)
                {
                    if (buffer.Length + args.Data.Length + 1 > spec.MaxOutputChars) { truncated = true; return; }
                    buffer.AppendLine(args.Data);
                }
            };

            using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
            {
                var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                process.OutputDataReceived += onData;
                process.ErrorDataReceived += onData;
                process.Exited += (sender, args) => exited.TrySetResult(true);

                if (!process.Start()) throw new InvalidOperationException("The process could not be started: " + spec.FileName);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    limit.CancelAfter(spec.Timeout);
                    using (limit.Token.Register(() => KillProcessTree(process)))
                    {
                        await exited.Task.ConfigureAwait(false);
                    }
                    bool timedOut = limit.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
                    process.WaitForExit();                       // flush the asynchronous output readers
                    cancellationToken.ThrowIfCancellationRequested();
                    string output;
                    lock (gate) output = buffer.ToString();
                    return new ProcessResult(timedOut ? -1 : process.ExitCode, output, timedOut, truncated);
                }
            }
        }

        private static void KillProcessTree(Process process)
        {
            try
            {
                if (process.HasExited) return;
                if (Path.DirectorySeparatorChar == '\\')
                {
                    // Process.Kill(true) does not exist on .NET Framework / netstandard2.0.
                    using (var killer = Process.Start(new ProcessStartInfo("taskkill", "/PID " + process.Id + " /T /F")
                    { UseShellExecute = false, CreateNoWindow = true }))
                    {
                        killer?.WaitForExit(5000);
                    }
                }
                if (!process.HasExited) process.Kill();
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception || ex is NotSupportedException)
            {
                // Already exited or access denied: nothing more can be done safely.
            }
        }
    }
}

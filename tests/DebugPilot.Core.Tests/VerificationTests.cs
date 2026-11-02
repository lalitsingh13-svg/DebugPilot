using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DebugPilot.Core.Fixes;
using DebugPilot.Core.Verification;
using Xunit;

namespace DebugPilot.Core.Tests
{
    public class UnifiedDiffTests
    {
        [Fact]
        public void Shows_context_and_the_added_line()
        {
            string diff = UnifiedDiff.Create("a\nb\nc\nd\ne\nf\n", "a\nb\nc\nX\nd\ne\nf\n", 2);
            Assert.Contains("+X", diff);
            Assert.Contains(" c", diff);
            Assert.Contains(" d", diff);
            Assert.DoesNotContain(" a\n", diff + "\n");
            Assert.Contains("@@ -2,4 +2,5 @@", diff);
        }

        [Fact]
        public void Shows_replacements_and_handles_identical_text_and_crlf()
        {
            string diff = UnifiedDiff.Create("one\r\ntwo\r\n", "one\r\n2\r\n");
            Assert.Contains("-two", diff);
            Assert.Contains("+2", diff);
            Assert.Equal("(no changes)", UnifiedDiff.Create("same\n", "same\n"));
            Assert.Contains("+x", UnifiedDiff.Create(string.Empty, "x\n"));
        }
    }

    public class BuildOutputParserTests
    {
        [Fact]
        public void Parses_file_diagnostics_with_project_and_dedupes_repeats()
        {
            string line = @"C:\src\App\Program.cs(8,26): error CS0246: The type or namespace name 'StringBuilder' could not be found (are you missing a using directive or an assembly reference?) [C:\src\App\App.csproj]";
            var list = BuildOutputParser.Parse(line + "\r\n" + line + "\r\nBuild FAILED.\r\n");
            Assert.Single(list);
            var d = list[0];
            Assert.Equal("error", d.Severity);
            Assert.Equal("CS0246", d.Code);
            Assert.Equal(@"C:\src\App\Program.cs", d.FilePath);
            Assert.Equal(8, d.Line);
            Assert.Equal(26, d.Column);
            Assert.Equal(@"C:\src\App\App.csproj", d.ProjectPath);
            Assert.Contains("StringBuilder", d.Message);
        }

        [Fact]
        public void Parses_unix_paths_warnings_and_tool_diagnostics()
        {
            var list = BuildOutputParser.Parse("/tmp/x/A.cs(3,1): warning CS8618: Non-nullable property 'P' must contain a non-null value. [/tmp/x/x.csproj]\n" +
                                              "MSBUILD : error MSB1009: Project file does not exist.\n" +
                                              "/tmp/x/x.csproj : error NU1101: Unable to find package Foo. No packages exist with this id in source(s): nuget.org\n");
            Assert.Equal(3, list.Count);
            Assert.Equal("warning", list[0].Severity);
            Assert.Equal("MSB1009", list[1].Code);
            Assert.Equal("NU1101", list[2].Code);
            Assert.Equal("/tmp/x/x.csproj", list[2].FilePath);
        }

        [Fact]
        public void Ignores_summary_lines_empty_input_and_respects_the_cap()
        {
            Assert.Empty(BuildOutputParser.Parse("    0 Warning(s)\n    0 Error(s)\nTime Elapsed 00:00:01\n"));
            Assert.Empty(BuildOutputParser.Parse(null));
            string many = string.Join("\n", Enumerable.Range(1, 50).Select(i => "/a.cs(" + i + ",1): error CS1002: ; expected"));
            Assert.Equal(10, BuildOutputParser.Parse(many, 10).Count);
        }
    }

    public class ProcessBuildVerifierTests
    {
        private const string SbMessage = "The type or namespace name 'StringBuilder' could not be found (are you missing a using directive or an assembly reference?)";
        private static ErrorContext Original(string file) => new ErrorContext(ErrorSource.Compiler, "CS0246", SbMessage, Severity.Error, file, 8, 26);

        private sealed class FakeRunner : IProcessRunner
        {
            private readonly Func<ProcessSpec, Task<ProcessResult>> _run;
            public FakeRunner(Func<ProcessSpec, Task<ProcessResult>> run) { _run = run; }
            public List<ProcessSpec> Specs { get; } = new List<ProcessSpec>();
            public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
            {
                Specs.Add(spec);
                cancellationToken.ThrowIfCancellationRequested();
                return _run(spec);
            }
        }

        private static FakeRunner Returns(int exit, string output, bool timedOut = false) =>
            new FakeRunner(_ => Task.FromResult(new ProcessResult(exit, output, timedOut, false)));

        private static string Setup(TempDir dir, bool sdk = true)
        {
            dir.Write("App.csproj", sdk ? "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>" : "<Project ToolsVersion=\"15.0\"></Project>");
            return dir.Write("Program.cs", "class P { }\n");
        }

        [Fact]
        public async Task Successful_build_passes_and_uses_dotnet_with_safe_arguments()
        {
            using (var dir = new TempDir())
            {
                string file = Setup(dir);
                var runner = Returns(0, "Build succeeded.\n    0 Warning(s)\n    0 Error(s)\n");
                var result = await new ProcessBuildVerifier(runner).VerifyAsync(Original(file), file, CancellationToken.None);
                Assert.Equal(VerificationStatus.Passed, result.Status);
                Assert.True(result.OriginalErrorGone);
                Assert.Equal("dotnet", runner.Specs[0].FileName);
                Assert.Contains("build \"", runner.Specs[0].Arguments);
                Assert.Contains("-nodeReuse:false", runner.Specs[0].Arguments);
                Assert.Contains("UseSharedCompilation=false", runner.Specs[0].Arguments);
                Assert.Equal(dir.Path, runner.Specs[0].WorkingDirectory);
            }
        }

        [Fact]
        public async Task Original_error_still_present_means_failed_even_when_line_numbers_shift()
        {
            using (var dir = new TempDir())
            {
                string file = Setup(dir);
                string output = @"C:\somewhere\Program.cs(9,26): error CS0246: " + SbMessage + " [C:\\somewhere\\App.csproj]\n";
                var result = await new ProcessBuildVerifier(Returns(1, output)).VerifyAsync(Original(file), file, CancellationToken.None);
                Assert.Equal(VerificationStatus.Failed, result.Status);
                Assert.False(result.OriginalErrorGone);
                Assert.Single(result.RemainingErrors);
            }
        }

        [Fact]
        public async Task Other_errors_only_means_partially_fixed()
        {
            using (var dir = new TempDir())
            {
                string file = Setup(dir);
                string output = "/x/Program.cs(20,5): error CS0103: The name 'y' does not exist in the current context\n";
                var result = await new ProcessBuildVerifier(Returns(1, output)).VerifyAsync(Original(file), file, CancellationToken.None);
                Assert.Equal(VerificationStatus.PartiallyFixed, result.Status);
                Assert.True(result.OriginalErrorGone);
                Assert.Contains("other error", result.Summary);
            }
        }

        [Fact]
        public async Task Same_code_in_another_file_does_not_count_as_the_original_error()
        {
            using (var dir = new TempDir())
            {
                string file = Setup(dir);
                string output = "/x/Other.cs(3,3): error CS0246: " + SbMessage + "\n";
                var result = await new ProcessBuildVerifier(Returns(1, output)).VerifyAsync(Original(file), file, CancellationToken.None);
                Assert.Equal(VerificationStatus.PartiallyFixed, result.Status);
            }
        }

        [Fact]
        public async Task Restore_errors_are_inconclusive_not_a_verdict()
        {
            using (var dir = new TempDir())
            {
                string file = Setup(dir);
                var result = await new ProcessBuildVerifier(Returns(1, "/x/App.csproj : error NU1101: Unable to find package Foo.\n")).VerifyAsync(Original(file), file, CancellationToken.None);
                Assert.Equal(VerificationStatus.Inconclusive, result.Status);
                Assert.Contains("NU1101", result.Summary);
            }
        }

        [Fact]
        public async Task Exit_code_without_parsable_errors_is_inconclusive()
        {
            using (var dir = new TempDir())
            {
                string file = Setup(dir);
                var result = await new ProcessBuildVerifier(Returns(1, "something went wrong\n")).VerifyAsync(Original(file), file, CancellationToken.None);
                Assert.Equal(VerificationStatus.Inconclusive, result.Status);
            }
        }

        [Fact]
        public async Task Timeout_and_start_failures_are_inconclusive()
        {
            using (var dir = new TempDir())
            {
                string file = Setup(dir);
                var timeout = await new ProcessBuildVerifier(Returns(-1, string.Empty, timedOut: true), TimeSpan.FromMinutes(2)).VerifyAsync(Original(file), file, CancellationToken.None);
                Assert.Equal(VerificationStatus.Inconclusive, timeout.Status);
                Assert.Contains("2 minute", timeout.Summary);

                var missing = new FakeRunner(_ => throw new Win32Exception("dotnet not found"));
                var failed = await new ProcessBuildVerifier(missing).VerifyAsync(Original(file), file, CancellationToken.None);
                Assert.Equal(VerificationStatus.Inconclusive, failed.Status);
                Assert.Contains("could not be started", failed.Summary);
            }
        }

        [Fact]
        public async Task No_project_is_inconclusive()
        {
            using (var dir = new TempDir())
            {
                string file = dir.Write("Program.cs", "class P { }\n");
                var result = await new ProcessBuildVerifier(Returns(0, string.Empty)).VerifyAsync(Original(file), file, CancellationToken.None);
                Assert.Equal(VerificationStatus.Inconclusive, result.Status);
            }
        }

        [Fact]
        public async Task Classic_projects_use_the_located_msbuild_or_are_inconclusive()
        {
            using (var dir = new TempDir())
            {
                string file = Setup(dir, sdk: false);
                var runner = Returns(0, "Build succeeded.\n");
                var withMsBuild = await new ProcessBuildVerifier(runner, null, (r, ct) => Task.FromResult<string?>("C:\\VS\\MSBuild.exe")).VerifyAsync(Original(file), file, CancellationToken.None);
                Assert.Equal(VerificationStatus.Passed, withMsBuild.Status);
                Assert.Equal("C:\\VS\\MSBuild.exe", runner.Specs[0].FileName);
                Assert.Contains("/restore", runner.Specs[0].Arguments);

                var without = await new ProcessBuildVerifier(Returns(0, string.Empty), null, (r, ct) => Task.FromResult<string?>(null)).VerifyAsync(Original(file), file, CancellationToken.None);
                Assert.Equal(VerificationStatus.Inconclusive, without.Status);
                Assert.Contains("MSBuild was not found", without.Summary);
            }
        }

        [Fact]
        public async Task Cancellation_is_propagated_to_the_caller()
        {
            using (var dir = new TempDir())
            {
                string file = Setup(dir);
                using (var source = new CancellationTokenSource())
                {
                    source.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                        new ProcessBuildVerifier(Returns(0, string.Empty)).VerifyAsync(Original(file), file, source.Token));
                }
            }
        }
    }

    public class SystemProcessRunnerTests
    {
        [Fact]
        public async Task Runs_a_real_process_and_captures_output()
        {
            var result = await new SystemProcessRunner().RunAsync(new ProcessSpec("dotnet", "--version", Environment.CurrentDirectory, TimeSpan.FromMinutes(1)), CancellationToken.None);
            Assert.Equal(0, result.ExitCode);
            Assert.False(result.TimedOut);
            Assert.True(result.Output.Trim().Length > 0);
        }

        [Fact]
        public async Task A_tiny_timeout_stops_the_process_and_reports_it()
        {
            var result = await new SystemProcessRunner().RunAsync(new ProcessSpec("dotnet", "--info", Environment.CurrentDirectory, TimeSpan.FromMilliseconds(1)), CancellationToken.None);
            Assert.True(result.TimedOut);
        }

        [Fact]
        public async Task Cancellation_throws_and_does_not_leave_the_call_hanging()
        {
            using (var source = new CancellationTokenSource())
            {
                source.CancelAfter(TimeSpan.FromMilliseconds(1));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    new SystemProcessRunner().RunAsync(new ProcessSpec("dotnet", "--info", Environment.CurrentDirectory, TimeSpan.FromMinutes(1)), source.Token));
            }
        }
    }
}

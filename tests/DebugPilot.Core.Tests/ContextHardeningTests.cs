using System;
using System.Text;
using DebugPilot.Core;
using Xunit;

namespace DebugPilot.Core.Tests
{
    public class ContextHardeningTests
    {
        private static ErrorContext Error() =>
            new ErrorContext(ErrorSource.Build, "MSB4018", "task failed", Severity.Error, FilePath: @"C:\repo\src\A.cs", Line: 3);

        [Fact]
        public void Secret_straddling_the_byte_budget_never_leaks_a_prefix()
        {
            string token = "gh" + "p_" + "abcdefghijklmnopqrstuvwxyz0123456789";
            string padding = new string('x', 1200);
            var input = new ContextCaptureInput(Error()) { TrimmedBuildOutput = padding + " token " + token };
            var preview = new ContextEngine().Build(input, new ContextCaptureOptions { MaxUtf8Bytes = 1300 });
            Assert.DoesNotContain("ghp_abc", preview.Text);
            Assert.DoesNotContain("abcdefghijklmnop", preview.Text);
        }

        [Fact]
        public void Build_output_keeps_the_tail_where_errors_usually_are()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 400; i++) sb.Append("Compiling file").Append(i).Append(".cs\n");
            sb.Append("FINAL_ERROR_MARKER: error CS1002: ; expected");
            var input = new ContextCaptureInput(Error()) { TrimmedBuildOutput = sb.ToString() };
            var preview = new ContextEngine().Build(input, new ContextCaptureOptions { MaxUtf8Bytes = 4000 });
            Assert.Contains("FINAL_ERROR_MARKER", preview.Text);
            Assert.Contains("middle omitted", preview.Text);
            Assert.True(preview.WasTruncated);
            Assert.True(preview.Utf8ByteCount <= 4000);
        }

        [Fact]
        public void Stack_trace_keeps_both_ends()
        {
            var sb = new StringBuilder("System.NullReferenceException: FIRST_LINE_MARKER\n");
            for (int i = 0; i < 500; i++) sb.Append("   at Some.Namespace.Type").Append(i).Append(".Method()\n");
            sb.Append("   at MyApp.Program.Main() LAST_FRAME_MARKER");
            var input = new ContextCaptureInput(Error()) { StackTrace = sb.ToString() };
            var preview = new ContextEngine().Build(input, new ContextCaptureOptions { MaxUtf8Bytes = 3000 });
            Assert.Contains("FIRST_LINE_MARKER", preview.Text);
            Assert.Contains("LAST_FRAME_MARKER", preview.Text);
        }

        [Fact]
        public void Huge_input_is_bounded_and_redacted()
        {
            string huge = new string('z', 5_000_000) + " password=topsecretvalue";
            var input = new ContextCaptureInput(Error()) { TrimmedBuildOutput = huge };
            var preview = new ContextEngine().Build(input, new ContextCaptureOptions { MaxUtf8Bytes = 2000 });
            Assert.True(preview.Utf8ByteCount <= 2000);
            Assert.DoesNotContain("topsecretvalue", preview.Text);
        }

        [Fact]
        public void Debugger_locals_with_secret_looking_names_are_masked_when_enabled()
        {
            var input = new ContextCaptureInput(Error()) { DebuggerLocals = "count = 5\nuserPassword = hunter2value\nauthToken: abc999\nname = widget" };
            var preview = new ContextEngine().Build(input, new ContextCaptureOptions { IncludeDebuggerLocals = true });
            Assert.Contains("count = 5", preview.Text);
            Assert.Contains("name = widget", preview.Text);
            Assert.DoesNotContain("hunter2value", preview.Text);
            Assert.DoesNotContain("abc999", preview.Text);
        }

        [Fact]
        public void Debugger_locals_are_limited_in_count()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 100; i++) sb.Append("v").Append(i).Append(" = ").Append(i).Append('\n');
            var input = new ContextCaptureInput(Error()) { DebuggerLocals = sb.ToString() };
            var preview = new ContextEngine().Build(input, new ContextCaptureOptions { IncludeDebuggerLocals = true });
            Assert.Contains("v0 = 0", preview.Text);
            Assert.DoesNotContain("v99 = 99", preview.Text);
        }

        [Theory]
        [InlineData(@"C:\repo\server.pem")]
        [InlineData(@"C:\repo\signing.snk")]
        [InlineData(@"C:\repo\id_rsa")]
        [InlineData(@"C:\Users\a\AppData\Roaming\Microsoft\UserSecrets\x\secrets.json")]
        [InlineData(@"C:\Users\a\.aws\credentials")]
        public void More_sensitive_files_are_excluded(string path)
        {
            var input = new ContextCaptureInput(new ErrorContext(ErrorSource.Compiler, "CS0001", "m", Severity.Error, FilePath: path, Line: 1))
            { SourceText = "PRIVATE_SOURCE_SENTINEL" };
            var preview = new ContextEngine().Build(input);
            Assert.DoesNotContain("PRIVATE_SOURCE_SENTINEL", preview.Text);
            Assert.True(preview.SensitiveSourceExcluded);
        }
    }
}

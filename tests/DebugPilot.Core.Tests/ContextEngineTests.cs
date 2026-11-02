using System.Text;
using DebugPilot.Core;
using Xunit;

namespace DebugPilot.Core.Tests
{
    public class ContextEngineTests
    {
        private static ErrorContext Error(string? path = @"C:\repo\src\Widget.cs", int? line = 3) =>
            new ErrorContext(ErrorSource.Compiler, "CS0246", "The type or namespace name 'Widget' could not be found", Severity.Error, FilePath: path, Line: line, ProjectPath: @"C:\repo\Widget.csproj");

        [Fact]
        public void Redact_masks_common_secret_assignments()
        {
            string text = ContextEngine.Redact("api_key=abc123 password: hunter2 client_secret=shh token=xyz \"password\": \"json-secret\"");
            Assert.DoesNotContain("abc123", text);
            Assert.DoesNotContain("hunter2", text);
            Assert.DoesNotContain("shh", text);
            Assert.DoesNotContain("xyz", text);
            Assert.DoesNotContain("json-secret", text);
            Assert.Contains("<REDACTED>", text);
        }

        [Fact]
        public void Redact_masks_bearer_tokens_emails_and_user_paths()
        {
            string text = ContextEngine.Redact("Bearer eyJhbGciOi.testtoken user@example.com C:\\Users\\Alice\\src\\a.cs /home/bob/project");
            Assert.DoesNotContain("eyJhbGciOi.testtoken", text);
            Assert.DoesNotContain("user@example.com", text);
            Assert.DoesNotContain("Alice", text);
            Assert.DoesNotContain("bob", text);
            Assert.Contains("<EMAIL>", text);
            Assert.Contains("<USER_PATH>", text);
        }

        [Fact]
        public void Build_includes_diagnostic_and_only_relevant_source_window()
        {
            var input = new ContextCaptureInput(Error(line: 4))
            {
                SourceText = "one\ntwo\nthree\nFOUR_ERROR\nfive\nsix\nseven",
                SourceStartLine = 1
            };
            var preview = new ContextEngine().Build(input, new ContextCaptureOptions { SourceLinesBefore = 1, SourceLinesAfter = 1 });
            Assert.Contains("CS0246", preview.Text);
            Assert.Contains("three", preview.Text);
            Assert.Contains("FOUR_ERROR", preview.Text);
            Assert.Contains("five", preview.Text);
            Assert.DoesNotContain("two", preview.Text);
            Assert.DoesNotContain("six", preview.Text);
        }

        [Theory]
        [InlineData(@"C:\repo\appsettings.json")]
        [InlineData(@"C:\repo\secrets.json")]
        [InlineData(@"C:\repo\.env")]
        [InlineData(@"C:\repo\cert.pfx")]
        public void Build_excludes_source_from_sensitive_files(string path)
        {
            var input = new ContextCaptureInput(Error(path)) { SourceText = "PRIVATE_SOURCE_SENTINEL", EnclosingSignature = "PRIVATE_SIGNATURE_SENTINEL" };
            var preview = new ContextEngine().Build(input);
            Assert.DoesNotContain("PRIVATE_SOURCE_SENTINEL", preview.Text);
            Assert.DoesNotContain("PRIVATE_SIGNATURE_SENTINEL", preview.Text);
            Assert.True(preview.SensitiveSourceExcluded);
        }

        [Fact]
        public void Build_enforces_utf8_byte_budget()
        {
            var input = new ContextCaptureInput(Error())
            {
                SourceText = new string('x', 2000),
                RelevantProjectSettings = new string('y', 2000),
                TrimmedBuildOutput = new string('z', 2000)
            };
            var preview = new ContextEngine().Build(input, new ContextCaptureOptions { MaxUtf8Bytes = 512 });
            Assert.True(preview.Utf8ByteCount <= 512);
            Assert.Equal(Encoding.UTF8.GetByteCount(preview.Text), preview.Utf8ByteCount);
            Assert.True(preview.WasTruncated);
        }


        [Fact]
        public void Build_enforces_utf8_budget_for_multibyte_text()
        {
            var input = new ContextCaptureInput(Error()) { TestFailure = new string('界', 2000) };
            var preview = new ContextEngine().Build(input, new ContextCaptureOptions { MaxUtf8Bytes = 512 });
            Assert.True(preview.Utf8ByteCount <= 512);
            Assert.Equal(Encoding.UTF8.GetByteCount(preview.Text), preview.Utf8ByteCount);
        }

        [Fact]
        public void Debugger_locals_are_omitted_unless_explicitly_enabled()
        {
            var input = new ContextCaptureInput(Error()) { DebuggerLocals = "customerId=42" };
            Assert.DoesNotContain("customerId", new ContextEngine().Build(input).Text);
            Assert.Contains("customerId", new ContextEngine().Build(input, new ContextCaptureOptions { IncludeDebuggerLocals = true }).Text);
        }

        [Fact]
        public void Build_redacts_secrets_in_diagnostic_and_build_output()
        {
            var error = new ErrorContext(ErrorSource.Build, "MSB0001", "Failed with password=hunter2", Severity.Error);
            var input = new ContextCaptureInput(error) { TrimmedBuildOutput = "api_key=abc123" };
            var preview = new ContextEngine().Build(input);
            Assert.DoesNotContain("hunter2", preview.Text);
            Assert.DoesNotContain("abc123", preview.Text);
        }

        [Fact]
        public void Build_rejects_unreasonably_small_budget()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ContextEngine().Build(new ContextCaptureInput(Error()), new ContextCaptureOptions { MaxUtf8Bytes = 10 }));
        }
    }
}

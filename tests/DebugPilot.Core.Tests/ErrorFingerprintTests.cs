using DebugPilot.Core;
using Xunit;

namespace DebugPilot.Core.Tests
{
    public class ErrorFingerprintTests
    {
        private static ErrorContext Make(string code, string msg, string? project = "A.csproj") =>
            new ErrorContext(ErrorSource.Compiler, code, msg, Severity.Error, ProjectPath: project);

        [Fact]
        public void Same_error_on_different_lines_and_paths_has_same_fingerprint()
        {
            var a = Make("CS0246", @"Type 'Foo' not found in C:\src\x\A.cs line 10");
            var b = Make("cs0246", @"Type 'Foo' not found in D:\other\B.cs line 99");
            Assert.Equal(ErrorFingerprint.Compute(a), ErrorFingerprint.Compute(b));
        }

        [Fact]
        public void Different_code_or_project_changes_fingerprint()
        {
            var a = Make("CS0246", "Type 'Foo' not found");
            Assert.NotEqual(ErrorFingerprint.Compute(a), ErrorFingerprint.Compute(Make("CS0103", "Type 'Foo' not found")));
            Assert.NotEqual(ErrorFingerprint.Compute(a), ErrorFingerprint.Compute(Make("CS0246", "Type 'Foo' not found", "B.csproj")));
        }

        [Fact]
        public void Normalize_masks_guids_numbers_and_whitespace()
        {
            var n = ErrorFingerprint.Normalize("Id 3f2504e0-4f89-11d3-9a0c-0305e82c3301   failed 42 times");
            Assert.Equal("id <guid> failed <n> times", n);
        }
    }
}

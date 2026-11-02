using DebugPilot.Core.Fixes;
using Xunit;

namespace DebugPilot.Core.Tests
{
    public class UsingDirectiveInserterTests
    {
        private static string Fix(string source, string ns)
        {
            var patch = UsingDirectiveInserter.CreatePatch("A.cs", source, ns);
            Assert.NotNull(patch);
            // The patch must be anchored on real text so PatchEngine can detect a stale file.
            Assert.Equal(patch!.ExpectedOriginalText, source.Substring(patch.StartOffset, patch.Length));
            Assert.Equal(PatchEngine.Sha256(patch.ExpectedOriginalText), patch.ExpectedOriginalSha256);
            Assert.True(patch.Length > 0);
            return PatchText.Apply(source, patch);
        }

        [Fact]
        public void No_usings_inserts_above_first_code_line_with_blank_line()
        {
            Assert.Equal("using System.Text;\n\nnamespace A\n{\n}\n", Fix("namespace A\n{\n}\n", "System.Text"));
        }

        [Fact]
        public void Header_comments_and_nullable_directive_stay_above_the_new_using()
        {
            string src = "// Copyright\n/* block\n comment */\n#nullable enable\nnamespace A;\n";
            Assert.Equal("// Copyright\n/* block\n comment */\n#nullable enable\nusing System.Text;\n\nnamespace A;\n", Fix(src, "System.Text"));
        }

        [Fact]
        public void Inserts_after_last_when_new_namespace_sorts_last()
        {
            Assert.Equal("using System;\nusing System.Linq;\nusing System.Text;\n\nnamespace A {}\n",
                Fix("using System;\nusing System.Linq;\n\nnamespace A {}\n", "System.Text"));
        }

        [Fact]
        public void Inserts_in_sorted_position()
        {
            Assert.Equal("using System;\nusing System.Linq;\nusing System.Text;\n", Fix("using System;\nusing System.Text;\n", "System.Linq"));
        }

        [Fact]
        public void System_namespaces_sort_before_others()
        {
            Assert.Equal("using System;\nusing Acme.Widgets;\n", Fix("using System;\n", "Acme.Widgets"));
            Assert.Equal("using System;\nusing Acme.Widgets;\nusing Zeta;\n", Fix("using System;\nusing Zeta;\n", "Acme.Widgets"));
            Assert.Equal("using System;\nusing Acme;\n", Fix("using Acme;\n", "System"));
        }

        [Fact]
        public void Already_imported_returns_null()
        {
            Assert.Null(UsingDirectiveInserter.CreatePatch("A.cs", "using System.Text;\nclass X {}\n", "System.Text"));
            Assert.Null(UsingDirectiveInserter.CreatePatch("A.cs", "global using System.Text;\nclass X {}\n", "System.Text"));
        }

        [Fact]
        public void Alias_and_static_usings_do_not_count_as_imports()
        {
            string src = "using SB = System.Text.StringBuilder;\nclass X {}\n";
            Assert.Equal("using SB = System.Text.StringBuilder;\nusing System.Text;\nclass X {}\n", Fix(src, "System.Text"));
            Assert.Equal("using static System.Math;\nusing System;\nclass X {}\n", Fix("using static System.Math;\nclass X {}\n", "System"));
        }

        [Fact]
        public void Crlf_line_endings_are_preserved()
        {
            Assert.Equal("using System;\r\nusing System.Text;\r\n\r\nnamespace A {}\r\n", Fix("using System;\r\n\r\nnamespace A {}\r\n", "System.Text"));
            Assert.Equal("using System.Text;\r\n\r\nnamespace A {}\r\n", Fix("namespace A {}\r\n", "System.Text"));
        }

        [Fact]
        public void Conditional_compilation_in_the_header_is_not_edited()
        {
            Assert.Null(UsingDirectiveInserter.CreatePatch("A.cs", "#if NET8_0\nusing System;\n#endif\nclass X {}\n", "System.Text"));
        }

        [Fact]
        public void Unusual_or_multiline_usings_are_not_guessed()
        {
            Assert.Null(UsingDirectiveInserter.CreatePatch("A.cs", "using\nSystem;\nclass X {}\n", "System.Text"));
        }

        [Theory]
        [InlineData("Foo;Bar")]
        [InlineData("1Foo")]
        [InlineData("")]
        [InlineData("Foo..Bar")]
        [InlineData("Foo Bar")]
        public void Invalid_namespace_names_are_rejected(string ns)
        {
            Assert.Null(UsingDirectiveInserter.CreatePatch("A.cs", "class X {}\n", ns));
        }

        [Fact]
        public void Empty_file_returns_null()
        {
            Assert.Null(UsingDirectiveInserter.CreatePatch("A.cs", string.Empty, "System.Text"));
            Assert.Null(UsingDirectiveInserter.CreatePatch("A.cs", "// only a comment\n", "System.Text"));
        }

        [Fact]
        public void Usings_inside_a_namespace_block_do_not_confuse_the_header_scan()
        {
            string src = "namespace A\n{\n    using System;\n    class X {}\n}\n";
            Assert.Equal("using System.Text;\n\nnamespace A\n{\n    using System;\n    class X {}\n}\n", Fix(src, "System.Text"));
        }
    }
}

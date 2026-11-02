using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DebugPilot.Core.DiagnosisRules;
using DebugPilot.Core.Fixes;
using Xunit;

namespace DebugPilot.Core.Tests
{
    public class UsingFixProposerTests
    {
        private const string SbMessage = "The type or namespace name 'StringBuilder' could not be found (are you missing a using directive or an assembly reference?)";
        private const string Csproj = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>";

        private static ErrorContext Error(string file, string code, string message) =>
            new ErrorContext(ErrorSource.Compiler, code, message, Severity.Error, file, 5, 9);

        [Fact]
        public async Task Proposes_one_patch_for_a_bcl_type_and_the_patch_applies_through_the_engine()
        {
            using (var dir = new TempDir())
            {
                dir.Write("App.csproj", Csproj);
                string file = dir.Write("Program.cs", "using System;\n\nclass P { static void Main() { var sb = new StringBuilder(); } }\n");
                var diagnosis = await new UsingFixProposer().ProposeAsync(Error(file, "CS0246", SbMessage));

                Assert.NotNull(diagnosis);
                Assert.True(diagnosis!.HasConcreteFix);
                Assert.Single(diagnosis.Fixes);
                Assert.Equal(UsingFixProposer.RuleId, diagnosis.RuleId);
                Assert.False(diagnosis.CanAutoApply);
                Assert.Contains("System.Text", diagnosis.RootCause);

                var engine = new PatchEngine(dir.Path, Path.Combine(dir.Path, ".checkpoints"));
                var applied = await engine.ApplyAsync(diagnosis.Fixes[0], true);
                Assert.True(applied.Applied);
                Assert.Contains("using System.Text;", File.ReadAllText(file));
            }
        }

        [Fact]
        public async Task Finds_a_type_declared_in_a_referenced_project()
        {
            using (var dir = new TempDir())
            {
                dir.Write("Lib/Lib.csproj", Csproj);
                dir.Write("Lib/Widget.cs", "namespace Acme.Widgets;\npublic class Widget { }\n");
                dir.Write("App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"..\\Lib\\Lib.csproj\" /></ItemGroup></Project>");
                string file = dir.Write("App/Program.cs", "class P { Widget w; }\n");
                var d = await new UsingFixProposer().ProposeAsync(Error(file, "CS0246", "The type or namespace name 'Widget' could not be found (are you missing a using directive or an assembly reference?)"));
                Assert.NotNull(d);
                Assert.Single(d!.Fixes);
                Assert.Contains("Acme.Widgets", d.Fixes[0].Replacement);
            }
        }

        [Fact]
        public async Task Several_candidate_namespaces_produce_choices_but_no_patch()
        {
            using (var dir = new TempDir())
            {
                dir.Write("App.csproj", Csproj);
                dir.Write("A.cs", "namespace One;\npublic class Helper { }\n");
                dir.Write("B.cs", "namespace Two;\npublic class Helper { }\n");
                string file = dir.Write("Program.cs", "class P { Helper h; }\n");
                var d = await new UsingFixProposer().ProposeAsync(Error(file, "CS0246", "The type or namespace name 'Helper' could not be found (are you missing a using directive or an assembly reference?)"));
                Assert.NotNull(d);
                Assert.False(d!.HasConcreteFix);
                Assert.Equal(2, d.Suggestions!.Count);
                Assert.Contains("One", d.RootCause);
                Assert.Contains("Two", d.RootCause);
            }
        }

        [Fact]
        public async Task Generic_arity_selects_the_right_namespace()
        {
            using (var dir = new TempDir())
            {
                dir.Write("App.csproj", Csproj);
                string file = dir.Write("Program.cs", "class P { }\n");
                var generic = await new UsingFixProposer().ProposeAsync(Error(file, "CS0246", "The type or namespace name 'IEnumerable<>' could not be found (are you missing a using directive or an assembly reference?)"));
                Assert.Contains("System.Collections.Generic", generic!.Fixes[0].Replacement);
                var plain = await new UsingFixProposer().ProposeAsync(Error(file, "CS0246", "The type or namespace name 'IEnumerable' could not be found (are you missing a using directive or an assembly reference?)"));
                Assert.Contains("using System.Collections;", plain!.Fixes[0].Replacement);
            }
        }

        [Fact]
        public async Task Missing_linq_extension_method_proposes_system_linq()
        {
            using (var dir = new TempDir())
            {
                dir.Write("App.csproj", Csproj);
                string file = dir.Write("Program.cs", "using System.Collections.Generic;\nclass P { }\n");
                var d = await new UsingFixProposer().ProposeAsync(Error(file, "CS1061", "'List<int>' does not contain a definition for 'ToList' and no accessible extension method 'ToList' accepting a first argument of type 'List<int>' could be found (are you missing a using directive or an assembly reference?)"));
                Assert.NotNull(d);
                Assert.Contains("using System.Linq;", d!.Fixes[0].Replacement);
            }
        }

        [Fact]
        public async Task Namespace_already_imported_gives_no_proposal()
        {
            using (var dir = new TempDir())
            {
                dir.Write("App.csproj", Csproj);
                string file = dir.Write("Program.cs", "using System.Text;\nclass P { }\n");
                Assert.Null(await new UsingFixProposer().ProposeAsync(Error(file, "CS0246", SbMessage)));
            }
        }

        [Fact]
        public async Task Unknown_types_and_unsupported_files_give_no_proposal()
        {
            using (var dir = new TempDir())
            {
                dir.Write("App.csproj", Csproj);
                string cs = dir.Write("Program.cs", "class P { }\n");
                string txt = dir.Write("notes.txt", "x");
                Assert.Null(await new UsingFixProposer().ProposeAsync(Error(cs, "CS0246", "The type or namespace name 'Frobnicator' could not be found (are you missing a using directive or an assembly reference?)")));
                Assert.Null(await new UsingFixProposer().ProposeAsync(Error(txt, "CS0246", SbMessage)));
                Assert.Null(await new UsingFixProposer().ProposeAsync(Error(Path.Combine(dir.Path, "missing.cs"), "CS0246", SbMessage)));
                Assert.Null(await new UsingFixProposer().ProposeAsync(Error(cs, "CS8602", "Dereference of a possibly null reference.")));
                Assert.Null(await new UsingFixProposer().ProposeAsync(new ErrorContext(ErrorSource.Runtime, "CS0246", SbMessage, Severity.Error, cs)));
                Assert.Null(await new UsingFixProposer().ProposeAsync(new ErrorContext(ErrorSource.Compiler, "CS0246", SbMessage, Severity.Error)));
            }
        }

        [Fact]
        public async Task Files_under_sensitive_directories_are_never_read_or_patched()
        {
            using (var dir = new TempDir())
            {
                dir.Write("App.csproj", Csproj);
                string ordinary = dir.Write("Program.cs", "class P { }\n");
                string secret = dir.Write("UserSecrets/abc/Program.cs", "class P { }\n");
                Assert.NotNull(await new UsingFixProposer().ProposeAsync(Error(ordinary, "CS0246", SbMessage)));
                Assert.Null(await new UsingFixProposer().ProposeAsync(Error(secret, "CS0246", SbMessage)));
            }
        }

        [Fact]
        public void Project_index_skips_build_output_and_multi_namespace_files()
        {
            using (var dir = new TempDir())
            {
                string csproj = dir.Write("App.csproj", Csproj);
                dir.Write("obj/Generated.cs", "namespace Gen;\npublic class InObj { }\n");
                dir.Write("bin/Debug/Gen.cs", "namespace Gen;\npublic class InBin { }\n");
                dir.Write("Two.cs", "namespace A { public class First { } }\nnamespace B { public class Second { } }\n");
                dir.Write("Good.cs", "namespace Good.Ns\n{\n    public sealed class Found<T, U> { }\n    internal record Rec(int X);\n}\n");
                var index = ProjectTypeIndex.Build(csproj);
                Assert.Empty(index.FindNamespaces("InObj", 0));
                Assert.Empty(index.FindNamespaces("InBin", 0));
                Assert.Empty(index.FindNamespaces("First", 0));
                Assert.Equal(1, index.FilesSkippedMultiNamespace);
                Assert.Equal("Good.Ns", index.FindNamespaces("Found", 2)[0]);
                Assert.Empty(index.FindNamespaces("Found", 1));
                Assert.Equal("Good.Ns", index.FindNamespaces("Rec", 0)[0]);
            }
        }

        [Fact]
        public void Project_index_respects_the_file_limit_and_cancellation()
        {
            using (var dir = new TempDir())
            {
                string csproj = dir.Write("App.csproj", Csproj);
                for (int i = 0; i < 10; i++) dir.Write("F" + i + ".cs", "namespace N;\npublic class C" + i + " { }\n");
                var limited = ProjectTypeIndex.Build(csproj, new IndexLimits { MaxFiles = 3 });
                Assert.True(limited.Truncated);
                Assert.Equal(3, limited.FilesScanned);
                using (var source = new CancellationTokenSource())
                {
                    source.Cancel();
                    Assert.Throws<OperationCanceledException>(() => ProjectTypeIndex.Build(csproj, null, source.Token));
                }
            }
        }

        [Fact]
        public async Task Cancelled_token_stops_the_proposer()
        {
            using (var dir = new TempDir())
            {
                dir.Write("App.csproj", Csproj);
                string file = dir.Write("Program.cs", "class P { }\n");
                using (var source = new CancellationTokenSource())
                {
                    source.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UsingFixProposer().ProposeAsync(Error(file, "CS0246", SbMessage), source.Token));
                }
            }
        }

        [Fact]
        public async Task Default_engine_returns_the_concrete_fix_before_generic_guidance()
        {
            using (var dir = new TempDir())
            {
                dir.Write("App.csproj", Csproj);
                string file = dir.Write("Program.cs", "class P { }\n");
                var d = await new DeterministicDiagnosisEngine().DiagnoseAsync(Error(file, "CS0246", SbMessage));
                Assert.NotNull(d);
                Assert.Equal(UsingFixProposer.RuleId, d!.RuleId);
                Assert.True(d.HasConcreteFix);

                // Without a file the generic guidance rule still answers (and has no patch).
                var guidance = await new DeterministicDiagnosisEngine().DiagnoseAsync(new ErrorContext(ErrorSource.Compiler, "CS0246", SbMessage, Severity.Error));
                Assert.NotNull(guidance);
                Assert.False(guidance!.HasConcreteFix);
            }
        }
    }
}

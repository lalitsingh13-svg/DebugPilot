using DebugPilot.Core.Fixes;
using Xunit;

namespace DebugPilot.Core.Tests
{
    public class MissingSymbolParserTests
    {
        [Fact]
        public void Parses_missing_type()
        {
            var s = MissingSymbolParser.Parse("CS0246", "The type or namespace name 'StringBuilder' could not be found (are you missing a using directive or an assembly reference?)");
            Assert.NotNull(s);
            Assert.Equal("StringBuilder", s!.Name);
            Assert.Equal(0, s.GenericArity);
            Assert.Equal(MissingSymbolKind.TypeOrNamespace, s.Kind);
        }

        [Theory]
        [InlineData("List<>", "List", 1)]
        [InlineData("Dictionary<,>", "Dictionary", 2)]
        [InlineData("Tuple<,,>", "Tuple", 3)]
        public void Parses_generic_arity(string raw, string name, int arity)
        {
            var s = MissingSymbolParser.Parse("CS0246", "The type or namespace name '" + raw + "' could not be found (are you missing a using directive or an assembly reference?)");
            Assert.NotNull(s);
            Assert.Equal(name, s!.Name);
            Assert.Equal(arity, s.GenericArity);
        }

        [Fact]
        public void Parses_missing_name_and_extension_method()
        {
            var n = MissingSymbolParser.Parse("CS0103", "The name 'Console' does not exist in the current context");
            Assert.Equal("Console", n!.Name);
            Assert.Equal(MissingSymbolKind.Name, n.Kind);

            var e = MissingSymbolParser.Parse("cs1061", "'List<int>' does not contain a definition for 'ToList' and no accessible extension method 'ToList' accepting a first argument of type 'List<int>' could be found (are you missing a using directive or an assembly reference?)");
            Assert.Equal("ToList", e!.Name);
            Assert.Equal(MissingSymbolKind.ExtensionMethod, e.Kind);
        }

        [Fact]
        public void Plain_missing_member_without_extension_hint_is_not_parsed()
        {
            Assert.Null(MissingSymbolParser.Parse("CS1061", "'Foo' does not contain a definition for 'Bar' and no accessible extension method 'Baz' accepting a first argument of type 'Foo' could be found"));
            Assert.Null(MissingSymbolParser.Parse("CS1061", "'Foo' does not contain a definition for 'Bar'"));
        }

        [Theory]
        [InlineData("CS0246", "The type or namespace name 'System.Text.Foo' could not be found")]
        [InlineData("CS0246", "The type or namespace name '1abc' could not be found")]
        [InlineData("CS0246", "something unrelated")]
        [InlineData("CS8602", "Dereference of a possibly null reference.")]
        [InlineData("", "The type or namespace name 'Foo' could not be found")]
        public void Rejects_what_a_using_directive_cannot_fix(string code, string message)
        {
            Assert.Null(MissingSymbolParser.Parse(code, message));
        }
    }

    public class KnownNamespacesTests
    {
        [Theory]
        [InlineData("StringBuilder", 0, "System.Text")]
        [InlineData("List", 1, "System.Collections.Generic")]
        [InlineData("Dictionary", 2, "System.Collections.Generic")]
        [InlineData("IEnumerable", 1, "System.Collections.Generic")]
        [InlineData("IEnumerable", 0, "System.Collections")]
        [InlineData("Task", 0, "System.Threading.Tasks")]
        [InlineData("Task", 1, "System.Threading.Tasks")]
        [InlineData("Func", 2, "System")]
        [InlineData("Console", 0, "System")]
        [InlineData("HttpClient", 0, "System.Net.Http")]
        public void Finds_unambiguous_bcl_types(string name, int arity, string ns)
        {
            var found = KnownNamespaces.FindType(name, arity);
            Assert.Single(found);
            Assert.Equal(ns, found[0]);
        }

        [Theory]
        [InlineData("List", 0)]
        [InlineData("StringBuilder", 1)]
        [InlineData("NoSuchType", 0)]
        public void Arity_must_match_and_unknown_names_return_nothing(string name, int arity)
        {
            Assert.Empty(KnownNamespaces.FindType(name, arity));
        }

        [Fact]
        public void Linq_extension_methods_map_to_system_linq()
        {
            Assert.Equal("System.Linq", KnownNamespaces.FindExtensionMethod("ToList")[0]);
            Assert.Empty(KnownNamespaces.FindExtensionMethod("Count"));
        }
    }
}

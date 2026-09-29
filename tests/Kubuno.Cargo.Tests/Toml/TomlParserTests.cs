using Kubuno.Cargo.Toml;
using static Kubuno.Cargo.Tests.Toml.TomlTestHelper;

namespace Kubuno.Cargo.Tests.Toml
{
    public class TomlParserTests
    {
        private static TomlValue V(string tomlValue)
        {
            var v = TomlDocument.Parse("k = " + tomlValue + "\n").GetValue("k");
            Assert.NotNull(v);
            return v!;
        }

        [Fact]
        public void Empty_document_parses()
        {
            var d = TomlDocument.Parse("");
            Assert.Equal("", d.Text);
            Assert.Empty(d.GetKeys());
        }

        [Fact]
        public void Text_and_ToString_equal_the_input()
        {
            var text = "# c\n[a]\nb = 1\n";
            var d = TomlDocument.Parse(text);
            Assert.Equal(text, d.Text);
            Assert.Equal(text, d.ToString());
        }

        [Fact]
        public void Basic_string_escapes()
        {
            Assert.Equal("a\tb\nc\"d\\e\u00e9\U0001F600\b\f\r", V("\"a\\tb\\nc\\\"d\\\\e\\u00e9\\U0001F600\\b\\f\\r\"").AsString());
        }

        [Fact]
        public void Literal_string_has_no_escapes()
        {
            var v = V(@"'C:\path\n'");
            Assert.Equal(@"C:\path\n", v.AsString());
            Assert.Equal(@"'C:\path\n'", v.Raw);
        }

        [Fact]
        public void Multiline_basic_string_trims_first_newline_and_honours_line_ending_backslash()
        {
            var d = TomlDocument.Parse("k = \"\"\"\nRoses are red\\\n    Violets are blue\"\"\"\nn = 1\n");
            Assert.Equal("Roses are redViolets are blue", d.GetValue("k")!.AsString());
            Assert.Equal(1, d.GetValue("n")!.AsInteger());
        }

        [Fact]
        public void Multiline_basic_string_keeps_inner_quotes_and_normalises_crlf()
        {
            var d = TomlDocument.Parse("k = \"\"\"a \"\"quoted\"\" b\r\nc\"\"\"\r\n");
            Assert.Equal("a \"\"quoted\"\" b\nc", d.GetValue("k")!.AsString());
        }

        [Fact]
        public void Multiline_literal_string()
        {
            var d = TomlDocument.Parse("k = '''\nline1\n  \\raw\n'''\n");
            Assert.Equal("line1\n  \\raw\n", d.GetValue("k")!.AsString());
        }

        [Fact]
        public void Multiline_string_may_contain_hash_and_brackets()
        {
            var d = TomlDocument.Parse("k = \"\"\"\n# not a comment\n[not.a.table]\n\"\"\"\nafter = 1\n");
            Assert.Equal("# not a comment\n[not.a.table]\n", d.GetValue("k")!.AsString());
            Assert.Equal(1, d.GetValue("after")!.AsInteger());
            Assert.Equal(new[] { "k", "after" }, d.GetKeys());
        }

        [Theory]
        [InlineData("42", 42L)]
        [InlineData("+7", 7L)]
        [InlineData("-17", -17L)]
        [InlineData("0", 0L)]
        [InlineData("1_000", 1000L)]
        [InlineData("0xDEAD_BEEF", 0xDEADBEEFL)]
        [InlineData("0o755", 493L)]
        [InlineData("0b1101", 13L)]
        [InlineData("9223372036854775807", long.MaxValue)]
        [InlineData("-9223372036854775808", long.MinValue)]
        public void Integers(string source, long expected)
        {
            var v = V(source);
            Assert.Equal(TomlValueKind.Integer, v.Kind);
            Assert.Equal(expected, v.AsInteger());
            Assert.Equal(source, v.Raw);
        }

        [Theory]
        [InlineData("3.14", 3.14)]
        [InlineData("-0.01", -0.01)]
        [InlineData("5e+22", 5e22)]
        [InlineData("1e06", 1e6)]
        [InlineData("6.626e-34", 6.626e-34)]
        [InlineData("224_617.445_991", 224617.445991)]
        [InlineData("inf", double.PositiveInfinity)]
        [InlineData("+inf", double.PositiveInfinity)]
        [InlineData("-inf", double.NegativeInfinity)]
        public void Floats(string source, double expected)
        {
            var v = V(source);
            Assert.Equal(TomlValueKind.Float, v.Kind);
            Assert.Equal(expected, v.AsFloat());
        }

        [Fact]
        public void Nan_is_a_float()
        {
            Assert.True(double.IsNaN(V("nan").AsFloat()!.Value));
            Assert.True(double.IsNaN(V("-nan").AsFloat()!.Value));
        }

        [Fact]
        public void Booleans()
        {
            Assert.Equal(true, V("true").AsBoolean());
            Assert.Equal(false, V("false").AsBoolean());
        }

        [Theory]
        [InlineData("1979-05-27T07:32:00Z")]
        [InlineData("1979-05-27T00:32:00.999999-07:00")]
        [InlineData("1979-05-27 07:32:00")]
        [InlineData("1979-05-27T07:32:00")]
        [InlineData("1979-05-27")]
        [InlineData("07:32:00")]
        [InlineData("00:32:00.5")]
        public void Date_times_keep_their_raw_text(string source)
        {
            var v = V(source);
            Assert.Equal(TomlValueKind.DateTime, v.Kind);
            Assert.Equal(source, v.Raw);
            Assert.Equal(source, v.AsDateTime());
        }

        [Fact]
        public void Date_time_with_space_is_followed_by_next_line_correctly()
        {
            var d = TomlDocument.Parse("a = 1979-05-27 07:32:00Z # when\nb = 2\n");
            Assert.Equal("1979-05-27 07:32:00Z", d.GetValue("a")!.Raw);
            Assert.Equal(2, d.GetValue("b")!.AsInteger());
        }

        [Fact]
        public void Arrays_nested_multiline_comments_and_trailing_comma()
        {
            var d = TomlDocument.Parse("k = [ # first\n  1, 2, # two\n  [3, 4],\n  \"x\", # last\n]\n");
            var items = d.GetValue("k")!.AsArray()!;
            Assert.Equal(4, items.Count);
            Assert.Equal(1, items[0].AsInteger());
            Assert.Equal(TomlValueKind.Array, items[2].Kind);
            Assert.Equal(4, items[2].AsArray()![1].AsInteger());
            Assert.Equal("x", items[3].AsString());
        }

        [Fact]
        public void Empty_array_and_empty_inline_table()
        {
            Assert.Empty(V("[]").AsArray()!);
            Assert.Empty(V("{}").AsTable()!);
            Assert.Empty(V("[ ]").AsArray()!);
            Assert.Empty(V("{ }").AsTable()!);
        }

        [Fact]
        public void Inline_table_with_nested_values_and_dotted_key()
        {
            var v = V("{ a = 1, b.c = \"x\", d = { e = [1, 2] } }");
            var t = v.AsTable()!;
            Assert.Equal(new[] { "a", "b", "d" }, t.Select(e => e.Key).ToArray());
            Assert.Equal("x", v.AsTable()![1].Value.AsTable()![0].Value.AsString());
            Assert.Equal("{ a = 1, b.c = \"x\", d = { e = [1, 2] } }", v.Raw);
        }

        [Fact]
        public void Raw_of_array_is_the_exact_source()
        {
            var d = TomlDocument.Parse("k = [ 1,2 ,3 ] # c\n");
            Assert.Equal("[ 1,2 ,3 ]", d.GetValue("k")!.Raw);
        }

        [Fact]
        public void Comments_are_accepted_everywhere()
        {
            var text = "# top\n[a] # header\n# inside\nk = 1 # trailing\n\n   # indented\n[b.c]#glued\nx = [ # in array\n 1 ] #end";
            var d = TomlDocument.Parse(text);
            Assert.Equal(1, d.GetValue("a", "k")!.AsInteger());
            Assert.Equal(1, d.GetValue("b", "c", "x")!.AsArray()![0].AsInteger());
            Assert.Equal(text, d.Text);
        }

        [Fact]
        public void Quoted_and_dotted_keys_with_whitespace()
        {
            var d = TomlDocument.Parse("a . b = 1\n\"q k\" .'l k'. z = 2\n\"\" = 3\n");
            Assert.Equal(1, d.GetValue("a", "b")!.AsInteger());
            Assert.Equal(2, d.GetValue("q k", "l k", "z")!.AsInteger());
            Assert.Equal(3, d.GetValue("")!.AsInteger());
        }

        [Fact]
        public void Quoted_key_supports_escapes()
        {
            var d = TomlDocument.Parse("\"a\\tb\" = 1\n");
            Assert.Equal(new[] { "a\tb" }, d.GetKeys());
        }

        [Fact]
        public void Crlf_document_parses()
        {
            var text = "[a]\r\nk = 1\r\n# c\r\nl = [\r\n  1,\r\n  2,\r\n]\r\n";
            var d = TomlDocument.Parse(text);
            Assert.Equal(1, d.GetValue("a", "k")!.AsInteger());
            Assert.Equal(2, d.GetValue("a", "l")!.AsArray()!.Count);
            Assert.Equal(text, d.Text);
        }

        [Fact]
        public void Document_without_trailing_newline_parses()
        {
            var d = TomlDocument.Parse("a = 1");
            Assert.Equal(1, d.GetValue("a")!.AsInteger());
        }

        [Fact]
        public void Bom_is_accepted_and_kept()
        {
            var text = "\uFEFF[package]\nname = \"x\"\n";
            var d = TomlDocument.Parse(text);
            Assert.Equal("x", d.GetValue("package", "name")!.AsString());
            Assert.Equal(text, d.Text);
        }

        [Fact]
        public void Bom_shifts_no_error_column()
        {
            var ex = Assert.Throws<TomlParseException>(() => TomlDocument.Parse("\uFEFFa = "));
            Assert.Equal(1, ex.Line);
            Assert.Equal(5, ex.Column);
        }

        [Theory]
        [InlineData("a = ", 1, 5)]
        [InlineData("a = 1\nb = ", 2, 5)]
        [InlineData("a = \"abc", 1, 5)]
        [InlineData("[a\nb = 1", 1, 3)]
        [InlineData("a = 1 2", 1, 7)]
        [InlineData("a = 1\na = 2", 2, 1)]
        [InlineData("[t]\nx = 1\n[t]", 3, 1)]
        [InlineData("a = 0123", 1, 5)]
        [InlineData("a = [1, 2", 1, 5)]
        [InlineData("a = \"\\q\"", 1, 6)]
        [InlineData("= 1", 1, 1)]
        [InlineData("a = {b = 1,", 1, 5)]
        [InlineData("a = \"\\ud800\"", 1, 6)]
        [InlineData("a = 9223372036854775808", 1, 5)]
        [InlineData("a = 0xFFFFFFFFFFFFFFFF", 1, 5)]
        [InlineData("a = tru", 1, 5)]
        [InlineData("a = 1__0", 1, 5)]
        [InlineData("a = \"a\nb\"", 1, 5)]
        [InlineData("# ok\n\n[a]\nb = @", 4, 5)]
        [InlineData("[[a]", 1, 5)]
        [InlineData("a.b = 1\na.b = 2", 2, 1)]
        [InlineData("a = 'x", 1, 5)]
        [InlineData("a = \"\"\"never closed", 1, 5)]
        [InlineData("a = [1 2]", 1, 8)]
        [InlineData("a = { b = 1 c = 2 }", 1, 13)]
        public void Invalid_documents_report_line_and_column(string text, int line, int column)
        {
            var ex = Assert.Throws<TomlParseException>(() => TomlDocument.Parse(text));
            Assert.Equal(line, ex.Line);
            Assert.Equal(column, ex.Column);
            Assert.IsAssignableFrom<FormatException>(ex);
            Assert.Contains("line " + line, ex.Message);
        }

        [Fact]
        public void Bare_carriage_return_is_rejected()
        {
            Assert.Throws<TomlParseException>(() => TomlDocument.Parse("a = 1\rb = 2\n"));
        }

        [Fact]
        public void Control_character_in_string_is_rejected()
        {
            Assert.Throws<TomlParseException>(() => TomlDocument.Parse("a = \"x\u0001y\"\n"));
        }

        [Fact]
        public void Deep_nesting_is_rejected_instead_of_overflowing()
        {
            var text = "a = " + new string('[', 500) + new string(']', 500) + "\n";
            Assert.Throws<TomlParseException>(() => TomlDocument.Parse(text));
        }

        [Fact]
        public void Null_text_throws()
        {
            Assert.Throws<ArgumentNullException>(() => TomlDocument.Parse(null!));
        }

        [Fact]
        public void Array_of_tables_and_sub_tables_parse()
        {
            var text = F("""
                [[bin]]
                name = "a"
                [bin.meta]
                x = 1
                [[bin]]
                name = "b"
                """);
            var d = TomlDocument.Parse(text);
            Assert.Equal(2, d.GetArrayOfTables("bin").Count);
        }
    }
}

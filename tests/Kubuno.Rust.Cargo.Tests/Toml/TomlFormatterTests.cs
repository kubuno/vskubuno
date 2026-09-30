using Kubuno.Rust.Cargo.Toml;
using static Kubuno.Rust.Cargo.Tests.Toml.TomlTestHelper;

namespace Kubuno.Rust.Cargo.Tests.Toml
{
    public class TomlFormatterTests
    {
        [Theory]
        [InlineData("name", "name")]
        [InlineData("opt-level", "opt-level")]
        [InlineData("a_b1", "a_b1")]
        [InlineData("cfg(windows)", "\"cfg(windows)\"")]
        [InlineData("with space", "\"with space\"")]
        [InlineData("a.b", "\"a.b\"")]
        [InlineData("", "\"\"")]
        [InlineData("q\"uote", "\"q\\\"uote\"")]
        [InlineData("café", "\"café\"")]
        public void Keys_are_bare_only_when_allowed(string key, string expected)
        {
            Assert.Equal(expected, TomlKey.Format(key));
        }

        [Fact]
        public void Format_scalars()
        {
            Assert.Equal("\"a\"", TomlValueFormatter.Format(S("a")));
            Assert.Equal("1", TomlValueFormatter.Format(I(1)));
            Assert.Equal("true", TomlValueFormatter.Format(B(true)));
            Assert.Equal("false", TomlValueFormatter.Format(B(false)));
            Assert.Equal("1.5", TomlValueFormatter.Format(TomlValue.Float(1.5)));
            Assert.Equal("2.0", TomlValueFormatter.Format(TomlValue.Float(2)));
        }

        [Fact]
        public void Format_string_escapes_control_characters()
        {
            Assert.Equal("\"\\b\\t\\n\\f\\r\\\"\\\\\\u0000\\u007F\"", TomlValueFormatter.Format(S("\b\t\n\f\r\"\\\0\u007f")));
        }

        [Fact]
        public void Format_arrays_and_tables_on_one_line()
        {
            Assert.Equal("[]", TomlValueFormatter.Format(Arr()));
            Assert.Equal("[\"a\", \"b\"]", TomlValueFormatter.Format(Arr(S("a"), S("b"))));
            Assert.Equal("{}", TomlValueFormatter.Format(Tbl()));
            Assert.Equal("{ level = \"warn\", priority = -1 }", TomlValueFormatter.Format(Tbl(("level", S("warn")), ("priority", I(-1)))));
            Assert.Equal("{ a = { \"b c\" = [1] } }", TomlValueFormatter.Format(Tbl(("a", Tbl(("b c", Arr(I(1))))))));
        }

        [Fact]
        public void Format_date_time_uses_the_raw_text()
        {
            var v = TomlDocument.Parse("d = 1979-05-27T07:32:00Z\n").GetValue("d")!;
            Assert.Equal("1979-05-27T07:32:00Z", TomlValueFormatter.Format(v));
        }

        [Fact]
        public void Formatted_values_parse_back_to_equal_values()
        {
            var values = new[]
            {
                S("plain"), S("with \"quotes\" and \\ and \n newline"), I(-5), TomlValue.Float(0.25), B(true),
                Arr(I(1), Arr(S("n")), Tbl(("k", B(false)))), Tbl(("a", I(1)), ("b c", Arr()), ("d", Tbl())),
            };
            foreach (var v in values)
            {
                var back = TomlDocument.Parse("k = " + TomlValueFormatter.Format(v) + "\n").GetValue("k")!;
                Assert.Equal(v, back);
            }
        }

        [Fact]
        public void Null_arguments_throw()
        {
            Assert.Throws<ArgumentNullException>(() => TomlKey.Format(null!));
            Assert.Throws<ArgumentNullException>(() => TomlValueFormatter.Format(null!));
        }

        [Fact]
        public void TomlEdit_apply_replaces_the_range()
        {
            var e = new TomlEdit(2, 3, "XY");
            Assert.Equal("abXYfg", e.Apply("abcdefg"));
        }
    }
}

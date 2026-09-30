using Kubuno.Cargo.Toml;
using static Kubuno.Cargo.Tests.Toml.TomlTestHelper;

namespace Kubuno.Cargo.Tests.Toml
{
    /// <summary>Every test asserts the FULL resulting text, proving that only the intended lines change.</summary>
    public class TomlSetValueTests
    {
        // ------------------------------------------------------------ rule 1: existing key

        [Fact]
        public void Replaces_only_the_value_keeping_alignment_and_trailing_comment()
        {
            var text = F("""
                [package]
                name    = "old"   # the name
                version = "0.1.0"
                """);
            Assert.Equal(F("""
                [package]
                name    = "new"   # the name
                version = "0.1.0"
                """), Set(text, S("new"), "package", "name"));
        }

        [Fact]
        public void Replaced_value_edit_is_minimal()
        {
            var text = "[package]\nversion = \"0.1.0\"\n";
            var doc = TomlDocument.Parse(text);
            var edit = doc.SetValue(S("0.2.0"), "package", "version")!.Value;
            Assert.Equal(text.IndexOf("1.0", StringComparison.Ordinal), edit.Start);
            Assert.Equal(1, edit.OldLength);
            Assert.Equal("2", edit.NewText);
        }

        [Fact]
        public void Keeps_literal_string_quoting()
        {
            var text = F("""
                [package]
                name = 'old' # c
                """);
            Assert.Equal(F("""
                [package]
                name = 'new' # c
                """), Set(text, S("new"), "package", "name"));
        }

        [Fact]
        public void Literal_string_falls_back_to_basic_when_value_has_a_quote()
        {
            var text = "a = 'old'\n";
            Assert.Equal("a = \"it's\"\n", Set(text, S("it's"), "a"));
        }

        [Fact]
        public void Literal_string_falls_back_to_basic_for_control_characters()
        {
            var text = "a = 'old'\n";
            Assert.Equal("a = \"x\\ny\"\n", Set(text, S("x\ny"), "a"));
        }

        [Fact]
        public void Multiline_string_is_replaced_by_a_basic_string()
        {
            var text = "a = '''\nold\n''' # c\nb = 1\n";
            Assert.Equal("a = \"new\" # c\nb = 1\n", Set(text, S("new"), "a"));
        }

        [Fact]
        public void Replaces_integer_boolean_and_float()
        {
            var text = F("""
                [profile.release]
                opt-level = 2 # speed
                debug = false
                scale = 0.5
                """);
            text = Set(text, I(3), "profile", "release", "opt-level");
            text = Set(text, B(true), "profile", "release", "debug");
            text = Set(text, TomlValue.Float(1), "profile", "release", "scale");
            Assert.Equal(F("""
                [profile.release]
                opt-level = 3 # speed
                debug = true
                scale = 1.0
                """), text);
        }

        [Fact]
        public void Replaces_a_value_of_a_dotted_key_leaf()
        {
            var text = F("""
                [package]
                edition.workspace = true # inherited
                """);
            Assert.Equal(F("""
                [package]
                edition.workspace = false # inherited
                """), Set(text, B(false), "package", "edition", "workspace"));
        }

        [Fact]
        public void Replaces_a_root_dotted_key()
        {
            var text = "profile.release.lto = true\nprofile.release.opt-level = 3\n";
            Assert.Equal("profile.release.lto = \"fat\"\nprofile.release.opt-level = 3\n", Set(text, S("fat"), "profile", "release", "lto"));
        }

        [Fact]
        public void Replaces_a_key_of_a_subtable_header()
        {
            var text = F("""
                [dependencies.serde]
                version = "1"
                features = ["derive"]
                """);
            Assert.Equal(F("""
                [dependencies.serde]
                version = "2"
                features = ["derive"]
                """), Set(text, S("2"), "dependencies", "serde", "version"));
        }

        [Fact]
        public void Replaces_a_whole_array_in_place()
        {
            var text = "[features]\ndefault = [\"a\",\"b\"] # d\n";
            Assert.Equal("[features]\ndefault = [\"a\", \"c\"] # d\n", Set(text, Arr(S("a"), S("c")), "features", "default"));
        }

        [Fact]
        public void Equal_value_is_a_no_op()
        {
            var text = "a = 'x'\nb = 1\nt = { p = 1, q = [1, 2] }\nf = 1.5\nl = [\"a\"]\n";
            var doc = TomlDocument.Parse(text);
            Assert.Null(doc.SetValue(S("x"), "a"));
            Assert.Null(doc.SetValue(I(1), "b"));
            Assert.Null(doc.SetValue(Tbl(("q", Arr(I(1), I(2))), ("p", I(1))), "t"));
            Assert.Null(doc.SetValue(TomlValue.Float(1.5), "f"));
            Assert.Null(doc.SetValue(Arr(S("a")), "l"));
            Assert.Null(doc.SetValue(I(1), "t", "p"));
            Assert.Equal(text, doc.Text);
        }

        [Fact]
        public void Different_kind_is_not_equal()
        {
            var text = "a = 1\n";
            Assert.Equal("a = 1.0\n", Set(text, TomlValue.Float(1), "a"));
        }

        // ------------------------------------------------------------ rule 2: inline tables

        [Fact]
        public void Replaces_a_value_inside_an_inline_table()
        {
            var text = F("""
                [dependencies]
                serde = { version = "1", features = ["derive"] } # keep
                """);
            Assert.Equal(F("""
                [dependencies]
                serde = { version = "2", features = ["derive"] } # keep
                """), Set(text, S("2"), "dependencies", "serde", "version"));
        }

        [Fact]
        public void Replaces_an_array_inside_an_inline_table()
        {
            var text = "serde = { version = \"1\", features = [\"derive\"] }\n";
            Assert.Equal("serde = { version = \"1\", features = [\"derive\", \"rc\"] }\n", Set(text, Arr(S("derive"), S("rc")), "serde", "features"));
        }

        [Fact]
        public void Inserts_a_missing_key_in_an_inline_table()
        {
            var text = "[dependencies]\nserde = { version = \"1\" }\n";
            Assert.Equal("[dependencies]\nserde = { version = \"1\", optional = true }\n", Set(text, B(true), "dependencies", "serde", "optional"));
        }

        [Fact]
        public void Inserts_in_a_compact_inline_table_keeping_the_compact_style()
        {
            Assert.Equal("t = {a=1, b=2}\n", Set("t = {a=1}\n", I(2), "t", "b"));
        }

        [Fact]
        public void Inserts_in_an_empty_inline_table()
        {
            Assert.Equal("t = { b = 2 }\n", Set("t = {}\n", I(2), "t", "b"));
            Assert.Equal("t = { b = 2 }\n", Set("t = { }\n", I(2), "t", "b"));
        }

        [Fact]
        public void Inserts_in_a_nested_inline_table()
        {
            Assert.Equal("a = { b = { c = 1, d = 2 } }\n", Set("a = { b = { c = 1 } }\n", I(2), "a", "b", "d"));
        }

        [Fact]
        public void Inserts_before_an_existing_trailing_comma()
        {
            Assert.Equal("t = { a = 1, b = 2, }\n", Set("t = { a = 1, }\n", I(2), "t", "b"));
        }

        [Fact]
        public void Inserts_a_deep_missing_path_in_an_inline_table_as_dotted_key()
        {
            Assert.Equal("t = { a = 1, x.y = 2 }\n", Set("t = { a = 1 }\n", I(2), "t", "x", "y"));
        }

        [Fact]
        public void Inserts_into_inline_table_defined_by_root_dotted_key_prefix()
        {
            var text = "profile = { release = { lto = true } }\n";
            Assert.Equal("profile = { release = { lto = true, opt-level = 3 } }\n", Set(text, I(3), "profile", "release", "opt-level"));
        }

        [Fact]
        public void Inline_table_in_a_dotted_key_value_is_editable()
        {
            var text = "[dependencies]\nserde.version = \"1\"\nkubuno = { path = \"..\" }\n";
            Assert.Equal("[dependencies]\nserde.version = \"2\"\nkubuno = { path = \"..\" }\n", Set(text, S("2"), "dependencies", "serde", "version"));
        }

        // ------------------------------------------------------------ rule 3: subtree replaced

        [Fact]
        public void Dotted_subtree_is_replaced_keeping_indentation_and_comment()
        {
            var text = F("""
                [package]
                name = "x"
                  edition.workspace = true # inherited
                version = "1"
                """);
            Assert.Equal(F("""
                [package]
                name = "x"
                  edition = "2021" # inherited
                version = "1"
                """), Set(text, S("2021"), "package", "edition"));
        }

        [Fact]
        public void Dotted_subtree_with_interleaved_lines_only_touches_affected_lines()
        {
            var text = F("""
                [package]
                edition.workspace = true # c
                name = "x"
                edition.other = 1
                version = "1"
                """);
            Assert.Equal(F("""
                [package]
                edition = "2021" # c
                name = "x"
                version = "1"
                """), Set(text, S("2021"), "package", "edition"));
        }

        [Fact]
        public void Root_dotted_subtree_is_replaced()
        {
            var text = "a.b = 1\na.c = 2\nz = 3\n";
            Assert.Equal("a = 9\nz = 3\n", Set(text, I(9), "a"));
        }

        [Fact]
        public void Header_table_is_replaced_by_a_key_in_the_parent_table()
        {
            var text = F("""
                [dependencies]
                anyhow = "1"

                [dependencies.serde]
                version = "1"
                features = ["derive"]

                [features]
                default = []
                """);
            Assert.Equal(F("""
                [dependencies]
                anyhow = "1"
                serde = "1"

                [features]
                default = []
                """), Set(text, S("1"), "dependencies", "serde"));
        }

        [Fact]
        public void Inline_table_is_replaced_by_a_scalar()
        {
            var text = "[dependencies]\nserde = { version = \"1\" } # c\n";
            Assert.Equal("[dependencies]\nserde = \"1\" # c\n", Set(text, S("1"), "dependencies", "serde"));
        }

        [Fact]
        public void Scalar_is_replaced_by_an_inline_table()
        {
            var text = "[dependencies]\nserde = \"1\"\n";
            var v = Tbl(("version", S("1")), ("features", Arr(S("derive"))));
            Assert.Equal("[dependencies]\nserde = { version = \"1\", features = [\"derive\"] }\n", Set(text, v, "dependencies", "serde"));
        }

        // ------------------------------------------------------------ rule 4: missing key in a header table

        [Fact]
        public void Inserts_after_the_last_key_before_trailing_blank_lines_and_comments()
        {
            var text = F("""
                [package]
                name = "x"

                # deps
                [dependencies]
                """);
            Assert.Equal(F("""
                [package]
                name = "x"
                edition = "2021"

                # deps
                [dependencies]
                """), Set(text, S("2021"), "package", "edition"));
        }

        [Fact]
        public void Inserts_right_after_the_header_of_an_empty_table()
        {
            var text = "[package]\n\n[dependencies]\n";
            Assert.Equal("[package]\nedition = \"2021\"\n\n[dependencies]\n", Set(text, S("2021"), "package", "edition"));
        }

        [Fact]
        public void Inserts_with_the_previous_entry_indentation()
        {
            var text = "[t]\n    a = 1\n";
            Assert.Equal("[t]\n    a = 1\n    b = 2\n", Set(text, I(2), "t", "b"));
        }

        [Fact]
        public void Inserts_after_a_multiline_last_value()
        {
            var text = F("""
                [package]
                keywords = [
                    "a",
                ]

                [lib]
                """);
            Assert.Equal(F("""
                [package]
                keywords = [
                    "a",
                ]
                edition = "2021"

                [lib]
                """), Set(text, S("2021"), "package", "edition"));
        }

        [Fact]
        public void Inserts_in_a_table_whose_last_line_has_no_newline()
        {
            Assert.Equal("[a]\nx = 1\ny = 2", Set("[a]\nx = 1", I(2), "a", "y"));
        }

        [Fact]
        public void Inserts_in_a_header_only_table_without_newline()
        {
            Assert.Equal("[a]\nx = 2", Set("[a]", I(2), "a", "x"));
        }

        [Fact]
        public void Inserts_in_features_table()
        {
            var text = "[features]\ndefault = [\"a\"]\na = []\n";
            Assert.Equal("[features]\ndefault = [\"a\"]\na = []\nextra = [\"dep:x\", \"a\"]\n", Set(text, Arr(S("dep:x"), S("a")), "features", "extra"));
        }

        [Fact]
        public void Quotes_keys_that_need_it()
        {
            Assert.Equal("[t]\na = 1\n\"needs quoting\" = 2\n", Set("[t]\na = 1\n", I(2), "t", "needs quoting"));
        }

        [Fact]
        public void Lint_tables_accept_inline_table_values()
        {
            var text = "[lints.clippy]\nunwrap_used = \"deny\"\n";
            var v = Tbl(("level", S("warn")), ("priority", I(-1)));
            Assert.Equal("[lints.clippy]\nunwrap_used = \"deny\"\npedantic = { level = \"warn\", priority = -1 }\n", Set(text, v, "lints", "clippy", "pedantic"));
        }

        // ------------------------------------------------------------ rule 5: missing root key

        [Fact]
        public void Root_key_goes_after_the_last_root_key()
        {
            var text = "a = 1\nb = 2\n\n[t]\nx = 1\n";
            Assert.Equal("a = 1\nb = 2\nc = 3\n\n[t]\nx = 1\n", Set(text, I(3), "c"));
        }

        [Fact]
        public void Root_key_goes_before_the_first_header_with_a_blank_line()
        {
            var text = "[package]\nname = \"x\"\n";
            Assert.Equal("top = 1\n\n[package]\nname = \"x\"\n", Set(text, I(1), "top"));
        }

        [Fact]
        public void Root_key_after_leading_comments_before_first_header()
        {
            var text = "# license\n\n[package]\nname = \"x\"\n";
            Assert.Equal("# license\n\ntop = 1\n\n[package]\nname = \"x\"\n", Set(text, I(1), "top"));
        }

        [Fact]
        public void Root_key_in_an_empty_document()
        {
            Assert.Equal("a = 1\n", Set("", I(1), "a"));
        }

        [Fact]
        public void Root_key_in_a_comment_only_document()
        {
            Assert.Equal("# hi\na = 1\n", Set("# hi\n", I(1), "a"));
            Assert.Equal("# hi\na = 1", Set("# hi", I(1), "a"));
        }

        // ------------------------------------------------------------ rule 6: missing table

        [Fact]
        public void Missing_table_is_appended_at_the_end_after_one_blank_line()
        {
            var text = "[package]\nname = \"x\"\n";
            Assert.Equal("[package]\nname = \"x\"\n\n[profile.release]\nlto = true\n", Set(text, B(true), "profile", "release", "lto"));
        }

        [Fact]
        public void Missing_table_is_placed_after_the_sibling_with_the_longest_common_prefix()
        {
            var text = F("""
                [profile.release]
                lto = true

                [dependencies]
                a = "1"
                """);
            Assert.Equal(F("""
                [profile.release]
                lto = true

                [profile.dev]
                opt-level = 1

                [dependencies]
                a = "1"
                """), Set(text, I(1), "profile", "dev", "opt-level"));
        }

        [Fact]
        public void Missing_table_before_a_directly_following_header_gets_a_separating_blank_line()
        {
            var text = "[profile.release]\nlto = true\n[dependencies]\na = \"1\"\n";
            Assert.Equal("[profile.release]\nlto = true\n\n[profile.dev]\nopt-level = 1\n\n[dependencies]\na = \"1\"\n", Set(text, I(1), "profile", "dev", "opt-level"));
        }

        [Fact]
        public void Missing_table_goes_after_the_last_of_several_prefix_sharing_blocks()
        {
            var text = F("""
                [profile.release]
                lto = true

                [profile.release.package.foo]
                opt-level = 2

                [dependencies]
                """);
            Assert.Equal(F("""
                [profile.release]
                lto = true

                [profile.release.package.foo]
                opt-level = 2

                [profile.dev]
                opt-level = 1

                [dependencies]
                """), Set(text, I(1), "profile", "dev", "opt-level"));
        }

        [Fact]
        public void Missing_table_at_end_of_a_file_without_final_newline_adds_none()
        {
            Assert.Equal("[package]\nname = \"x\"\n\n[profile.release]\nlto = true", Set("[package]\nname = \"x\"", B(true), "profile", "release", "lto"));
        }

        [Fact]
        public void Missing_table_at_end_when_a_blank_line_already_ends_the_file()
        {
            Assert.Equal("[package]\nname = \"x\"\n\n[profile.release]\nlto = true\n", Set("[package]\nname = \"x\"\n\n", B(true), "profile", "release", "lto"));
        }

        [Fact]
        public void Missing_table_in_an_empty_document()
        {
            Assert.Equal("[dependencies]\nserde = \"1\"\n", Set("", S("1"), "dependencies", "serde"));
        }

        [Fact]
        public void Missing_table_next_to_an_ancestor_table()
        {
            var text = F("""
                [dependencies]
                anyhow = "1"

                [features]
                default = []
                """);
            // The parent [dependencies.serde] does not exist: a header block is created right after [dependencies].
            Assert.Equal(F("""
                [dependencies]
                anyhow = "1"

                [dependencies.serde]
                version = "1"

                [features]
                default = []
                """), Set(text, S("1"), "dependencies", "serde", "version"));
        }

        [Fact]
        public void Missing_table_with_quoted_segments()
        {
            var text = F("""
                [target.'cfg(windows)'.dependencies]
                winapi = "0.3"
                """);
            Assert.Equal(F("""
                [target.'cfg(windows)'.dependencies]
                winapi = "0.3"

                [target."cfg(unix)".dependencies]
                libc = "0.2"
                """), Set(text, S("0.2"), "target", "cfg(unix)", "dependencies", "libc"));
        }

        [Fact]
        public void Existing_quoted_header_is_found_and_appended_to()
        {
            var text = F("""
                [target.'cfg(windows)'.dependencies]
                winapi = "0.3"

                [features]
                """);
            Assert.Equal(F("""
                [target.'cfg(windows)'.dependencies]
                winapi = "0.3"
                windows = "0.62"

                [features]
                """), Set(text, S("0.62"), "target", "cfg(windows)", "dependencies", "windows"));
        }

        [Fact]
        public void Parent_defined_by_root_dotted_keys_gets_a_sibling_dotted_key()
        {
            var text = "name = \"x\"\nprofile.release.lto = true\n\n[t]\n";
            Assert.Equal("name = \"x\"\nprofile.release.lto = true\nprofile.release.opt-level = 3\n\n[t]\n", Set(text, I(3), "profile", "release", "opt-level"));
        }

        [Fact]
        public void Parent_defined_by_dotted_keys_under_a_header_gets_a_relative_sibling()
        {
            var text = F("""
                [package]
                authors.workspace = true
                version.workspace = true
                """);
            Assert.Equal(F("""
                [package]
                authors.workspace = true
                authors.other = 1
                version.workspace = true
                """), Set(text, I(1), "package", "authors", "other"));
        }

        // ------------------------------------------------------------ rules 7-10: misc

        [Fact]
        public void Array_of_tables_is_read_only()
        {
            var doc = TomlDocument.Parse("[[bin]]\nname = \"a\"\n");
            Assert.Throws<NotSupportedException>(() => doc.SetValue(S("b"), "bin", "name"));
            Assert.Throws<NotSupportedException>(() => doc.SetValue(S("b"), "bin"));
            Assert.Equal("[[bin]]\nname = \"a\"\n", doc.Text);
        }

        [Fact]
        public void Setting_below_a_scalar_is_rejected()
        {
            var doc = TomlDocument.Parse("[dependencies]\nserde = \"1\"\n");
            Assert.Throws<InvalidOperationException>(() => doc.SetValue(S("x"), "dependencies", "serde", "features"));
            Assert.Equal("[dependencies]\nserde = \"1\"\n", doc.Text);
        }

        [Fact]
        public void Invalid_arguments_are_rejected()
        {
            var doc = TomlDocument.Parse("");
            Assert.Throws<ArgumentException>(() => doc.SetValue(S("x")));
            Assert.Throws<ArgumentNullException>(() => doc.SetValue(null!, "a"));
            Assert.Throws<ArgumentException>(() => doc.Remove());
        }

        [Fact]
        public void Newline_style_of_the_document_is_used_for_inserted_lines()
        {
            var text = Crlf("[package]\nname = \"x\"\n\n[dependencies]\na = \"1\"\n");
            var s1 = Set(text, S("2021"), "package", "edition");
            Assert.Equal(Crlf("[package]\nname = \"x\"\nedition = \"2021\"\n\n[dependencies]\na = \"1\"\n"), s1);
            var s2 = Set(s1, B(true), "profile", "release", "lto");
            Assert.Equal(Crlf("[package]\nname = \"x\"\nedition = \"2021\"\n\n[dependencies]\na = \"1\"\n\n[profile.release]\nlto = true\n"), s2);
            var s3 = Set(s2, I(1), "top");
            Assert.Equal(Crlf("top = 1\n\n[package]\nname = \"x\"\nedition = \"2021\"\n\n[dependencies]\na = \"1\"\n\n[profile.release]\nlto = true\n"), s3);
            Assert.DoesNotContain("\n", s3.Replace("\r\n", string.Empty));
        }

        [Fact]
        public void Crlf_replacement_keeps_the_line_endings()
        {
            var text = Crlf("[package]\nname = \"old\" # c\nversion = \"1\"\n");
            Assert.Equal(Crlf("[package]\nname = \"new\" # c\nversion = \"1\"\n"), Set(text, S("new"), "package", "name"));
        }

        [Fact]
        public void Missing_table_with_default_newline_when_document_has_no_line_break()
        {
            Assert.Equal("a = 1\n\n[t]\nx = 2\n", Set("a = 1\n", I(2), "t", "x"));
            Assert.Equal("a = 1\n\n[t]\nx = 2", Set("a = 1", I(2), "t", "x"));
        }

        [Fact]
        public void Bom_is_preserved_by_every_kind_of_edit()
        {
            var text = "﻿[package]\nname = \"x\"\n";
            var r = Set(text, S("y"), "package", "name");
            Assert.Equal("﻿[package]\nname = \"y\"\n", r);
            var i = Set(text, S("2021"), "package", "edition");
            Assert.Equal("﻿[package]\nname = \"x\"\nedition = \"2021\"\n", i);
            var root = Set(text, I(1), "top");
            Assert.Equal("﻿top = 1\n\n[package]\nname = \"x\"\n", root);
            var tbl = Set(text, B(true), "profile", "release", "lto");
            Assert.Equal("﻿[package]\nname = \"x\"\n\n[profile.release]\nlto = true\n", tbl);
            var comment = Set("﻿# c\n", I(1), "a");
            Assert.Equal("﻿# c\na = 1\n", comment);
        }

        // ------------------------------------------------------------ value formatting

        [Fact]
        public void Strings_are_written_as_escaped_basic_strings()
        {
            var r = Set("", S("a\"b\\c\nd\te\u0001"), "k");
            Assert.Equal("k = \"a\\\"b\\\\c\\nd\\te\\u0001\"\n", r);
            Assert.Equal("a\"b\\c\nd\te\u0001", TomlDocument.Parse(r).GetValue("k")!.AsString());
        }

        [Fact]
        public void Non_ascii_strings_are_kept_verbatim()
        {
            var r = Set("", S("café \U0001F600"), "k");
            Assert.Equal("k = \"café \U0001F600\"\n", r);
        }

        [Theory]
        [InlineData(1.0, "1.0")]
        [InlineData(0.1, "0.1")]
        [InlineData(-2.5, "-2.5")]
        [InlineData(100.0, "100.0")]
        [InlineData(double.PositiveInfinity, "inf")]
        [InlineData(double.NegativeInfinity, "-inf")]
        [InlineData(double.NaN, "nan")]
        public void Floats_are_written_round_trippable(double value, string expected)
        {
            var r = Set("", TomlValue.Float(value), "k");
            Assert.Equal("k = " + expected + "\n", r);
            var back = TomlDocument.Parse(r).GetValue("k")!.AsFloat()!.Value;
            Assert.True(value.Equals(back));
        }

        [Fact]
        public void Tiny_and_huge_floats_round_trip()
        {
            foreach (var v in new[] { 1e-7, 1.5e300, 123456789012345678.0, Math.PI })
            {
                var r = Set("", TomlValue.Float(v), "k");
                Assert.Equal(v, TomlDocument.Parse(r).GetValue("k")!.AsFloat());
            }
        }

        [Fact]
        public void Integers_are_written_in_decimal()
        {
            Assert.Equal("k = -42\n", Set("", I(-42), "k"));
            Assert.Equal("k = 9223372036854775807\n", Set("", I(long.MaxValue), "k"));
        }

        [Fact]
        public void Nested_arrays_and_tables_are_written_on_one_line()
        {
            var v = Arr(Arr(I(1), I(2)), Tbl(("a", S("x")), ("b c", B(false))), Arr());
            Assert.Equal("k = [[1, 2], { a = \"x\", \"b c\" = false }, []]\n", Set("", v, "k"));
        }

        // ------------------------------------------------------------ multi-line arrays

        [Fact]
        public void Multiline_array_is_rewritten_multiline_with_same_indentation_and_trailing_comma()
        {
            var text = F("""
                [package]
                keywords = [
                    "one", # first
                    "two",
                ]
                name = "x"
                """);
            Assert.Equal(F("""
                [package]
                keywords = [
                    "x",
                    "y",
                    "z",
                ]
                name = "x"
                """), Set(text, Arr(S("x"), S("y"), S("z")), "package", "keywords"));
        }

        [Fact]
        public void Multiline_array_without_trailing_comma_keeps_none()
        {
            var text = "[t]\n  k = [\n      1,\n      2\n  ]\n";
            Assert.Equal("[t]\n  k = [\n      3,\n      4,\n      5\n  ]\n", Set(text, Arr(I(3), I(4), I(5)), "t", "k"));
        }

        [Fact]
        public void Multiline_array_keeps_trailing_comment_of_its_last_line()
        {
            var text = "k = [\n    1,\n    2,\n] # the list\n";
            Assert.Equal("k = [\n    9,\n] # the list\n", Set(text, Arr(I(9)), "k"));
        }

        [Fact]
        public void Multiline_array_replaced_by_an_empty_array_becomes_single_line()
        {
            var text = "k = [\n    1,\n]\nn = 1\n";
            Assert.Equal("k = []\nn = 1\n", Set(text, Arr(), "k"));
        }

        [Fact]
        public void Multiline_array_replaced_in_crlf_document()
        {
            var text = Crlf("k = [\n    1,\n    2,\n]\n");
            Assert.Equal(Crlf("k = [\n    7,\n    8,\n    9,\n]\n"), Set(text, Arr(I(7), I(8), I(9)), "k"));
        }

        [Fact]
        public void Single_line_array_stays_single_line()
        {
            Assert.Equal("k = [1, 2, 3]\n", Set("k = [ 1 ]\n", Arr(I(1), I(2), I(3)), "k"));
        }

        // ------------------------------------------------------------ edits accumulate correctly

        [Fact]
        public void Successive_edits_each_apply_to_the_previous_text()
        {
            var doc = TomlDocument.Parse("[package]\nname = \"x\"\n");
            var t0 = doc.Text;
            var e1 = doc.SetValue(S("2021"), "package", "edition")!.Value;
            var t1 = doc.Text;
            var e2 = doc.SetValue(B(true), "profile", "release", "lto")!.Value;
            var t2 = doc.Text;
            var e3 = doc.Remove("package", "name")!.Value;
            AssertEdit(t0, e1, t1);
            AssertEdit(t1, e2, t2);
            AssertEdit(t2, e3, doc.Text);
            Assert.Equal("[package]\nedition = \"2021\"\n\n[profile.release]\nlto = true\n", doc.Text);
            Assert.Equal("2021", doc.GetValue("package", "edition")!.AsString());
            Assert.Null(doc.GetValue("package", "name"));
        }

        [Fact]
        public void Realistic_edit_session_changes_only_the_edited_lines()
        {
            var text = Crlf(F(TomlRoundTripTests.Member));
            var doc = TomlDocument.Parse(text);
            doc.SetValue(S("2024"), "package", "edition");
            doc.SetValue(S("A description"), "package", "description");
            doc.SetValue(I(3), "profile", "release", "opt-level");
            doc.SetValue(S("4.1"), "dependencies", "clap", "version");
            var oldLines = text.Split("\r\n");
            var newLines = doc.Text.Split("\r\n");
            var removed = oldLines.Except(newLines).ToArray();
            var added = newLines.Except(oldLines).ToArray();
            Assert.Equal(new[] { "description = \"An app\"", "edition.workspace = true", "clap = { version = \"4\", optional = true }" }, removed);
            Assert.Equal(
                new[]
                {
                    "description = \"A description\"",
                    "edition = \"2024\"",
                    "clap = { version = \"4.1\", optional = true }",
                    "[profile.release]",
                    "opt-level = 3",
                },
                added);
        }
    }
}

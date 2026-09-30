using Kubuno.Rust.Cargo.Toml;
using static Kubuno.Rust.Cargo.Tests.Toml.TomlTestHelper;

namespace Kubuno.Rust.Cargo.Tests.Toml
{
    public class TomlRemoveTests
    {
        [Fact]
        public void Removes_the_line_with_its_trailing_comment()
        {
            var text = F("""
                [package]
                name = "x" # the name
                version = "1"
                """);
            Assert.Equal(F("""
                [package]
                version = "1"
                """), Remove(text, "package", "name"));
        }

        [Fact]
        public void Absent_path_returns_null_and_changes_nothing()
        {
            var doc = TomlDocument.Parse("[a]\nx = 1\n");
            Assert.Null(doc.Remove("a", "y"));
            Assert.Null(doc.Remove("b"));
            Assert.Null(doc.Remove("a", "x", "deeper"));
            Assert.Equal("[a]\nx = 1\n", doc.Text);
        }

        [Fact]
        public void Removes_a_root_key()
        {
            Assert.Equal("b = 2\n\n[t]\nx = 1\n", Remove("a = 1\nb = 2\n\n[t]\nx = 1\n", "a"));
        }

        [Fact]
        public void Removes_a_multiline_value_entirely()
        {
            var text = F("""
                [package]
                keywords = [
                    "a", # c
                    "b",
                ]
                name = "x"
                """);
            Assert.Equal(F("""
                [package]
                name = "x"
                """), Remove(text, "package", "keywords"));
        }

        // ------------------------------------------------------------ inline tables

        [Theory]
        [InlineData("x", "a = { y = 2, z = 3 }\n")]
        [InlineData("y", "a = { x = 1, z = 3 }\n")]
        [InlineData("z", "a = { x = 1, y = 2 }\n")]
        public void Removes_an_entry_and_its_comma_from_an_inline_table(string key, string expected)
        {
            Assert.Equal(expected, Remove("a = { x = 1, y = 2, z = 3 }\n", "a", key));
        }

        [Fact]
        public void Removing_the_only_inline_entry_leaves_an_empty_table()
        {
            Assert.Equal("a = {} # c\n", Remove("a = { x = 1 } # c\n", "a", "x"));
        }

        [Fact]
        public void Removing_the_last_inline_entry_keeps_a_trailing_comma()
        {
            Assert.Equal("a = { x = 1, }\n", Remove("a = { x = 1, y = 2, }\n", "a", "y"));
        }

        [Fact]
        public void Removes_from_a_nested_inline_table_under_a_header()
        {
            var text = "[dependencies]\nserde = { version = \"1\", features = [\"derive\"], optional = true }\n";
            Assert.Equal("[dependencies]\nserde = { version = \"1\", optional = true }\n", Remove(text, "dependencies", "serde", "features"));
        }

        [Fact]
        public void Removes_the_whole_inline_valued_key()
        {
            var text = "[dependencies]\nserde = { version = \"1\" }\ntokio = \"1\"\n";
            Assert.Equal("[dependencies]\ntokio = \"1\"\n", Remove(text, "dependencies", "serde"));
        }

        // ------------------------------------------------------------ dotted subtrees

        [Fact]
        public void Removes_every_line_of_a_dotted_subtree()
        {
            var text = F("""
                [package]
                name = "x"
                edition.workspace = true
                edition.other = 1
                version = "1"
                """);
            Assert.Equal(F("""
                [package]
                name = "x"
                version = "1"
                """), Remove(text, "package", "edition"));
        }

        [Fact]
        public void Removes_a_single_dotted_leaf()
        {
            var text = "profile.release.lto = true\nprofile.release.opt-level = 3\n";
            Assert.Equal("profile.release.opt-level = 3\n", Remove(text, "profile", "release", "lto"));
        }

        // ------------------------------------------------------------ pruning

        [Fact]
        public void Prunes_a_table_left_empty_together_with_one_blank_line()
        {
            var text = F("""
                [package]
                name = "x"

                [dependencies]
                serde = "1"

                [features]
                a = []
                """);
            Assert.Equal(F("""
                [package]
                name = "x"

                [features]
                a = []
                """), Remove(text, "dependencies", "serde"));
        }

        [Fact]
        public void Prunes_the_last_table_of_the_file_without_leaving_a_trailing_blank_line()
        {
            var text = F("""
                [package]
                name = "x"

                [dependencies]
                serde = "1"
                """);
            Assert.Equal(F("""
                [package]
                name = "x"
                """), Remove(text, "dependencies", "serde"));
        }

        [Fact]
        public void Prunes_the_first_table_of_the_file()
        {
            var text = F("""
                [dependencies]
                serde = "1"

                [features]
                a = []
                """);
            Assert.Equal(F("""
                [features]
                a = []
                """), Remove(text, "dependencies", "serde"));
        }

        [Fact]
        public void Prunes_a_table_emptied_by_removing_a_dotted_subtree()
        {
            var text = "[package]\nedition.workspace = true\n\n[dependencies]\n";
            Assert.Equal("[dependencies]\n", Remove(text, "package", "edition"));
        }

        [Fact]
        public void Keeps_the_header_when_other_entries_remain()
        {
            var text = "[package]\nname = \"x\"\nversion = \"1\"\n";
            Assert.Equal("[package]\nversion = \"1\"\n", Remove(text, "package", "name"));
        }

        [Fact]
        public void Keeps_the_header_when_a_comment_remains_inside_the_table()
        {
            var text = F("""
                [dependencies]
                # keep me
                serde = "1"

                [features]
                """);
            Assert.Equal(F("""
                [dependencies]
                # keep me

                [features]
                """), Remove(text, "dependencies", "serde"));
        }

        [Fact]
        public void Keeps_the_header_when_a_sub_table_exists()
        {
            var text = F("""
                [profile.release]
                lto = true

                [profile.release.package.foo]
                opt-level = 2
                """);
            Assert.Equal(F("""
                [profile.release]

                [profile.release.package.foo]
                opt-level = 2
                """), Remove(text, "profile", "release", "lto"));
        }

        [Fact]
        public void Keeps_the_header_when_comments_sit_directly_above_it()
        {
            var text = "# Dependencies\n[dependencies]\nserde = \"1\"\n";
            Assert.Equal("# Dependencies\n[dependencies]\n", Remove(text, "dependencies", "serde"));
        }

        [Fact]
        public void Does_not_prune_an_empty_inline_table_owner()
        {
            var text = "a = { x = 1 }\n[t]\ny = 1\n";
            Assert.Equal("a = {}\n[t]\ny = 1\n", Remove(text, "a", "x"));
        }

        // ------------------------------------------------------------ whole tables

        [Fact]
        public void Removes_a_whole_table_and_one_blank_separator_line()
        {
            var text = F("""
                [profile.release]
                lto = true

                [profile.dev]
                opt-level = 1

                [dependencies]
                a = "1"
                """);
            Assert.Equal(F("""
                [profile.release]
                lto = true

                [dependencies]
                a = "1"
                """), Remove(text, "profile", "dev"));
        }

        [Fact]
        public void Removing_a_table_takes_its_inner_comments_and_the_comment_attached_above()
        {
            var text = F("""
                [a]
                x = 1

                # the b table
                [b]
                # inner
                y = 2
                # tail

                [c]
                z = 3
                """);
            Assert.Equal(F("""
                [a]
                x = 1

                [c]
                z = 3
                """), Remove(text, "b"));
        }

        [Fact]
        public void Removing_a_table_keeps_the_comment_attached_to_the_next_header()
        {
            var text = F("""
                [a]
                x = 1

                [b]
                y = 2

                # about c
                [c]
                z = 3
                """);
            Assert.Equal(F("""
                [a]
                x = 1

                # about c
                [c]
                z = 3
                """), Remove(text, "b"));
        }

        [Fact]
        public void Removes_the_last_table_of_the_file()
        {
            Assert.Equal("[a]\nx = 1\n", Remove("[a]\nx = 1\n\n[b]\ny = 2\n", "b"));
        }

        [Fact]
        public void Removes_the_first_table_of_the_file()
        {
            Assert.Equal("[b]\ny = 2\n", Remove("[a]\nx = 1\n\n[b]\ny = 2\n", "a"));
        }

        [Fact]
        public void Removes_a_super_table_with_all_its_sub_tables()
        {
            var text = "[a.b]\nx = 1\n\n[a.c]\ny = 2\n\n[z]\nk = 1\n";
            Assert.Equal("[z]\nk = 1\n", Remove(text, "a"));
        }

        [Fact]
        public void Removes_a_subtable_defined_by_header_via_its_parent_path()
        {
            var text = "[dependencies]\na = \"1\"\n\n[dependencies.serde]\nversion = \"1\"\n\n[features]\n";
            Assert.Equal("[dependencies]\na = \"1\"\n\n[features]\n", Remove(text, "dependencies", "serde"));
        }

        [Fact]
        public void Removes_one_key_of_a_header_defined_dependency()
        {
            var text = "[dependencies.serde]\nversion = \"1\"\nfeatures = [\"derive\"]\n";
            Assert.Equal("[dependencies.serde]\nversion = \"1\"\n", Remove(text, "dependencies", "serde", "features"));
        }

        [Fact]
        public void Removes_all_elements_of_an_array_of_tables()
        {
            var text = F("""
                [package]
                name = "x"

                [[bin]]
                name = "a"

                [[bin]]
                name = "b"

                [features]
                """);
            Assert.Equal(F("""
                [package]
                name = "x"

                [features]
                """), Remove(text, "bin"));
        }

        [Fact]
        public void Keys_inside_an_array_of_tables_are_not_addressable()
        {
            var doc = TomlDocument.Parse("[[bin]]\nname = \"a\"\n");
            Assert.Null(doc.Remove("bin", "name"));
        }

        // ------------------------------------------------------------ formats

        [Fact]
        public void Removal_works_on_crlf_documents()
        {
            var text = Crlf("[package]\nname = \"x\"\n\n[dependencies]\nserde = \"1\"\n\n[features]\n");
            Assert.Equal(Crlf("[package]\nname = \"x\"\n\n[features]\n"), Remove(text, "dependencies", "serde"));
        }

        [Fact]
        public void Removal_keeps_the_bom()
        {
            var text = "﻿[a]\nx = 1\n\n[b]\ny = 2\n";
            Assert.Equal("﻿[b]\ny = 2\n", Remove(text, "a"));
        }

        [Fact]
        public void Removal_at_the_end_of_a_file_without_newline()
        {
            Assert.Equal("[a]\nx = 1\n", Remove("[a]\nx = 1\ny = 2", "a", "y"));
        }

        [Fact]
        public void Removal_never_leaves_two_consecutive_blank_lines_where_there_were_none()
        {
            var text = F("""
                [a]
                x = 1

                [b]
                y = 2

                [c]
                z = 3

                [d]
                w = 4
                """);
            var r = Remove(text, "c", "z");
            Assert.DoesNotContain("\n\n\n", r);
            Assert.Equal(F("""
                [a]
                x = 1

                [b]
                y = 2

                [d]
                w = 4
                """), r);
        }

        [Fact]
        public void Set_then_remove_restores_the_original_text()
        {
            var text = F(TomlRoundTripTests.Member);
            var doc = TomlDocument.Parse(text);
            doc.SetValue(S("A"), "package", "homepage");
            doc.SetValue(B(true), "profile", "release", "lto");
            Assert.NotEqual(text, doc.Text);
            doc.Remove("package", "homepage");
            doc.Remove("profile", "release", "lto");
            Assert.Equal(text, doc.Text);
        }
    }
}

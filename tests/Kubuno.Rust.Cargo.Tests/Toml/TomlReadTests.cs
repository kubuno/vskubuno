using Kubuno.Rust.Cargo.Toml;
using static Kubuno.Rust.Cargo.Tests.Toml.TomlTestHelper;

namespace Kubuno.Rust.Cargo.Tests.Toml
{
    public class TomlReadTests
    {
        [Fact]
        public void GetValue_through_header_table()
        {
            var d = TomlDocument.Parse(F("""
                [package]
                name = "demo"
                version = "0.1.0"
                """));
            Assert.Equal("demo", d.GetValue("package", "name")!.AsString());
            Assert.Equal("0.1.0", d.GetValue("package", "version")!.AsString());
        }

        [Fact]
        public void GetValue_absent_is_null()
        {
            var d = TomlDocument.Parse(F("""
                [package]
                name = "demo"
                """));
            Assert.Null(d.GetValue("package", "edition"));
            Assert.Null(d.GetValue("nope"));
            Assert.Null(d.GetValue("package", "name", "deeper"));
        }

        [Fact]
        public void GetValue_through_dotted_keys_and_inheritance()
        {
            var d = TomlDocument.Parse(F("""
                [package]
                name = "demo"
                edition.workspace = true
                version = { workspace = true }
                """));
            var edition = d.GetValue("package", "edition")!;
            Assert.Equal(TomlValueKind.Table, edition.Kind);
            Assert.Equal(true, edition.AsTable()![0].Value.AsBoolean());
            Assert.Equal(true, d.GetValue("package", "edition", "workspace")!.AsBoolean());
            Assert.Equal(true, d.GetValue("package", "version", "workspace")!.AsBoolean());
            Assert.Null(edition.Raw);
        }

        [Fact]
        public void GetValue_through_inline_tables()
        {
            var d = TomlDocument.Parse(F("""
                [dependencies]
                serde = { version = "1", features = ["derive"] }
                tokio = "1"
                """));
            Assert.Equal("1", d.GetValue("dependencies", "serde", "version")!.AsString());
            Assert.Equal("derive", d.GetValue("dependencies", "serde", "features")!.AsArray()![0].AsString());
            var serde = d.GetValue("dependencies", "serde")!;
            Assert.Equal(TomlValueKind.Table, serde.Kind);
            Assert.Equal("{ version = \"1\", features = [\"derive\"] }", serde.Raw);
        }

        [Fact]
        public void GetValue_through_root_dotted_keys()
        {
            var d = TomlDocument.Parse(F("""
                profile.release.lto = true
                profile.release.opt-level = 3
                """));
            Assert.Equal(true, d.GetValue("profile", "release", "lto")!.AsBoolean());
            var release = d.GetValue("profile", "release")!;
            Assert.Equal(2, release.AsTable()!.Count);
            Assert.True(d.ContainsTable("profile"));
            Assert.True(d.ContainsTable("profile", "release"));
        }

        [Fact]
        public void GetValue_through_nested_headers_and_implicit_super_tables()
        {
            var d = TomlDocument.Parse(F("""
                [profile.release]
                lto = true

                [profile.release.package.foo]
                opt-level = 2
                """));
            Assert.Equal(true, d.GetValue("profile", "release", "lto")!.AsBoolean());
            Assert.Equal(2, d.GetValue("profile", "release", "package", "foo", "opt-level")!.AsInteger());
            Assert.True(d.ContainsTable("profile"));
            Assert.True(d.ContainsTable("profile", "release", "package"));
            Assert.Equal(new[] { "release" }, d.GetKeys("profile"));
            Assert.Equal(new[] { "lto", "package" }, d.GetKeys("profile", "release"));
        }

        [Fact]
        public void GetValue_merges_all_sources_of_a_table()
        {
            var d = TomlDocument.Parse(F("""
                [a]
                x = 1
                y.z = 2
                w = { q = 3 }
                [a.sub]
                k = 4
                """));
            var a = d.GetValue("a")!;
            Assert.Equal(TomlValueKind.Table, a.Kind);
            Assert.Null(a.Raw);
            Assert.Equal(new[] { "x", "y", "w", "sub" }, a.AsTable()!.Select(e => e.Key).ToArray());
            Assert.Equal(2, d.GetValue("a", "y", "z")!.AsInteger());
            Assert.Equal(3, d.GetValue("a", "w", "q")!.AsInteger());
            Assert.Equal(4, d.GetValue("a", "sub", "k")!.AsInteger());
        }

        [Fact]
        public void GetValue_with_no_path_is_the_root_table()
        {
            var d = TomlDocument.Parse(F("""
                a = 1
                [t]
                b = 2
                """));
            var root = d.GetValue()!;
            Assert.Equal(TomlValueKind.Table, root.Kind);
            Assert.Equal(new[] { "a", "t" }, root.AsTable()!.Select(e => e.Key).ToArray());
            Assert.True(d.ContainsTable());
        }

        [Fact]
        public void Quoted_header_keys_resolve()
        {
            var d = TomlDocument.Parse(F("""
                [target.'cfg(windows)'.dependencies]
                winapi = "0.3"

                [target."cfg(unix)".dependencies]
                libc = "0.2"
                """));
            Assert.Equal("0.3", d.GetValue("target", "cfg(windows)", "dependencies", "winapi")!.AsString());
            Assert.Equal("0.2", d.GetValue("target", "cfg(unix)", "dependencies", "libc")!.AsString());
            Assert.Equal(new[] { "cfg(windows)", "cfg(unix)" }, d.GetKeys("target"));
        }

        [Fact]
        public void ContainsTable_covers_every_definition_form()
        {
            var d = TomlDocument.Parse(F("""
                dotted.k = 1
                inline = { a = 1 }
                scalar = 1
                [hdr]
                [outer.inner]
                x = 1
                """));
            Assert.True(d.ContainsTable("dotted"));
            Assert.True(d.ContainsTable("inline"));
            Assert.True(d.ContainsTable("hdr"));
            Assert.True(d.ContainsTable("outer"));
            Assert.True(d.ContainsTable("outer", "inner"));
            Assert.False(d.ContainsTable("scalar"));
            Assert.False(d.ContainsTable("missing"));
            Assert.False(d.ContainsTable("outer", "inner", "x"));
        }

        [Fact]
        public void Empty_header_table_is_an_empty_table()
        {
            var d = TomlDocument.Parse("[empty]\n");
            Assert.True(d.ContainsTable("empty"));
            Assert.Empty(d.GetValue("empty")!.AsTable()!);
            Assert.Empty(d.GetKeys("empty"));
        }

        [Fact]
        public void GetKeys_is_in_document_order_and_deduplicated()
        {
            var d = TomlDocument.Parse(F("""
                z = 1
                a.x = 1
                a.y = 2
                [m]
                b = 1
                [m.n]
                c = 1
                """));
            Assert.Equal(new[] { "z", "a", "m" }, d.GetKeys());
            Assert.Equal(new[] { "x", "y" }, d.GetKeys("a"));
            Assert.Equal(new[] { "b", "n" }, d.GetKeys("m"));
        }

        [Fact]
        public void GetKeys_of_inline_table_and_missing_table()
        {
            var d = TomlDocument.Parse("t = { b = 1, a = 2 }\n");
            Assert.Equal(new[] { "b", "a" }, d.GetKeys("t"));
            Assert.Empty(d.GetKeys("nope"));
        }

        [Fact]
        public void GetArrayOfTables_reads_each_element()
        {
            var d = TomlDocument.Parse(F("""
                [package]
                name = "x"

                [[bin]]
                name = "one"
                path = "src/one.rs"

                [[bin]]
                name = "two"
                required-features = ["a", "b"]
                test.harness = false
                meta = { k = 1 }

                [bin.extra]
                z = 9

                [[example]]
                name = "ex"
                """));
            var bins = d.GetArrayOfTables("bin");
            Assert.Equal(2, bins.Count);
            Assert.Equal("one", bins[0].GetValue("name")!.AsString());
            Assert.Equal("src/one.rs", bins[0].GetValue("path")!.AsString());
            Assert.Equal(new[] { "name", "path" }, bins[0].GetKeys());
            Assert.Equal("two", bins[1].GetValue("name")!.AsString());
            Assert.Equal(2, bins[1].GetValue("required-features")!.AsArray()!.Count);
            Assert.Equal(false, bins[1].GetValue("test", "harness")!.AsBoolean());
            Assert.Equal(1, bins[1].GetValue("meta", "k")!.AsInteger());
            Assert.Equal(9, bins[1].GetValue("extra", "z")!.AsInteger());
            Assert.Null(bins[0].GetValue("required-features"));
            Assert.Single(d.GetArrayOfTables("example"));
            Assert.Empty(d.GetArrayOfTables("lib"));
        }

        [Fact]
        public void GetValue_of_an_array_of_tables_is_an_array_of_tables()
        {
            var d = TomlDocument.Parse(F("""
                [[bin]]
                name = "one"
                [[bin]]
                name = "two"
                """));
            var arr = d.GetValue("bin")!;
            Assert.Equal(TomlValueKind.Array, arr.Kind);
            Assert.Equal("two", arr.AsArray()![1].AsTable()![0].Value.AsString());
            Assert.Equal(new[] { "bin" }, d.GetKeys());
            Assert.False(d.ContainsTable("bin"));
        }

        [Fact]
        public void Accessors_return_null_for_other_kinds()
        {
            var s = TomlValue.String("x");
            Assert.Null(s.AsInteger());
            Assert.Null(s.AsFloat());
            Assert.Null(s.AsBoolean());
            Assert.Null(s.AsArray());
            Assert.Null(s.AsTable());
            Assert.Null(TomlValue.Integer(1).AsString());
            Assert.Null(TomlValue.Boolean(true).AsInteger());
            Assert.Null(TomlValue.Float(1).AsBoolean());
            Assert.Null(TomlValue.Array(new TomlValue[0]).AsTable());
        }

        [Fact]
        public void Values_created_in_code_have_no_raw()
        {
            Assert.Null(S("x").Raw);
            Assert.Null(I(1).Raw);
            Assert.Null(Arr(I(1)).Raw);
        }

        [Fact]
        public void Equality_is_semantic()
        {
            var d = TomlDocument.Parse("a = 'x'\nb = \"x\"\nt = { p = 1, q = 2 }\nu = { q = 2, p = 1 }\n");
            Assert.Equal(d.GetValue("a"), d.GetValue("b"));
            Assert.Equal(d.GetValue("a")!.GetHashCode(), d.GetValue("b")!.GetHashCode());
            Assert.Equal(d.GetValue("t"), d.GetValue("u"));
            Assert.Equal(d.GetValue("t")!.GetHashCode(), d.GetValue("u")!.GetHashCode());
            Assert.NotEqual(I(1), TomlValue.Float(1));
            Assert.NotEqual(S("1"), I(1));
            Assert.Equal(Arr(I(1), S("a")), Arr(I(1), S("a")));
            Assert.NotEqual(Arr(I(1)), Arr(I(1), I(2)));
        }

        [Fact]
        public void Table_factory_rejects_duplicate_keys()
        {
            Assert.Throws<ArgumentException>(() => Tbl(("a", I(1)), ("a", I(2))));
        }

        [Fact]
        public void ToString_renders_single_line_toml()
        {
            Assert.Equal("{ a = 1, b = [\"x\", true] }", Tbl(("a", I(1)), ("b", Arr(S("x"), B(true)))).ToString());
        }
    }
}

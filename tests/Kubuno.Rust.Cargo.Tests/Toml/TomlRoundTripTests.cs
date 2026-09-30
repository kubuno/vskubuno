using Kubuno.Rust.Cargo.Toml;
using static Kubuno.Rust.Cargo.Tests.Toml.TomlTestHelper;

namespace Kubuno.Rust.Cargo.Tests.Toml
{
    public class TomlRoundTripTests
    {
        public const string WorkspaceRoot = """
            # Kubuno desktop workspace
            [workspace]
            resolver = "2"
            members = [
                "kubuno_ui",      # shared UI
                "drive/*",
                # "disabled",
                "apps/app-one",
            ]
            exclude = ["target"]

            [workspace.package]
            version = "0.4.2"
            edition = "2021"
            authors = ["Kubuno <kubuno@toiledev.com>"]
            license = "AGPL-3.0-only"
            rust-version = "1.85"

            [workspace.dependencies]
            serde = { version = "1", features = ["derive"] }
            tokio = { version = "1.40", default-features = false, features = [
                "rt-multi-thread",
                "macros", # needed by main
            ] }
            anyhow = "1"
            kubuno_ui = { path = "kubuno_ui", version = "0.4.2" }

            [workspace.lints.clippy]
            pedantic = { level = "warn", priority = -1 }
            unwrap_used = "deny"

            [profile.release]
            opt-level = 3
            lto = "thin"   # smaller binaries
            codegen-units = 1
            """;

        public const string Member = """
            [package]
            name = "app-one"
            description = "An app"
            version.workspace = true
            edition.workspace = true
            authors.workspace = true
            license = { workspace = true }
            publish = false

            [lib]
            crate-type = ["cdylib", "rlib"]

            [[bin]]
            name = "app-one"
            path = "src/main.rs"

            [[bin]]
            name = "tool"
            path = "src/bin/tool.rs"
            required-features = ["tools"]

            [[example]]
            name = "demo"

            [features]
            default = ["a"]
            a = []
            tools = ["dep:clap"]

            [dependencies]
            serde.workspace = true
            clap = { version = "4", optional = true }
            kubuno_ui = { workspace = true }

            [target.'cfg(windows)'.dependencies]
            windows = { version = "0.62", features = ["Win32_Foundation"] }

            [target."cfg(unix)".dependencies]
            libc = "0.2"

            [dev-dependencies]
            tempfile = "3"

            [lints]
            workspace = true

            [lints.clippy]
            pedantic = { level = "warn", priority = -1 }
            """;

        public const string CommentsEverywhere = """
            # ---- header ----
            # second line

            [package] # the package
            # before name
            name = "x" # name
            version = "1.0.0" # ver

            # a lonely comment

            [dependencies]  # deps
            a = [ # opening
              "one", # first
              # between
              "two",
            ] # closing
            b = { version = "1" } # inline

            # trailing comment at EOF
            """;

        public static IEnumerable<object[]> Documents()
        {
            yield return new object[] { F(WorkspaceRoot) };
            yield return new object[] { F(Member) };
            yield return new object[] { F(CommentsEverywhere) };
            yield return new object[] { NoEol(CommentsEverywhere) };
            yield return new object[] { Crlf(F(Member)) };
            yield return new object[] { Crlf(F(WorkspaceRoot)) };
            yield return new object[] { "﻿" + F(Member) };
            yield return new object[] { "﻿" + Crlf(F(WorkspaceRoot)) };
            yield return new object[] { "" };
            yield return new object[] { "\n\n\n" };
            yield return new object[] { "   \t \n# only a comment" };
            yield return new object[] { "[a]\n\n\n\n[b]\n\n" };
            yield return new object[] { "a=1\nb   =   2\n  c = 3\n\t[t]\n\tk\t=\t4\t\n" };
        }

        [Theory]
        [MemberData(nameof(Documents))]
        public void Round_trip_is_identity(string text)
        {
            var d = TomlDocument.Parse(text);
            Assert.Equal(text, d.Text);
            Assert.Equal(text, d.ToString());
        }

        [Fact]
        public void Workspace_root_reads_correctly()
        {
            var d = TomlDocument.Parse(F(WorkspaceRoot));
            Assert.Equal("2", d.GetValue("workspace", "resolver")!.AsString());
            var members = d.GetValue("workspace", "members")!.AsArray()!;
            Assert.Equal(new[] { "kubuno_ui", "drive/*", "apps/app-one" }, members.Select(m => m.AsString()).ToArray());
            Assert.Equal("0.4.2", d.GetValue("workspace", "package", "version")!.AsString());
            Assert.Equal("thin", d.GetValue("profile", "release", "lto")!.AsString());
            Assert.Equal(-1, d.GetValue("workspace", "lints", "clippy", "pedantic", "priority")!.AsInteger());
            Assert.Equal("warn", d.GetValue("workspace", "lints", "clippy", "pedantic", "level")!.AsString());
            var features = d.GetValue("workspace", "dependencies", "tokio", "features")!.AsArray()!;
            Assert.Equal(2, features.Count);
            Assert.Equal(new[] { "workspace", "profile" }, d.GetKeys());
        }

        [Fact]
        public void Member_reads_correctly()
        {
            var d = TomlDocument.Parse(F(Member));
            Assert.Equal(true, d.GetValue("package", "version", "workspace")!.AsBoolean());
            Assert.Equal(true, d.GetValue("package", "license", "workspace")!.AsBoolean());
            Assert.Equal(false, d.GetValue("package", "publish")!.AsBoolean());
            Assert.Equal(new[] { "cdylib", "rlib" }, d.GetValue("lib", "crate-type")!.AsArray()!.Select(x => x.AsString()).ToArray());
            Assert.Equal(2, d.GetArrayOfTables("bin").Count);
            Assert.Equal("demo", d.GetArrayOfTables("example")[0].GetValue("name")!.AsString());
            Assert.Equal(new[] { "default", "a", "tools" }, d.GetKeys("features"));
            Assert.Equal("0.62", d.GetValue("target", "cfg(windows)", "dependencies", "windows", "version")!.AsString());
            Assert.Equal(true, d.GetValue("dependencies", "serde", "workspace")!.AsBoolean());
            Assert.Equal(true, d.GetValue("lints", "workspace")!.AsBoolean());
            Assert.Equal("warn", d.GetValue("lints", "clippy", "pedantic", "level")!.AsString());
            Assert.Equal(new[] { "package", "lib", "bin", "example", "features", "dependencies", "target", "dev-dependencies", "lints" }, d.GetKeys());
        }

        [Fact]
        public void Comments_everywhere_reads_correctly()
        {
            var d = TomlDocument.Parse(F(CommentsEverywhere));
            Assert.Equal("x", d.GetValue("package", "name")!.AsString());
            Assert.Equal(new[] { "one", "two" }, d.GetValue("dependencies", "a")!.AsArray()!.Select(x => x.AsString()).ToArray());
            Assert.Equal("1", d.GetValue("dependencies", "b", "version")!.AsString());
        }

        private static List<string[]> LeafPaths(TomlDocument d)
        {
            var result = new List<string[]>();
            void Walk(string[] table)
            {
                foreach (var key in d.GetKeys(table))
                {
                    var path = table.Concat(new[] { key }).ToArray();
                    if (d.GetArrayOfTables(path).Count > 0) continue;
                    if (d.ContainsTable(path)) Walk(path);
                    else result.Add(path);
                }
            }

            Walk(new string[0]);
            return result;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Setting_any_single_line_leaf_changes_exactly_one_line(bool crlf)
        {
            foreach (var source in new[] { Member, WorkspaceRoot })
            {
                var text = crlf ? Crlf(F(source)) : F(source);
                var probe = TomlDocument.Parse(text);
                foreach (var path in LeafPaths(probe))
                {
                    var raw = probe.GetValue(path)!.Raw!;
                    if (raw.Contains('\n')) continue;
                    var doc = TomlDocument.Parse(text);
                    var edit = doc.SetValue(S("changed-value"), path);
                    Assert.NotNull(edit);
                    AssertEdit(text, edit!.Value, doc.Text);
                    var before = text.Split('\n');
                    var after = doc.Text.Split('\n');
                    Assert.Equal(before.Length, after.Length);
                    Assert.Single(before.Zip(after, (b, a) => b != a), differs => differs);
                    Assert.Equal("changed-value", doc.GetValue(path)!.AsString());
                }
            }
        }

        [Fact]
        public void Removing_every_leaf_one_by_one_always_leaves_a_valid_document()
        {
            foreach (var source in new[] { Member, WorkspaceRoot })
            {
                var text = F(source);
                var doc = TomlDocument.Parse(text);
                foreach (var path in LeafPaths(TomlDocument.Parse(text)))
                {
                    var before = doc.Text;
                    var edit = doc.Remove(path);
                    if (edit == null) continue;
                    AssertEdit(before, edit.Value, doc.Text);
                    Assert.Null(doc.GetValue(path));
                }

                Assert.Empty(LeafPaths(doc));
            }
        }

        [Fact]
        public void No_op_edits_leave_the_text_identical()
        {
            var text = Crlf(F(Member));
            var d = TomlDocument.Parse(text);
            Assert.Null(d.SetValue(S("app-one"), "package", "name"));
            Assert.Null(d.SetValue(B(false), "package", "publish"));
            Assert.Null(d.Remove("package", "nonexistent"));
            Assert.Equal(text, d.Text);
        }
    }
}

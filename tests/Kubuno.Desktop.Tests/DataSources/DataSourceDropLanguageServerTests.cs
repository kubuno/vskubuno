using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Kubuno.VisualStudio.Core.DataSources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.DataSources
{
    /// <summary>
    /// The drop fragments through the real <c>kubuno-views-ls</c> (the designer's edit pipeline: <c>kubuno/applyEdit</c>'s
    /// <c>insertFragment</c>, then the server's own diagnostics of the result). Skipped (inconclusive) when the release build of the
    /// server is not there (<c>KUBUNO_VIEWS_LS</c>, else <c>C:\kubuno-build\desktop-target\release\kubuno-views-ls.exe</c>).
    /// </summary>
    [TestClass]
    public sealed class DataSourceDropLanguageServerTests
    {
        private const string DefaultServer = @"C:\kubuno-build\desktop-target\release\kubuno-views-ls.exe";
        private const string DesktopCrates = @"Z:\src\desktop\windows\src\crates";

        [TestMethod]
        public void GridAndDetailsDropsRoundTripThroughTheLanguageServer()
        {
            string exe = Environment.GetEnvironmentVariable("KUBUNO_VIEWS_LS") is { Length: > 0 } configured ? configured : DefaultServer;
            if (!File.Exists(exe))
            {
                Assert.Inconclusive("kubuno-views-ls.exe not found at " + exe);
            }

            // A crate that links kubuno-data, so the server knows the data components (its scan of path dependencies).
            string root = Path.Combine(Path.GetTempPath(), "kubuno-ds-ls-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "src"));
            string dataCrate = Path.Combine(DesktopCrates, "kubuno-data");
            File.WriteAllText(Path.Combine(root, "Cargo.toml"), "[package]\nname = \"ds_ls\"\nversion = \"0.1.0\"\nedition = \"2021\"\n\n[dependencies]\nkubuno-data = { path = \"" + dataCrate.Replace('\\', '/') + "\" }\n");
            string viewPath = Path.Combine(root, "src", "main_view.kbview");
            File.WriteAllText(viewPath, DataSourceFixtures.TemplateView);
            try
            {
                using var server = LspProcess.Start(exe);
                server.Request("initialize", new JsonObject { ["processId"] = Process.GetCurrentProcess().Id, ["rootUri"] = new Uri(root).AbsoluteUri, ["capabilities"] = new JsonObject() });
                server.Notify("initialized", new JsonObject());
                string uri = new Uri(viewPath).AbsoluteUri;
                server.Notify("textDocument/didOpen", new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["uri"] = uri, ["languageId"] = "kbview", ["version"] = 1, ["text"] = DataSourceFixtures.TemplateView },
                });
                var baseline = server.WaitDiagnostics(uri, 1);

                // 1. customers as a grid, at (24, 80) of the root.
                var shop = DataSourceFixtures.Shop();
                string text = DataSourceFixtures.TemplateView;
                var grid = DataSourceDropPlanner.Plan(text, new DataDropRequest(shop, shop.Table("customers")!, DataTableDropMode.Grid, null), string.Empty, 2, 24, 80, out var error)!;
                Assert.IsNotNull(grid, error);
                text = ApplyThroughServer(server, uri, text, grid, 2);

                var outline = KbviewOutline.TryParse(text)!;
                Assert.IsNotNull(outline, text);
                CollectionAssert.AreEqual(
                    new[] { "DbConnection", "TableAdapter", "BindingSource", "ErrorProvider", "TextField", "Button", "BindingNavigator", "DataTable" },
                    outline.Root.Children.Select(c => c.Name).ToArray(),
                    text);
                Assert.AreEqual("customers_data_table", outline.Find(grid.SelectElementId)!.XName);
                Assert.AreEqual(7, outline.Find(grid.SelectElementId)!.Children.Count);
                StringAssert.Contains(text, "<!--\n  The main window of app.\n-->", "the comment is kept");
                StringAssert.Contains(text, "\n  <DbConnection x:Name=\"shop_connection\"", "components on their own indented lines");
                StringAssert.Contains(text, "\n    <Column Header=\"Id\"", "grid columns one level deeper");
                var afterGrid = server.WaitDiagnostics(uri, 2);
                AssertNoNewErrors(baseline, afterGrid, text);

                // 2. the same table in details mode next to it: its components are reused.
                var details = DataSourceDropPlanner.Plan(text, new DataDropRequest(shop, shop.Table("customers")!, DataTableDropMode.Details, null), string.Empty, 8, 520, 80, out error)!;
                Assert.IsNotNull(details, error);
                text = ApplyThroughServer(server, uri, text, details, 3);
                outline = KbviewOutline.TryParse(text)!;
                Assert.IsNotNull(outline, text);
                Assert.AreEqual(1, outline.All().Count(e => e.Name == "BindingSource"), "one binding source for the grid and the details");
                Assert.AreEqual("id_text_field", outline.Find(details.SelectElementId)!.XName);
                Assert.AreEqual(outline.All().Count(e => e.XName != null), outline.Names().Count, "no duplicate x:Name");
                AssertNoNewErrors(baseline, server.WaitDiagnostics(uri, 3), text);

                // 3. orders into the same view (a second adapter on the same connection).
                var orders = DataSourceDropPlanner.Plan(text, new DataDropRequest(shop, shop.Table("orders")!, DataTableDropMode.Grid, null), string.Empty, outline.Root.Children.Count, 24, 360, out error)!;
                text = ApplyThroughServer(server, uri, text, orders, 4);
                outline = KbviewOutline.TryParse(text)!;
                Assert.AreEqual(1, outline.All().Count(e => e.Name == "DbConnection"));
                Assert.AreEqual(2, outline.All().Count(e => e.Name == "TableAdapter"));
                AssertNoNewErrors(baseline, server.WaitDiagnostics(uri, 4), text);
            }
            finally
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }

        /// <summary>Runs every insertion of <paramref name="plan"/> through <c>kubuno/applyEdit</c> against the same text, applies all the edits at once (like the designer), and syncs the server.</summary>
        private static string ApplyThroughServer(LspProcess server, string uri, string text, DataDropPlan plan, int version)
        {
            var edits = new List<(int Start, int End, string NewText)>();
            foreach (var insertion in plan.Insertions)
            {
                var result = server.Request("kubuno/applyEdit", new JsonObject
                {
                    ["uri"] = uri,
                    ["op"] = new JsonObject { ["kind"] = "insertFragment", ["parentId"] = insertion.ParentId, ["index"] = insertion.Index, ["xml"] = insertion.Xml },
                });
                var array = result?["edits"] as JsonArray;
                Assert.IsTrue(array != null && array.Count > 0, "kubuno/applyEdit accepted the fragment: " + insertion.Xml);
                foreach (var edit in array!)
                {
                    var range = edit!["range"]!;
                    edits.Add((Offset(text, range["start"]!), Offset(text, range["end"]!), edit["newText"]!.GetValue<string>()));
                }
            }

            var builder = new StringBuilder(text);
            foreach (var edit in edits.OrderByDescending(e => e.Start))
            {
                builder.Remove(edit.Start, edit.End - edit.Start).Insert(edit.Start, edit.NewText);
            }

            string updated = builder.ToString();
            Assert.IsFalse(updated.Contains("x:Name=\"customers_table_adapter2\""), "the server renamed nothing");
            server.Notify("textDocument/didChange", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = version },
                ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = updated }),
            });
            return updated;
        }

        private static int Offset(string text, JsonNode position)
        {
            int line = position["line"]!.GetValue<int>();
            int character = position["character"]!.GetValue<int>();
            int offset = 0;
            for (int i = 0; i < line; i++)
            {
                offset = text.IndexOf('\n', offset) + 1;
            }

            return offset + character;
        }

        private static void AssertNoNewErrors(IReadOnlyList<string> before, IReadOnlyList<string> after, string text)
        {
            var added = after.Where(d => d.StartsWith("1:", StringComparison.Ordinal) && !before.Contains(d)).ToList();
            Assert.AreEqual(0, added.Count, "new errors: " + string.Join(" | ", added) + "\n" + text);
        }

        /// <summary>A minimal LSP client over the server's stdio (Content-Length framing).</summary>
        private sealed class LspProcess : IDisposable
        {
            private readonly Process _process;
            private readonly Stream _output;
            private int _id;

            private LspProcess(Process process)
            {
                _process = process;
                _output = process.StandardOutput.BaseStream;
            }

            public static LspProcess Start(string exe)
            {
                var info = new ProcessStartInfo(exe)
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                // The release build imports the Rust std dylib of the toolchain (beside the exe when shipped).
                string toolchain = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".rustup\toolchains\stable-x86_64-pc-windows-msvc\lib\rustlib\x86_64-pc-windows-msvc\lib");
                info.EnvironmentVariables["PATH"] = Path.GetDirectoryName(exe) + ";" + toolchain + ";" + Environment.GetEnvironmentVariable("PATH");
                var process = Process.Start(info)!;
                process.ErrorDataReceived += (_, _) => { };
                process.BeginErrorReadLine();
                return new LspProcess(process);
            }

            public JsonNode? Request(string method, JsonObject parameters)
            {
                int id = ++_id;
                Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < deadline)
                {
                    var message = Read();
                    if (message?["id"] is JsonValue value && value.GetValue<int>() == id)
                    {
                        Assert.IsNull(message["error"], method + ": " + message["error"]?.ToJsonString());
                        return message["result"];
                    }

                    _pending.Add(message);
                }

                Assert.Fail(method + " timed out");
                return null;
            }

            public void Notify(string method, JsonObject parameters) =>
                Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });

            private readonly List<JsonNode?> _pending = new List<JsonNode?>();

            /// <summary>The diagnostics published for <paramref name="uri"/> at <paramref name="version"/>, as "severity:message".</summary>
            public IReadOnlyList<string> WaitDiagnostics(string uri, int version)
            {
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < deadline)
                {
                    JsonNode? message;
                    if (_pending.Count > 0)
                    {
                        message = _pending[0];
                        _pending.RemoveAt(0);
                    }
                    else
                    {
                        message = Read();
                    }

                    if (message?["method"]?.GetValue<string>() == "textDocument/publishDiagnostics" &&
                        message["params"]?["uri"]?.GetValue<string>() is { } published && string.Equals(Uri.UnescapeDataString(published), Uri.UnescapeDataString(uri), StringComparison.OrdinalIgnoreCase) &&
                        (message["params"]?["version"] is null || message["params"]!["version"]!.GetValue<int>() == version))
                    {
                        return (message["params"]!["diagnostics"] as JsonArray ?? new JsonArray())
                            .Select(d => (d!["severity"]?.GetValue<int>() ?? 1) + ":" + d["message"]!.GetValue<string>())
                            .ToList();
                    }
                }

                Assert.Fail("no diagnostics for version " + version);
                return Array.Empty<string>();
            }

            private void Send(JsonObject message)
            {
                byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
                byte[] header = Encoding.ASCII.GetBytes("Content-Length: " + body.Length + "\r\n\r\n");
                var input = _process.StandardInput.BaseStream;
                input.Write(header, 0, header.Length);
                input.Write(body, 0, body.Length);
                input.Flush();
            }

            private JsonNode? Read()
            {
                int length = -1;
                while (true)
                {
                    string line = ReadHeaderLine();
                    if (line.Length == 0)
                    {
                        break;
                    }

                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        length = int.Parse(line.Substring(15).Trim(), System.Globalization.CultureInfo.InvariantCulture);
                    }
                }

                Assert.IsTrue(length >= 0, "a framed message");
                var buffer = new byte[length];
                int read = 0;
                while (read < length)
                {
                    int n = _output.Read(buffer, read, length - read);
                    Assert.IsTrue(n > 0, "the server closed its output");
                    read += n;
                }

                return JsonNode.Parse(Encoding.UTF8.GetString(buffer));
            }

            private string ReadHeaderLine()
            {
                var bytes = new List<byte>();
                while (true)
                {
                    int b = _output.ReadByte();
                    Assert.IsTrue(b >= 0, "the server closed its output");
                    if (b == '\n')
                    {
                        break;
                    }

                    if (b != '\r')
                    {
                        bytes.Add((byte)b);
                    }
                }

                return Encoding.ASCII.GetString(bytes.ToArray());
            }

            public void Dispose()
            {
                try
                {
                    _process.StandardInput.Close();
                    if (!_process.WaitForExit(5000))
                    {
                        _process.Kill();
                    }
                }
                catch (InvalidOperationException)
                {
                }

                _process.Dispose();
            }
        }
    }
}

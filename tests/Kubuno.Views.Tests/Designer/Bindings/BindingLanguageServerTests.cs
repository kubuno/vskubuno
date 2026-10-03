using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Kubuno.Views.Designer.Bindings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Kubuno.Views.Tests.Designer.Bindings
{
    /// <summary>
    /// The binding UI against the real <c>kubuno-views-ls</c> (docs/DESIGNER.md, "Data bindings"): the source schema of a form
    /// class read from its code-behind, the diagnostics of an unknown path, a pick of the picker and the dialog's design-time value
    /// written through <c>kubuno/applyEdit</c> (the comment and the other attributes kept), and the live sample. Inconclusive when
    /// the server is not built (<c>KUBUNO_VIEWS_LS</c>, else the bindings agent's or the shared release build).
    /// </summary>
    [TestClass]
    public sealed class BindingLanguageServerTests
    {
        private static readonly string[] Candidates =
        {
            @"C:\kubuno-build\agent-bind\target\release\kubuno-views-ls.exe",
            @"C:\kubuno-build\desktop-target\release\kubuno-views-ls.exe",
        };

        private const string View = "<!-- The main window. -->\n<Form x:Class=\"MainForm\" Text=\"{Binding Title}\" Width=\"400\" Height=\"300\">\n  <Label x:Name=\"status\" Text=\"{Binding Statut}\" X=\"8\" Y=\"8\" Width=\"200\" Height=\"24\"/>\n</Form>\n";

        private const string Code = "use kubuno_desktop::prelude::*;\n\n#[kubuno_desktop::view(\"main_form.kbview\")]\npub struct MainForm {\n    /// The window's title.\n    #[bind]\n    title: String,\n    #[bind]\n    status_text: String,\n    #[bind]\n    total: f64,\n}\n";

        [DllImport("kernel32.dll")]
        private static extern uint SetErrorMode(uint mode);

        [TestMethod]
        public void PickerAndDialogEdits_RoundTripThroughTheLanguageServer()
        {
            string? exe = Environment.GetEnvironmentVariable("KUBUNO_VIEWS_LS") is { Length: > 0 } configured ? configured : Candidates.FirstOrDefault(File.Exists);
            if (exe is null || !File.Exists(exe))
            {
                Assert.Inconclusive("kubuno-views-ls.exe not found");
            }

            string root = Path.Combine(Path.GetTempPath(), "kubuno-bind-ls-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllText(Path.Combine(root, "Cargo.toml"), "[package]\nname = \"bind_ls\"\nversion = \"0.1.0\"\nedition = \"2021\"\n");
            File.WriteAllText(Path.Combine(root, "src", "main_form.rs"), Code);
            string viewPath = Path.Combine(root, "src", "main_form.kbview");
            File.WriteAllText(viewPath, View);
            // A crash dialog of the child must never wait for a click (it inherits the error mode).
            SetErrorMode(0x0001 | 0x0002);
            try
            {
                using var server = LspProcess.Start(exe!);
                server.Request("initialize", new JsonObject { ["processId"] = Process.GetCurrentProcess().Id, ["rootUri"] = new Uri(root).AbsoluteUri, ["capabilities"] = new JsonObject() });
                server.Notify("initialized", new JsonObject());
                string uri = new Uri(viewPath).AbsoluteUri;
                server.Notify("textDocument/didOpen", new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["uri"] = uri, ["languageId"] = "kbview", ["version"] = 1, ["text"] = View },
                });
                var diagnostics = server.WaitDiagnostics(uri, 1);
                Assert.IsTrue(diagnostics.Any(d => d.StartsWith("2:", StringComparison.Ordinal) && d.Contains("`Statut`") && d.Contains("StatusText")), string.Join(" | ", diagnostics));

                // The schema of the label: the form class's #[bind] fields, typed, and the label's problem.
                var answer = server.Request("kubuno/bindingSources", new JsonObject { ["uri"] = uri, ["elementId"] = "0" });
                var schema = BindingSourceSchema.Parse(JToken.Parse(answer!.ToJsonString()));
                CollectionAssert.AreEqual(new[] { "Title", "StatusText", "Total" }, schema.Context.Members.Select(m => m.Path).ToArray());
                Assert.AreEqual(BindingShape.Number, schema.Context.Members[2].Shape);
                Assert.AreEqual("The window's title.", schema.Context.Members[0].Doc);
                Assert.IsTrue(schema.Context.Members[0].Location!.Uri.EndsWith("main_form.rs", StringComparison.OrdinalIgnoreCase));
                Assert.AreEqual("binding-unknown-path", schema.IssuesOf("Text").Single().Code);

                // A pick in the picker, then the dialog's design-time value: one batch of edits against the same text.
                string text = View;
                var picked = BindingPickerModel.Apply("{Binding Statut}", schema.Context.Members.Single(m => m.Path == "StatusText"));
                var model = new BindingEditModel("Text", picked, null) { DesignValue = "Prêt", StringFormat = "N0" };
                var edits = model.Edits().Select(e => new JsonObject { ["kind"] = "setAttribute", ["elementId"] = "0", ["name"] = e.Attribute, ["value"] = e.Value }).ToList();
                text = ApplyThroughServer(server, uri, text, edits, 2);
                Assert.AreEqual(
                    "<!-- The main window. -->\n<Form x:Class=\"MainForm\" Text=\"{Binding Title}\" Width=\"400\" Height=\"300\">\n  <Label x:Name=\"status\" Text=\"{Binding StatusText, StringFormat=N0}\" X=\"8\" Y=\"8\" Width=\"200\" Height=\"24\" d:Text=\"Prêt\"/>\n</Form>\n",
                    text,
                    "only the value changed and the design-time value was added: the comment, the order and the layout kept");
                var after = server.WaitDiagnostics(uri, 2);
                Assert.IsFalse(after.Any(d => d.Contains("is not a member")), string.Join(" | ", after));

                // The live sample of the dialog: the runtime's own format.
                var preview = server.Request("kubuno/bindingPreview", new JsonObject { ["expression"] = "{Binding Total, StringFormat=N2, Culture=en-US}", ["value"] = "1234.5", ["shape"] = "Number", ["want"] = "Text" });
                Assert.AreEqual("1,234.50", preview!["text"]!.GetValue<string>());

                // F12 from the Properties window: the field in the code-behind.
                var definition = server.Request("kubuno/bindingDefinition", new JsonObject { ["uri"] = uri, ["elementId"] = "0", ["attribute"] = "Text" });
                Assert.AreEqual(8, definition!["range"]!["start"]!["line"]!.GetValue<int>(), "the status_text field");
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

        private static string ApplyThroughServer(LspProcess server, string uri, string text, IReadOnlyList<JsonObject> ops, int version)
        {
            var edits = new List<(int Start, int End, string NewText)>();
            foreach (var op in ops)
            {
                var result = server.Request("kubuno/applyEdit", new JsonObject { ["uri"] = uri, ["op"] = op });
                foreach (var edit in (result?["edits"] as JsonArray)!)
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

        /// <summary>A minimal LSP client over the server's stdio (Content-Length framing).</summary>
        private sealed class LspProcess : IDisposable
        {
            private readonly Process _process;
            private readonly Stream _output;
            private readonly List<JsonNode?> _pending = new List<JsonNode?>();
            private readonly StringBuilder _stderr = new StringBuilder();
            private int _id;

            private LspProcess(Process process)
            {
                _process = process;
                _output = process.StandardOutput.BaseStream;
            }

            public static LspProcess Start(string exe)
            {
                // The child's stdin writer takes the console's input encoding: a UTF-8 one with a preamble would send a BOM first.
                try
                {
                    Console.InputEncoding = new UTF8Encoding(false);
                }
                catch (IOException)
                {
                }

                var info = new ProcessStartInfo(exe)
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                string toolchain = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".rustup\toolchains\stable-x86_64-pc-windows-msvc\lib\rustlib\x86_64-pc-windows-msvc\lib");
                info.EnvironmentVariables["PATH"] = Path.GetDirectoryName(exe) + ";" + Path.Combine(Path.GetDirectoryName(exe)!, "deps") + ";" + toolchain + ";" + Environment.GetEnvironmentVariable("PATH");
                var process = Process.Start(info)!;
                var lsp = new LspProcess(process);
                process.ErrorDataReceived += (_, e) =>
                {
                    lock (lsp._stderr)
                    {
                        lsp._stderr.AppendLine(e.Data);
                    }
                };
                process.BeginErrorReadLine();
                return lsp;
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
                    Assert.IsTrue(b >= 0, "the server closed its output: " + _stderr);
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

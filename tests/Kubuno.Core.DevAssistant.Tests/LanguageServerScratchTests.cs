using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Core.DevAssistant.Tests
{
    /// <summary>
    /// The recorded <c>/vue</c> ops against the REAL <c>kubuno-views-ls</c> (docs/AI-ASSISTANT.md section 6.2): opened on a
    /// scratch URI (never on disk), each op computed by <c>kubuno/applyEdit</c> in order, then the server's own diagnostics.
    /// Inconclusive when the server is not built on this machine.
    /// </summary>
    [TestClass]
    public sealed class LanguageServerScratchTests
    {
        [TestMethod]
        public async Task Recorded_vue_ops_are_valid_for_the_real_language_server()
        {
            var exe = Environment.GetEnvironmentVariable("KUBUNO_VIEWS_LS") ?? @"C:\kubuno-build\desktop-target\release\kubuno-views-ls.exe";
            if (!File.Exists(exe))
            {
                Assert.Inconclusive("kubuno-views-ls.exe is not built here: " + exe);
            }

            exe = Stage(exe);
            var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "vue.json")))!;
            var ops = fixture["rounds"]![1]!["events"]!.AsArray().Single(e => (string?)e!["type"] == "tool_use")!["input"]!["ops"]!.AsArray();

            using var server = LspProcess.Start(exe);
            await server.RequestAsync("initialize", new JsonObject { ["processId"] = Environment.ProcessId, ["rootUri"] = null, ["capabilities"] = new JsonObject() });
            server.Notify("initialized", new JsonObject());

            var uri = new Uri(Path.Combine(Path.GetTempPath(), ".kubuno-assist", Guid.NewGuid().ToString("N"), "main_view.kbview")).AbsoluteUri;
            var text = HostFlowTests.SampleView;
            server.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["languageId"] = "kbview", ["version"] = 1, ["text"] = text } });
            var baseline = await server.NextDiagnosticsAsync(uri);

            int version = 1;
            foreach (var op in ops)
            {
                var result = await server.RequestAsync("kubuno/applyEdit", new JsonObject { ["uri"] = uri, ["op"] = op!.DeepClone() });
                var edits = result?["edits"]?.AsArray();
                Assert.IsNotNull(edits);
                Assert.IsTrue(edits!.Count > 0, "op had no effect: " + op.ToJsonString());
                foreach (var edit in edits.OrderByDescending(e => Offset(text, e!["range"]!["start"]!)))
                {
                    int start = Offset(text, edit!["range"]!["start"]!);
                    int end = Offset(text, edit["range"]!["end"]!);
                    text = text.Substring(0, start) + (string?)edit["newText"] + text.Substring(end);
                }

                server.Notify("textDocument/didChange", new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = ++version },
                    ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = text }),
                });
            }

            var final = await server.NextDiagnosticsAsync(uri);
            server.Notify("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } });

            var before = baseline.Where(d => d.Severity == 1).Select(d => d.Message).ToHashSet();
            var newErrors = final.Where(d => d.Severity == 1 && !before.Contains(d.Message)).ToList();
            Assert.AreEqual(0, newErrors.Count, "new errors: " + string.Join("; ", newErrors.Select(d => d.Message)) + "\n" + text);
            StringAssert.Contains(text, "notifications_label");
            StringAssert.Contains(text, "<Switch x:Name=\"notifications\"");
            KbviewOpsForTests.AssertWellFormed(text);
            Assert.AreEqual(2, Logic.Changes.LineDiff.Compute(HostFlowTests.SampleView, text).Count, "two separate hunks to review\n" + text);
        }

        /// <summary>
        /// Copies the server with the DLLs it imports (kubuno_ui is a Rust dylib, std-*.dll comes from the toolchain) into
        /// a private folder, as the VSIX ships it in tools\.
        /// </summary>
        private static string Stage(string exe)
        {
            var folder = Path.Combine(Path.GetTempPath(), "kubuno-assist-ls", File.GetLastWriteTimeUtc(exe).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            var staged = Path.Combine(folder, Path.GetFileName(exe));
            File.Copy(exe, staged, overwrite: true);
            var image = File.ReadAllText(exe, Encoding.Latin1);
            var stdDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".rustup\toolchains\stable-x86_64-pc-windows-msvc\lib\rustlib\x86_64-pc-windows-msvc\lib");
            foreach (var name in System.Text.RegularExpressions.Regex.Matches(image, @"(kubuno_ui(-[0-9a-f]{16})?|std-[0-9a-f]{16})\.dll").Select(m => m.Value).Distinct())
            {
                var source = new[] { Path.GetDirectoryName(exe)!, Path.Combine(Path.GetDirectoryName(exe)!, "deps"), stdDir }
                    .Select(dir => Path.Combine(dir, name)).FirstOrDefault(File.Exists);
                if (source is null)
                {
                    Assert.Inconclusive(name + " (imported by kubuno-views-ls.exe) was not found.");
                }

                File.Copy(source!, Path.Combine(folder, name), overwrite: true);
            }

            return staged;
        }

        private static int Offset(string text, JsonNode position)
        {
            int line = (int)position["line"]!;
            int character = (int)position["character"]!;
            int offset = 0;
            for (int i = 0; i < line; i++)
            {
                offset = text.IndexOf('\n', offset) + 1;
            }

            return offset + character;
        }

        private sealed record Diagnostic(int Severity, string Message);

        /// <summary>A language server over Content-Length framed JSON-RPC on stdio.</summary>
        private sealed class LspProcess : IDisposable
        {
            private readonly Process _process;
            private readonly Stream _input;
            private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
            private readonly ConcurrentDictionary<string, List<List<Diagnostic>>> _diagnostics = new(StringComparer.OrdinalIgnoreCase);
            private readonly ConcurrentDictionary<string, int> _consumed = new(StringComparer.OrdinalIgnoreCase);
            private readonly object _writeLock = new();
            private int _nextId;

            private LspProcess(Process process)
            {
                _process = process;
                _input = process.StandardInput.BaseStream;
                _ = Task.Run(ReadLoop);
            }

            public static LspProcess Start(string exe)
            {
                // A test-spawned process must never pop a Windows error dialog (missing DLL...).
                SetErrorMode(0x0001 | 0x0002);
                var start = new ProcessStartInfo(exe)
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exe)!,
                };
                var process = Process.Start(start)!;
                process.ErrorDataReceived += (_, _) => { };
                process.BeginErrorReadLine();
                return new LspProcess(process);
            }

            public async Task<JsonNode?> RequestAsync(string method, JsonNode parameters)
            {
                var id = Interlocked.Increment(ref _nextId);
                var completion = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending[id] = completion;
                Write(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
                return await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
            }

            public void Notify(string method, JsonNode parameters) =>
                Write(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });

            /// <summary>The latest diagnostics once at least one new publication arrived and the server went quiet.</summary>
            public async Task<List<Diagnostic>> NextDiagnosticsAsync(string uri)
            {
                var key = Normalize(uri);
                int seen = _consumed.GetValueOrDefault(key);
                var deadline = DateTime.UtcNow.AddSeconds(8);
                int lastCount = -1;
                var quietSince = DateTime.UtcNow;
                while (DateTime.UtcNow < deadline)
                {
                    var list = _diagnostics.GetValueOrDefault(key);
                    int count = list?.Count ?? 0;
                    if (count != lastCount)
                    {
                        lastCount = count;
                        quietSince = DateTime.UtcNow;
                    }
                    else if (count > seen && DateTime.UtcNow - quietSince > TimeSpan.FromMilliseconds(700))
                    {
                        _consumed[key] = count;
                        return list![count - 1];
                    }

                    await Task.Delay(100);
                }

                return new List<Diagnostic>();
            }

            private static string Normalize(string uri) => Uri.UnescapeDataString(uri).Replace('\\', '/');

            private void Write(JsonNode message)
            {
                var body = Encoding.UTF8.GetBytes(message.ToJsonString());
                var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
                lock (_writeLock)
                {
                    _input.Write(header);
                    _input.Write(body);
                    _input.Flush();
                }
            }

            private async Task ReadLoop()
            {
                var output = _process.StandardOutput.BaseStream;
                while (true)
                {
                    int length = -1;
                    var line = new StringBuilder();
                    while (true)
                    {
                        int b = output.ReadByte();
                        if (b < 0)
                        {
                            return;
                        }

                        if (b == '\n')
                        {
                            var header = line.ToString().TrimEnd('\r');
                            line.Clear();
                            if (header.Length == 0)
                            {
                                break;
                            }

                            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            {
                                length = int.Parse(header.Substring(15).Trim());
                            }
                        }
                        else
                        {
                            line.Append((char)b);
                        }
                    }

                    var body = new byte[length];
                    int read = 0;
                    while (read < length)
                    {
                        read += await output.ReadAsync(body.AsMemory(read, length - read));
                    }

                    var message = JsonNode.Parse(body)!;
                    if (message["id"] is { } id && message["method"] is null)
                    {
                        if (_pending.TryRemove((int)id, out var completion))
                        {
                            completion.TrySetResult(message["result"]);
                        }
                    }
                    else if (message["id"] is { } requestId)
                    {
                        Write(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = requestId.DeepClone(), ["result"] = null });
                    }
                    else if ((string?)message["method"] == "textDocument/publishDiagnostics")
                    {
                        var parameters = message["params"]!;
                        var key = Normalize((string)parameters["uri"]!);
                        var list = parameters["diagnostics"]!.AsArray().Select(d => new Diagnostic((int?)d!["severity"] ?? 1, (string?)d["message"] ?? string.Empty)).ToList();
                        var publications = _diagnostics.GetOrAdd(key, _ => new List<List<Diagnostic>>());
                        lock (publications)
                        {
                            publications.Add(list);
                        }
                    }
                }
            }

            public void Dispose()
            {
                try
                {
                    _input.Close();
                    if (!_process.WaitForExit(2000))
                    {
                        _process.Kill();
                    }
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException)
                {
                }

                _process.Dispose();
            }

            [DllImport("kernel32.dll")]
            private static extern uint SetErrorMode(uint mode);
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Kubuno.Desktop.Designer.Editing;
using Kubuno.Desktop.Designer.Registry;
using Kubuno.Desktop.Designer.Registry.Infrastructure;
using Kubuno.Desktop.Views.LanguageService;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.Desktop.DevAssistant
{
    /// <summary>
    /// The Dev Assistant's access to <c>kubuno-views-ls</c> (docs/AI-ASSISTANT.md section 6.2): the live element
    /// registry, element ranges, and the validation of structured edits on a SCRATCH document - a URI under a virtual
    /// <c>.kubuno-assist\</c> folder next to the view that is opened in the language server only (never on disk, never in
    /// Visual Studio). Every <c>kubuno/applyEdit</c> op is computed by the server on that scratch copy, then the server's
    /// own diagnostics (captured by <see cref="HoverSuppressionMiddleLayer.DiagnosticsObserver"/> and kept out of the Error
    /// List) and a well-formedness and registry check decide whether the result may be proposed.
    /// </summary>
    internal static class KbviewLanguageServerBridge
    {
        private const string ScratchFolder = ".kubuno-assist";
        private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan DiagnosticsTimeout = TimeSpan.FromSeconds(4);
        private static readonly ConcurrentDictionary<string, DiagnosticsWaiter> Waiters = new ConcurrentDictionary<string, DiagnosticsWaiter>(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim RegistryLock = new SemaphoreSlim(1, 1);
        private static ComponentRegistry? _registry;
        private static int _hooked;

        /// <summary>The ready language server connection, starting the server when needed (null when it cannot run).</summary>
        public static async Task<JsonRpc?> GetRpcAsync(CancellationToken cancellationToken)
        {
            HookDiagnostics();
            var client = KubunoViewsLanguageClient.Current;
            if (client is null)
            {
                return null;
            }

            if (client.ReadyRpc is { } ready)
            {
                return ready;
            }

            await client.EnsureStartedAsync().ConfigureAwait(false);
            for (int i = 0; i < 50 && client.ReadyRpc is null; i++)
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }

            return client.ReadyRpc;
        }

        public static async Task<JToken?> InvokeAsync(JsonRpc rpc, string method, object parameters, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CallTimeout);
            return await rpc.InvokeWithParameterObjectAsync<JToken?>(method, parameters, timeout.Token).ConfigureAwait(false);
        }

        /// <summary>The live registry (cached until its version changes).</summary>
        public static async Task<ComponentRegistry?> GetRegistryAsync(CancellationToken cancellationToken)
        {
            var rpc = await GetRpcAsync(cancellationToken).ConfigureAwait(false);
            if (rpc is null)
            {
                return null;
            }

            await RegistryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var version = await JsonRpcRegistryClient.FetchVersionAsync(rpc, cancellationToken).ConfigureAwait(false);
                if (_registry is null || _registry.Components.Count == 0 || (version is not null && version != _registry.Version))
                {
                    _registry = await JsonRpcRegistryClient.FetchAsync(rpc, cancellationToken).ConfigureAwait(false);
                }

                return _registry;
            }
            finally
            {
                RegistryLock.Release();
            }
        }

        /// <summary>The offsets of element <paramref name="elementId"/> in <paramref name="text"/> (resolved by the server on a scratch copy).</summary>
        public static async Task<(int Start, int End)?> RangeOfElementAsync(string viewPath, string text, string elementId, CancellationToken cancellationToken)
        {
            var rpc = await GetRpcAsync(cancellationToken).ConfigureAwait(false);
            if (rpc is null)
            {
                return null;
            }

            var uri = ScratchUri(viewPath);
            await OpenAsync(rpc, uri, text).ConfigureAwait(false);
            try
            {
                var result = await InvokeAsync(rpc, "kubuno/rangeOfElement", new { uri, elementId }, cancellationToken).ConfigureAwait(false);
                var range = Designer.Selection.SelectionResponseParser.ParseRangeOfElement(result?.ToString(Newtonsoft.Json.Formatting.None));
                return range is { } r ? LspPositionMapper.ToOffsetRange(text, r) : null;
            }
            finally
            {
                await CloseAsync(rpc, uri).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Applies <paramref name="ops"/> (kubuno/applyEdit ops, in order) to <paramref name="text"/> on a scratch copy and
        /// validates the result. Returns the new text, or the errors to send back to the model.
        /// </summary>
        public static async Task<ScratchResult> ApplyOpsAsync(string viewPath, string text, IReadOnlyList<JObject> ops, CancellationToken cancellationToken)
        {
            var rpc = await GetRpcAsync(cancellationToken).ConfigureAwait(false);
            if (rpc is null)
            {
                return ScratchResult.Failure("The .kbview language server is not running: open the view in the editor first.");
            }

            var uri = ScratchUri(viewPath);
            var waiter = Waiters.GetOrAdd(Normalize(uri), _ => new DiagnosticsWaiter());
            int version = 1;
            await OpenAsync(rpc, uri, text).ConfigureAwait(false);
            try
            {
                var baseline = await waiter.NextAsync(DiagnosticsTimeout, cancellationToken).ConfigureAwait(false);
                var current = text;
                for (int i = 0; i < ops.Count; i++)
                {
                    var op = ops[i];
                    var result = await InvokeAsync(rpc, "kubuno/applyEdit", new { uri, op }, cancellationToken).ConfigureAwait(false);
                    var edits = result?["edits"] as JArray;
                    if (edits is null || edits.Count == 0)
                    {
                        return ScratchResult.Failure($"Op {i + 1} ({op["kind"]}) had no effect: its element id does not resolve in the current view (ids are child-ordinal paths, \"\" is the root; ids after an insert/remove/move at the same level shift).");
                    }

                    current = ApplyEdits(current, edits, (string?)op["kind"] == "insertChild");
                    version++;
                    waiter.Reset();
                    await rpc.NotifyWithParameterObjectAsync("textDocument/didChange", new
                    {
                        textDocument = new { uri, version },
                        contentChanges = new[] { new { text = current } },
                    }).ConfigureAwait(false);
                }

                var final = ops.Count == 0 ? baseline : await waiter.NextAsync(DiagnosticsTimeout, cancellationToken).ConfigureAwait(false);
                var problems = new List<string>();
                problems.AddRange(CheckWellFormed(current));
                if (await GetRegistryAsync(cancellationToken).ConfigureAwait(false) is { Components.Count: > 0 } registry)
                {
                    // Only elements the edit introduced: a view may already use controls this registry does not export
                    // (another crate's components), which is not the assistant's change to judge.
                    var existing = new HashSet<string>(CheckElementNames(text, registry));
                    problems.AddRange(CheckElementNames(current, registry).Where(p => !existing.Contains(p)));
                }

                if (final is not null)
                {
                    var before = new HashSet<string>(baseline?.Where(d => d.IsError).Select(d => d.Message) ?? Enumerable.Empty<string>());
                    problems.AddRange(final.Where(d => d.IsError && !before.Contains(d.Message)).Select(d => $"line {d.Line + 1}: {d.Message}"));
                }

                return problems.Count > 0
                    ? ScratchResult.Failure("The edited view is not valid:\n" + string.Join("\n", problems.Distinct()))
                    : new ScratchResult(current, null, final is null ? "the language server published no diagnostics in time; checked well-formedness and element names only" : "no new language-server error");
            }
            finally
            {
                await CloseAsync(rpc, uri).ConfigureAwait(false);
            }
        }

        /// <summary>Validates a whole view text (new views) on a scratch copy.</summary>
        public static Task<ScratchResult> ValidateAsync(string viewPath, string text, CancellationToken cancellationToken) =>
            ApplyOpsAsync(viewPath, text, Array.Empty<JObject>(), cancellationToken);

        private static string ApplyEdits(string text, JArray edits, bool insertChild)
        {
            var planned = new List<(int Start, int End, string NewText)>();
            foreach (var edit in edits.OfType<JObject>())
            {
                var range = edit["range"];
                var start = new LspPosition((int?)range?["start"]?["line"] ?? 0, (int?)range?["start"]?["character"] ?? 0);
                var end = new LspPosition((int?)range?["end"]?["line"] ?? 0, (int?)range?["end"]?["character"] ?? 0);
                var (s, e) = LspPositionMapper.ToOffsetRange(text, new LspRange(start, end));
                var newText = (string?)edit["newText"] ?? string.Empty;
                if (insertChild && s == e && edits.Count == 1)
                {
                    // The server inserts the fragment verbatim; give it its own indented line like the designer does.
                    newText = FormatInsertion(text, s, newText);
                }

                planned.Add((s, e, newText));
            }

            foreach (var (start, end, newText) in planned.OrderByDescending(p => p.Start))
            {
                text = text.Substring(0, start) + newText + text.Substring(end);
            }

            return text;
        }

        private static string FormatInsertion(string text, int offset, string fragment)
        {
            int lineStart = text.LastIndexOf('\n', Math.Max(0, offset - 1)) + 1;
            if (offset == 0)
            {
                lineStart = 0;
            }

            int lineEnd = text.IndexOf('\n', offset);
            lineEnd = lineEnd < 0 ? text.Length : lineEnd;
            var prefix = text.Substring(lineStart, offset - lineStart);
            var rest = text.Substring(offset, lineEnd - offset).TrimEnd('\r');
            string? previous = null;
            if (lineStart > 0)
            {
                var before = text.Substring(0, lineStart - 1).Replace("\r", string.Empty).Split('\n');
                previous = before.LastOrDefault(l => l.Trim().Length > 0);
            }

            var newLine = text.Contains("\r\n") ? "\r\n" : "\n";
            return InsertChildFormatter.Format(prefix, rest, previous, fragment, newLine);
        }

        private static IEnumerable<string> CheckWellFormed(string text)
        {
            try
            {
                // x:/d: prefixes need no xmlns declaration in a view (VIEWS-SPEC 3): parse without namespaces.
                using var reader = new XmlTextReader(new StringReader(text)) { Namespaces = false, DtdProcessing = DtdProcessing.Prohibit };
                while (reader.Read())
                {
                }
            }
            catch (XmlException exception)
            {
                return new[] { $"line {exception.LineNumber}: not well-formed XML ({exception.Message})" };
            }

            return Array.Empty<string>();
        }

        private static IEnumerable<string> CheckElementNames(string text, ComponentRegistry registry)
        {
            var unknown = new List<string>();
            try
            {
                using var reader = new XmlTextReader(new StringReader(text)) { Namespaces = false, DtdProcessing = DtdProcessing.Prohibit };
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.Name.Contains('.') || reader.Name.Contains(':'))
                    {
                        continue;
                    }

                    if (registry.Find(reader.Name) is null && !unknown.Contains(reader.Name))
                    {
                        unknown.Add(reader.Name);
                    }
                }
            }
            catch (XmlException)
            {
                return Array.Empty<string>();
            }

            return unknown.Select(name => $"<{name}> is not an element of the registry (call kbview_registry to list the elements)");
        }

        private static string ScratchUri(string viewPath)
        {
            var folder = Path.Combine(Path.GetDirectoryName(viewPath) ?? Path.GetTempPath(), ScratchFolder, Guid.NewGuid().ToString("N").Substring(0, 8));
            return new Uri(Path.Combine(folder, Path.GetFileName(viewPath))).AbsoluteUri;
        }

        private static Task OpenAsync(JsonRpc rpc, string uri, string text) =>
            rpc.NotifyWithParameterObjectAsync("textDocument/didOpen", new { textDocument = new { uri, languageId = "kbview", version = 1, text } });

        private static async Task CloseAsync(JsonRpc rpc, string uri)
        {
            try
            {
                await rpc.NotifyWithParameterObjectAsync("textDocument/didClose", new { textDocument = new { uri } }).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is ConnectionLostException or ObjectDisposedException)
            {
            }

            Waiters.TryRemove(Normalize(uri), out _);
        }

        private static string Normalize(string uri) => Uri.UnescapeDataString(uri).Replace('\\', '/').TrimEnd('/');

        private static void HookDiagnostics()
        {
            if (Interlocked.Exchange(ref _hooked, 1) == 1)
            {
                return;
            }

            var previous = HoverSuppressionMiddleLayer.DiagnosticsObserver;
            HoverSuppressionMiddleLayer.DiagnosticsObserver = parameters =>
            {
                var uri = (string?)parameters["uri"];
                if (uri is not null && Uri.UnescapeDataString(uri).Replace('\\', '/').IndexOf("/" + ScratchFolder + "/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (Waiters.TryGetValue(Normalize(uri), out var waiter))
                    {
                        waiter.Publish(((parameters["diagnostics"] as JArray) ?? new JArray()).OfType<JObject>().Select(d => new Diagnostic(
                            (int?)d["severity"] ?? 1,
                            (string?)d["message"] ?? string.Empty,
                            (int?)d["range"]?["start"]?["line"] ?? 0)).ToList());
                    }

                    return true; // A scratch document never reaches the Error List.
                }

                return previous?.Invoke(parameters) ?? false;
            };
        }

        internal sealed class Diagnostic
        {
            public Diagnostic(int severity, string message, int line)
            {
                Severity = severity;
                Message = message;
                Line = line;
            }

            public int Severity { get; }

            public string Message { get; }

            public int Line { get; }

            public bool IsError => Severity == 1;
        }

        /// <summary>Waits for the next diagnostics the server publishes for one scratch URI.</summary>
        private sealed class DiagnosticsWaiter
        {
            private readonly object _gate = new object();
            private TaskCompletionSource<IReadOnlyList<Diagnostic>> _next = new TaskCompletionSource<IReadOnlyList<Diagnostic>>(TaskCreationOptions.RunContinuationsAsynchronously);

            public void Publish(IReadOnlyList<Diagnostic> diagnostics)
            {
                lock (_gate)
                {
                    _next.TrySetResult(diagnostics);
                }
            }

            public void Reset()
            {
                lock (_gate)
                {
                    _next = new TaskCompletionSource<IReadOnlyList<Diagnostic>>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            /// <summary>The next publication, or null after <paramref name="timeout"/>.</summary>
            public async Task<IReadOnlyList<Diagnostic>?> NextAsync(TimeSpan timeout, CancellationToken cancellationToken)
            {
                Task<IReadOnlyList<Diagnostic>> next;
                lock (_gate)
                {
                    next = _next.Task;
                }

                var winner = await Task.WhenAny(next, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
                return winner == next ? await next.ConfigureAwait(false) : null;
            }
        }
    }

    /// <summary>The outcome of a scratch apply or validation.</summary>
    internal sealed class ScratchResult
    {
        public ScratchResult(string? text, string? error, string? note)
        {
            Text = text;
            Error = error;
            Note = note;
        }

        public string? Text { get; }

        public string? Error { get; }

        public string? Note { get; }

        public bool Succeeded => Text is not null;

        public static ScratchResult Failure(string error) => new ScratchResult(null, error, null);
    }
}

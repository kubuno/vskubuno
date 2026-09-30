using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.Rust.LanguageService.IntelliSense
{
    /// <summary>
    /// Requests Kubuno's own editor features (completion, code lenses) send to rust-analyzer over the client's
    /// <see cref="JsonRpc"/>. That connection is the one Visual Studio sends <c>didChange</c> on, but Visual Studio
    /// hands its notifications to it asynchronously: a request is only sent once rust-analyzer has been given the
    /// text the request is about (<see cref="RustDocumentVersions"/>, fed by <see cref="RustAnalyzerMiddleLayer"/>),
    /// and the answer's positions are read against the snapshot rust-analyzer had.
    /// </summary>
    public static class RustLsp
    {
        private static readonly TimeSpan SyncTimeout = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Sends <paramref name="method"/> about <paramref name="snapshot"/>'s document (at <paramref name="path"/>) once
        /// rust-analyzer has that text; <paramref name="parameters"/> builds the parameters for the snapshot the server
        /// has, reported back with the result (null result: rust-analyzer is not running).
        /// </summary>
        public static async Task<(JToken? Result, ITextSnapshot Snapshot)> RequestAsync(ITextSnapshot snapshot, string path, string method, Func<ITextSnapshot, JObject> parameters, CancellationToken cancellationToken)
        {
            var client = RustLanguageClient.Instance;
            if (client?.Rpc is not { } rpc)
            {
                return (null, snapshot);
            }

            var versions = client.DocumentVersions;
            await versions.WaitForAsync(path, snapshot.Version.VersionNumber, SyncTimeout, cancellationToken).ConfigureAwait(false);

            // The server may already have a newer text than the request's: use that snapshot when it is the current one.
            var serverSnapshot = snapshot;
            var current = snapshot.TextBuffer.CurrentSnapshot;
            if (current.Version.VersionNumber != snapshot.Version.VersionNumber && versions.Version(path) == current.Version.VersionNumber)
            {
                serverSnapshot = current;
            }

            var result = await rpc.InvokeWithParameterObjectAsync<JToken?>(method, parameters(serverSnapshot), cancellationToken).ConfigureAwait(false);
            return (result, serverSnapshot);
        }

        public static JObject Position(SnapshotPoint point)
        {
            var line = point.GetContainingLine();
            return new JObject { ["line"] = line.LineNumber, ["character"] = point.Position - line.Start.Position };
        }

        public static JObject TextDocument(string path) => new JObject { ["uri"] = new Uri(path).AbsoluteUri };

        /// <summary>The offset of an LSP position (UTF-16 line/character) in <paramref name="snapshot"/>, clamped; null when malformed.</summary>
        public static int? Offset(ITextSnapshot snapshot, JToken? position)
        {
            if (position is not JObject p || p["line"]?.Type != JTokenType.Integer || p["character"]?.Type != JTokenType.Integer)
            {
                return null;
            }

            int lineNumber = (int)p["line"]!;
            if (lineNumber < 0)
            {
                return 0;
            }

            if (lineNumber >= snapshot.LineCount)
            {
                return snapshot.Length;
            }

            var line = snapshot.GetLineFromLineNumber(lineNumber);
            return line.Start.Position + Math.Max(0, Math.Min((int)p["character"]!, line.Length));
        }

        /// <summary>The span of an LSP range in <paramref name="snapshot"/>, or null when malformed.</summary>
        public static SnapshotSpan? Span(ITextSnapshot snapshot, JToken? range)
        {
            var start = Offset(snapshot, range?["start"]);
            var end = Offset(snapshot, range?["end"]);
            if (start is null || end is null || end < start)
            {
                return null;
            }

            return new SnapshotSpan(snapshot, start.Value, end.Value - start.Value);
        }
    }

    /// <summary>
    /// The text version of each document rust-analyzer has been sent (<c>didOpen</c>/<c>didChange</c> carry the editor
    /// snapshot's version number), so a request about a given snapshot can wait until the server has it.
    /// </summary>
    internal sealed class RustDocumentVersions
    {
        private readonly Dictionary<string, int> _versions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string Key, int Version, TaskCompletionSource<bool> Done)> _waiters = new List<(string, int, TaskCompletionSource<bool>)>();

        /// <summary>Records what a <c>didOpen</c>/<c>didChange</c>/<c>didClose</c> just handed to the connection.</summary>
        public void OnNotificationSent(string method, JToken parameters)
        {
            var uri = (string?)parameters["textDocument"]?["uri"];
            if (uri is null || Key(uri) is not { } key)
            {
                return;
            }

            var done = new List<TaskCompletionSource<bool>>();
            lock (_versions)
            {
                if (method == "textDocument/didClose")
                {
                    _versions.Remove(key);
                    return;
                }

                if (parameters["textDocument"]?["version"]?.Type != JTokenType.Integer)
                {
                    return;
                }

                int version = (int)parameters["textDocument"]!["version"]!;
                _versions[key] = version;
                for (int i = _waiters.Count - 1; i >= 0; i--)
                {
                    if (_waiters[i].Key == key && _waiters[i].Version <= version)
                    {
                        done.Add(_waiters[i].Done);
                        _waiters.RemoveAt(i);
                    }
                }
            }

            foreach (var waiter in done)
            {
                waiter.TrySetResult(true);
            }
        }

        public int? Version(string path)
        {
            lock (_versions)
            {
                return Key(path) is { } key && _versions.TryGetValue(key, out var version) ? version : (int?)null;
            }
        }

        /// <summary>Completes once the server has version <paramref name="version"/> (or later) of the document, or after <paramref name="timeout"/>.</summary>
        public async Task WaitForAsync(string path, int version, TimeSpan timeout, CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool> waiter;
            lock (_versions)
            {
                var key = Key(path);
                if (key is null || (_versions.TryGetValue(key, out var known) && known >= version))
                {
                    return;
                }

                waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((key, version, waiter));
            }

            using (cancellationToken.Register(() => waiter.TrySetCanceled()))
            {
                await Task.WhenAny(waiter.Task, Task.Delay(timeout, CancellationToken.None)).ConfigureAwait(false);
            }

            lock (_versions)
            {
                _waiters.RemoveAll(w => w.Done == waiter);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>A path or <c>file:</c> URI as a comparable key (Visual Studio and Kubuno spell URIs differently).</summary>
        private static string? Key(string pathOrUri)
        {
            try
            {
                var path = pathOrUri.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(Uri.UnescapeDataString(pathOrUri)).LocalPath : pathOrUri;
                return System.IO.Path.GetFullPath(path).ToUpperInvariant();
            }
            catch (Exception exception) when (exception is UriFormatException or ArgumentException or NotSupportedException or System.IO.PathTooLongException)
            {
                return null;
            }
        }
    }
}

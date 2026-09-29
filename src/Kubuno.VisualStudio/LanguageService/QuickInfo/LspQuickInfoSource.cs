using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Core.QuickInfo;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.VisualStudio.LanguageService.QuickInfo
{
    /// <summary>
    /// A QuickInfo source that asks a language server for <c>textDocument/hover</c> itself (over the
    /// client's own <see cref="JsonRpc"/>, which bypasses the middle layer) and shows the answer as Visual
    /// Studio tooltip elements. The language clients' middle layers answer Visual Studio's own hover
    /// request with nothing, so the markdown tooltip the LSP client would draw never appears next to it.
    /// Visual Studio's LSP client cannot render rich content from a middle layer: its
    /// <c>_vs_rawContent</c> extension is lost when a middle-layer response is re-deserialized, and
    /// cannot carry clickable links anyway.
    /// </summary>
    internal sealed class LspQuickInfoSource : IAsyncQuickInfoSource
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        private readonly ITextBuffer _buffer;
        private readonly Func<JsonRpc?> _rpc;
        private readonly Func<string, CancellationToken, Task<QuickInfoElement?>> _convert;
        private readonly QuickInfoElementFactory _factory;
        private readonly Func<string?> _filePath;
        private readonly Action<string> _log;

        public LspQuickInfoSource(ITextBuffer buffer, Func<string?> filePath, Func<JsonRpc?> rpc, Func<string, CancellationToken, Task<QuickInfoElement?>> convert, QuickInfoElementFactory factory, Action<string> log)
        {
            _buffer = buffer;
            _filePath = filePath;
            _rpc = rpc;
            _convert = convert;
            _factory = factory;
            _log = log;
        }

        public async Task<QuickInfoItem?> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
        {
            var snapshot = _buffer.CurrentSnapshot;
            var trigger = session.GetTriggerPoint(snapshot);
            var rpc = _rpc();
            var path = _filePath();
            if (trigger is null || rpc is null || string.IsNullOrEmpty(path))
            {
                return null;
            }

            var point = trigger.Value;
            var line = point.GetContainingLine();
            var request = new JObject
            {
                ["textDocument"] = new JObject { ["uri"] = new Uri(path).AbsoluteUri },
                ["position"] = new JObject { ["line"] = line.LineNumber, ["character"] = point.Position - line.Start.Position },
            };

            JToken? result;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(Timeout);
                try
                {
                    result = await rpc.InvokeWithParameterObjectAsync<JToken?>("textDocument/hover", request, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (Exception exception) when (exception is RemoteInvocationException or ConnectionLostException or ObjectDisposedException)
                {
                    _log("QuickInfo: textDocument/hover failed: " + exception.Message);
                    return null;
                }
            }

            var markdown = HoverText(result?["contents"]);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return null;
            }

            QuickInfoElement? element;
            try
            {
                element = await _convert(markdown!, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                _log("QuickInfo: could not convert the hover: " + exception);
                return null;
            }

            if (element is null)
            {
                return null;
            }

            var span = ApplicableSpan(snapshot, point, result?["range"]);
            return new QuickInfoItem(snapshot.CreateTrackingSpan(span, SpanTrackingMode.EdgeInclusive), _factory.Create(element));
        }

        /// <summary>The markdown of a hover's <c>contents</c> (MarkupContent, MarkedString, string, or an array of them).</summary>
        internal static string? HoverText(JToken? contents)
        {
            switch (contents)
            {
                case null:
                    return null;
                case JValue scalar:
                    return scalar.Value<string>();
                case JArray array:
                    return string.Join("\n\n---\n\n", array.Select(HoverText).Where(t => !string.IsNullOrWhiteSpace(t)));
                case JObject markup when markup["kind"] != null:
                    return (string?)markup["value"];
                case JObject marked:
                    var language = (string?)marked["language"];
                    var value = (string?)marked["value"];
                    return language is null ? value : "```" + language + "\n" + value + "\n```";
                default:
                    return null;
            }
        }

        private static SnapshotSpan ApplicableSpan(ITextSnapshot snapshot, SnapshotPoint point, JToken? range)
        {
            if (range is JObject r && TryPosition(snapshot, r["start"], out int start) && TryPosition(snapshot, r["end"], out int end) && end >= start && start <= point.Position && point.Position <= end)
            {
                return new SnapshotSpan(snapshot, start, end - start);
            }

            // The identifier under the mouse.
            int from = point.Position;
            int to = point.Position;
            while (from > 0 && IsWordChar(snapshot[from - 1]))
            {
                from--;
            }

            while (to < snapshot.Length && IsWordChar(snapshot[to]))
            {
                to++;
            }

            return new SnapshotSpan(snapshot, from, Math.Max(0, to - from));
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        private static bool TryPosition(ITextSnapshot snapshot, JToken? position, out int offset)
        {
            offset = 0;
            if (position is not JObject p || p["line"]?.Type != JTokenType.Integer || p["character"]?.Type != JTokenType.Integer)
            {
                return false;
            }

            int lineNumber = (int)p["line"]!;
            if (lineNumber < 0 || lineNumber >= snapshot.LineCount)
            {
                return false;
            }

            var line = snapshot.GetLineFromLineNumber(lineNumber);
            offset = line.Start.Position + Math.Min((int)p["character"]!, line.Length);
            return true;
        }

        public void Dispose()
        {
        }
    }
}

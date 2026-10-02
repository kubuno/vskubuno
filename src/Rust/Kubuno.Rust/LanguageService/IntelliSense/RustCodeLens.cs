using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using EnvDTE;
using Kubuno.Rust.Logic.IntelliSense;
using Kubuno.Shared.Logging;
using Kubuno.Shared.Logic.Localization;
using Kubuno.Rust.Options;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.Rust.LanguageService.IntelliSense
{
    /// <summary>
    /// C#-style CodeLens for Rust: "3 references | 1 implementation" above functions, types, traits and variants,
    /// from rust-analyzer's own code lenses (<c>textDocument/codeLens</c> + <c>codeLens/resolve</c>; Visual Studio's
    /// LSP client does not show LSP code lenses). The counts are drawn in extra space above the line (a line
    /// transform, the way Visual Studio's CodeLens makes room); clicking one runs Find All References or Go To
    /// Implementation on the item.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [Export(typeof(ILineTransformSourceProvider))]
    [ContentType(Constants.RustContentType)]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class RustCodeLensProvider : IWpfTextViewCreationListener, ILineTransformSourceProvider
    {
        public const string LayerName = "KubunoRustCodeLens";

        [Export(typeof(AdornmentLayerDefinition))]
        [Name(LayerName)]
        [Order(After = PredefinedAdornmentLayers.Text)]
        internal static AdornmentLayerDefinition LayerDefinition = null!;

        [Import]
        internal ITextDocumentFactoryService TextDocumentFactory { get; set; } = null!;

        [Import]
        internal IEditorFormatMapService FormatMapService { get; set; } = null!;

        public void TextViewCreated(IWpfTextView textView) => Get(textView);

        public ILineTransformSource Create(IWpfTextView textView) => Get(textView);

        private RustCodeLensManager Get(IWpfTextView view) =>
            view.Properties.GetOrCreateSingletonProperty(() => new RustCodeLensManager(view, this));
    }

    internal sealed class RustCodeLensManager : ILineTransformSource
    {
        private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(1500);

        /// <summary>How many items get their counts per refresh (the ones nearest the visible lines).</summary>
        private const int MaxResolved = 120;

        private readonly IWpfTextView _view;
        private readonly RustCodeLensProvider _provider;
        private readonly IAdornmentLayer _layer;
        private Dictionary<int, LineLens> _lenses = new Dictionary<int, LineLens>();
        private CancellationTokenSource? _refresh;
        private bool _enabled;
        private int _loggedOnce;
        private int _retries;
        private int _refreshedVersion = -1;
        private HashSet<int> _pending = new HashSet<int>();
        private bool _pendingRefresh;
        private DateTime _refreshedAt;

        public RustCodeLensManager(IWpfTextView view, RustCodeLensProvider provider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _view = view;
            _provider = provider;
            _layer = view.GetAdornmentLayer(RustCodeLensProvider.LayerName);
            _enabled = RustInlayHintsOption.Options()?.CodeLens ?? true;
            view.LayoutChanged += OnLayoutChanged;
            view.TextBuffer.Changed += OnBufferChanged;
            view.Closed += OnClosed;
            RustAnalyzerMiddleLayer.SemanticTokensRequested += OnAnalysisMaybeChanged;
            RustOptionsPage.Applied += OnOptionsApplied;
            ScheduleRefresh(TimeSpan.FromMilliseconds(500));
        }

        /// <summary>The counts of one line, and where the item they are about starts (for the click).</summary>
        private sealed class LineLens
        {
            public LineLens(ITrackingPoint target, IReadOnlyList<CodeLensCount> counts)
            {
                Target = target;
                Counts = counts;
            }

            public ITrackingPoint Target { get; }

            public IReadOnlyList<CodeLensCount> Counts { get; }
        }

        public LineTransform GetLineTransform(ITextViewLine line, double yPosition, ViewRelativePosition placement)
        {
            if (_enabled && _lenses.Count > 0)
            {
                int number = line.Start.GetContainingLine().LineNumber;
                if (line.Start == line.Start.GetContainingLine().Start && _lenses.ContainsKey(number))
                {
                    return new LineTransform(LensHeight(), 0, 1.0);
                }
            }

            return new LineTransform(0, 0, 1.0);
        }

        private void OnOptionsApplied(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            bool enabled = RustInlayHintsOption.Options()?.CodeLens ?? true;
            if (enabled == _enabled)
            {
                return;
            }

            _enabled = enabled;
            if (!enabled)
            {
                _lenses = new Dictionary<int, LineLens>();
                Relayout();
            }
            else
            {
                ScheduleRefresh(TimeSpan.Zero);
            }
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e) => ScheduleRefresh(Debounce);

        /// <summary>
        /// Semantic tokens asked for an unchanged text: rust-analyzer refreshed its analysis (indexing over, cargo check done),
        /// so the counts may have changed too - they are 0 while it is still indexing.
        /// </summary>
        private void OnAnalysisMaybeChanged(string uri)
        {
            var version = _view.TextBuffer.CurrentSnapshot.Version.VersionNumber;
            if (version != _refreshedVersion || DateTime.UtcNow - _refreshedAt < TimeSpan.FromSeconds(5) || !_provider.TextDocumentFactory.TryGetTextDocument(_view.TextBuffer, out var document))
            {
                return;
            }

            try
            {
                if (string.Equals(new Uri(Uri.UnescapeDataString(uri)).LocalPath, System.IO.Path.GetFullPath(document.FilePath), StringComparison.OrdinalIgnoreCase))
                {
                    _refreshedAt = DateTime.UtcNow;
                    ScheduleRefresh(TimeSpan.FromSeconds(1));
                }
            }
            catch (Exception exception) when (exception is UriFormatException or ArgumentException)
            {
            }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _view.LayoutChanged -= OnLayoutChanged;
            _view.TextBuffer.Changed -= OnBufferChanged;
            _view.Closed -= OnClosed;
            RustAnalyzerMiddleLayer.SemanticTokensRequested -= OnAnalysisMaybeChanged;
            RustOptionsPage.Applied -= OnOptionsApplied;
            _refresh?.Cancel();
        }

        private void ScheduleRefresh(TimeSpan delay)
        {
            if (!_enabled)
            {
                return;
            }

            _refresh?.Cancel();
            var cancellation = new CancellationTokenSource();
            _refresh = cancellation;
#pragma warning disable VSSDK007 // deliberately fire-and-forget (a debounced refresh); FileAndForget reports any fault instead of dropping it silently.
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await Task.Delay(delay, cancellation.Token).ConfigureAwait(false);
                    await RefreshAsync(cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    KubunoLog.WriteException("CodeLens: refresh failed", exception);
                }
            }).FileAndForget("Kubuno/RustCodeLens/Refresh");
#pragma warning restore VSSDK007
        }

        private async Task RefreshAsync(CancellationToken token)
        {
            if (RustLanguageClient.Instance?.Rpc is not { } rpc || !_provider.TextDocumentFactory.TryGetTextDocument(_view.TextBuffer, out var document))
            {
                // rust-analyzer not running yet: try again later.
                ScheduleRefresh(TimeSpan.FromSeconds(3));
                return;
            }

            JToken? result;
            ITextSnapshot snapshot;
            try
            {
                (result, snapshot) = await RustLsp.RequestAsync(
                    _view.TextBuffer.CurrentSnapshot,
                    document.FilePath,
                    "textDocument/codeLens",
                    _ => new JObject { ["textDocument"] = RustLsp.TextDocument(document.FilePath) },
                    token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is RemoteInvocationException or ConnectionLostException or ObjectDisposedException or InvalidOperationException)
            {
                KubunoLog.WriteLine("CodeLens: textDocument/codeLens failed: " + exception.Message);
                return;
            }

            if (result is not JArray lenses)
            {
                // null: rust-analyzer has not loaded the workspace yet (the file is in no crate so far) - ask again shortly.
                if (_retries++ < 40)
                {
                    ScheduleRefresh(TimeSpan.FromSeconds(3));
                }

                return;
            }

            _retries = 0;

            // Counting references is a search per item: in a long file, only the items around the visible part are
            // resolved, the others when they are scrolled into view (OnLayoutChanged).
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
            int center = _view.TextViewLines is { Count: > 0 } visible
                ? visible.FirstVisibleLine.Start.TranslateTo(snapshot, PointTrackingMode.Negative).GetContainingLine().LineNumber + (visible.Count / 2)
                : 0;
            await TaskScheduler.Default;
            var all = lenses.OfType<JObject>()
                .Select(lens => (Lens: lens, Line: RustLsp.Offset(snapshot, lens["range"]?["start"]) is int offset ? snapshot.GetLineNumberFromPosition(offset) : -1))
                .Where(l => l.Line >= 0)
                .OrderBy(l => Math.Abs(l.Line - center))
                .ToList();
            var toResolve = all.Take(MaxResolved).Select(l => l.Lens).ToList();
            var unresolvedLines = all.Skip(MaxResolved).Select(l => l.Line).ToList();

            var byLine = new Dictionary<int, (int Offset, List<CodeLensCount> Counts)>();
            using (var throttle = new SemaphoreSlim(4))
            {
                var tasks = toResolve.Select(async lens =>
                {
                    await throttle.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        var resolved = lens["command"] is null
                            ? await rpc.InvokeWithParameterObjectAsync<JToken?>("codeLens/resolve", lens, token).ConfigureAwait(false) as JObject
                            : lens;
                        return (Lens: lens, Resolved: resolved);
                    }
                    catch (Exception exception) when (exception is RemoteInvocationException or ConnectionLostException or ObjectDisposedException)
                    {
                        return (Lens: lens, Resolved: (JObject?)null);
                    }
                    finally
                    {
                        throttle.Release();
                    }
                }).ToList();

                foreach (var (lens, resolved) in await Task.WhenAll(tasks).ConfigureAwait(false))
                {
                    var command = resolved?["command"];
                    var locations = command?["arguments"] is JArray { Count: >= 3 } arguments && arguments[2] is JArray found ? found.Count : (int?)null;
                    var count = CodeLensText.Parse((string?)command?["title"], locations);
                    var start = RustLsp.Offset(snapshot, lens["range"]?["start"]);
                    if (count is null || start is null)
                    {
                        continue;
                    }

                    int line = snapshot.GetLineNumberFromPosition(start.Value);
                    if (!byLine.TryGetValue(line, out var entry))
                    {
                        entry = (start.Value, new List<CodeLensCount>());
                        byLine[line] = entry;
                    }

                    if (!entry.Counts.Any(c => c.Kind == count.Kind))
                    {
                        entry.Counts.Add(count);
                    }
                }
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
            if (_view.IsClosed)
            {
                return;
            }

            var current = _view.TextBuffer.CurrentSnapshot;
            _refreshedVersion = current.Version.VersionNumber;
            _refreshedAt = DateTime.UtcNow;
            var fresh = new Dictionary<int, LineLens>();
            foreach (var pair in byLine)
            {
                var point = new SnapshotPoint(snapshot, pair.Value.Offset).TranslateTo(current, PointTrackingMode.Negative);
                fresh[current.GetLineNumberFromPosition(point.Position)] = new LineLens(
                    current.CreateTrackingPoint(point.Position, PointTrackingMode.Negative),
                    CodeLensText.Order(pair.Value.Counts));
            }

            if (Interlocked.Exchange(ref _loggedOnce, 1) == 0)
            {
                KubunoLog.WriteLine($"CodeLens: {lenses.Count} rust-analyzer lenses, counts on {fresh.Count} line(s) of {System.IO.Path.GetFileName(document.FilePath)}.");
            }

            _pending = new HashSet<int>(unresolvedLines.Select(line =>
                current.GetLineNumberFromPosition(snapshot.GetLineFromLineNumber(line).Start.TranslateTo(current, PointTrackingMode.Negative).Position)));
            _pendingRefresh = false;
            _lenses = fresh;
            Relayout();
        }

        /// <summary>Makes the view ask for the line transforms again (lines gaining or losing their lens space).</summary>
        private void Relayout()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_view.IsClosed || _view.TextViewLines is null)
            {
                return;
            }

            if (_view.InLayout)
            {
#pragma warning disable VSSDK007 // deliberately fire-and-forget: relayout once the current layout is over.
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(alwaysYield: true);
                    Relayout();
                }).FileAndForget("Kubuno/RustCodeLens/Relayout");
#pragma warning restore VSSDK007
                return;
            }

            var first = _view.TextViewLines.FirstVisibleLine;
            _view.DisplayTextLineContainingBufferPosition(first.Start, first.Top - _view.ViewportTop, ViewRelativePosition.Top);
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            if (_enabled && _pending.Count > 0 && !_pendingRefresh
                && _view.TextViewLines.Any(line => _pending.Contains(line.Start.GetContainingLine().LineNumber)))
            {
                // Items whose counts were left out (far from the view) scrolled into view.
                _pendingRefresh = true;
                ScheduleRefresh(TimeSpan.FromMilliseconds(300));
            }

            _layer.RemoveAllAdornments();
            if (!_enabled || _lenses.Count == 0)
            {
                return;
            }

            var properties = _view.FormattedLineSource.DefaultTextProperties;
            var typeface = properties.Typeface;
            double size = LensFontSize();
            double lensHeight = LensHeight();
            var formatMap = _provider.FormatMapService.GetEditorFormatMap(_view);
            var foreground = Brush(formatMap, "Line Number", Brushes.Gray);
            var hover = Brush(formatMap, "urlformat", Brushes.RoyalBlue);
            bool french = UiLanguage.IsFrench;
            var snapshot = _view.TextSnapshot;

            foreach (var line in _view.TextViewLines)
            {
                var bufferLine = line.Start.GetContainingLine();
                if (line.Start != bufferLine.Start || !_lenses.TryGetValue(bufferLine.LineNumber, out var lens))
                {
                    continue;
                }

                int indent = bufferLine.Start.Position;
                while (indent < bufferLine.End.Position && char.IsWhiteSpace(snapshot[indent]))
                {
                    indent++;
                }

                var text = new TextBlock
                {
                    FontFamily = typeface.FontFamily,
                    FontSize = size,
                    Foreground = foreground,
                    Cursor = Cursors.Arrow,
                };
                for (int i = 0; i < lens.Counts.Count; i++)
                {
                    if (i > 0)
                    {
                        text.Inlines.Add(new Run(" | "));
                    }

                    var count = lens.Counts[i];
                    var run = new Run(CodeLensText.Format(count, french)) { Cursor = Cursors.Hand };
                    run.MouseEnter += (_, _) => { run.Foreground = hover; run.TextDecorations = TextDecorations.Underline; };
                    run.MouseLeave += (_, _) => { run.Foreground = foreground; run.TextDecorations = null; };
                    run.MouseLeftButtonUp += (_, args) =>
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();
                        args.Handled = true;
                        Execute(lens.Target, count.Kind);
                    };
                    text.Inlines.Add(run);
                }

                var bounds = line.GetCharacterBounds(new SnapshotPoint(snapshot, Math.Min(indent, line.End.Position)));
                Canvas.SetLeft(text, bounds.Left);
                Canvas.SetTop(text, line.Top + Math.Max(0, (lensHeight - (size * 1.3)) / 2));
                _layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, line.Extent, null, text, null);
            }
        }

        private double LensFontSize() => Math.Max(8, _view.FormattedLineSource?.DefaultTextProperties.FontRenderingEmSize * 0.82 ?? 10);

        private double LensHeight() => Math.Ceiling(LensFontSize() * 1.35);

        private static Brush Brush(IEditorFormatMap map, string key, Brush fallback) =>
            map.GetProperties(key)?[EditorFormatDefinition.ForegroundBrushId] as Brush ?? fallback;

        /// <summary>Find All References (or Go To Implementation) on the item, as if the caret were on it.</summary>
        private void Execute(ITrackingPoint target, CodeLensKind kind)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var point = target.GetPoint(_view.TextSnapshot);
            _view.Selection.Clear();
            _view.Caret.MoveTo(point);
            _view.VisualElement.Focus();
            try
            {
                if (Package.GetGlobalService(typeof(DTE)) is DTE dte)
                {
                    dte.ExecuteCommand(kind == CodeLensKind.Implementations ? "Edit.GoToImplementation" : "Edit.FindAllReferences");
                }
            }
            catch (System.Runtime.InteropServices.COMException exception)
            {
                KubunoLog.WriteLine("CodeLens: could not run the command: " + exception.Message);
            }
        }
    }
}

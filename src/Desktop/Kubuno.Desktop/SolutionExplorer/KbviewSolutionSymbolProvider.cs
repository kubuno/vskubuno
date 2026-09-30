using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Core.Logging;
using Kubuno.Desktop.Designer.EditorFactory;
using Kubuno.Desktop.Logic.SolutionExplorer;
using Kubuno.Desktop.Views.Infrastructure;
using Kubuno.Desktop.Views.Locating;
using Kubuno.Desktop.Views.Options;
using Kubuno.Rust.Extensibility;
using Kubuno.Rust.Logic.SolutionExplorer;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;
using Process = System.Diagnostics.Process;

namespace Kubuno.Desktop.SolutionExplorer
{
    /// <summary>
    /// <c>.kbview</c> element trees under their file in Solution Explorer (docs/RSPROJ.md lot 8), plugged into the Rust
    /// layer's symbol tree (<see cref="ISolutionSymbolProvider"/>): each element with the Kubuno control icon of its tag
    /// (the same as in the designer's Toolbox), and a double-click that opens (or brings up) the Kubuno View Designer and
    /// puts its XML caret on the element - which the designer's own selection sync turns into a selection on the design
    /// surface, the same path a click in the XML pane takes.
    ///
    /// The tree comes from kubuno-views-ls's <c>textDocument/documentSymbol</c>. That server only answers for documents
    /// it was sent with <c>didOpen</c>, so the editor's own instance cannot be used (a <c>didOpen</c>/<c>didClose</c> pair
    /// from here would replace or drop the editor's copy of an open document). A private, short-lived instance is started
    /// instead: initialize, didOpen, documentSymbol, shutdown. The server is a small native binary with no workspace to
    /// load, so this stays cheap, and it only runs when a node is expanded or its file changes.
    /// </summary>
    [Export(typeof(ISolutionSymbolProvider))]
    internal sealed class KbviewSolutionSymbolProvider : ISolutionSymbolProvider
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
        private string? _executable;
        private bool _located;

        public string FileExtension => ".kbview";

        public Guid NavigationLogicalView => VSConstants.LOGVIEWID.Designer_guid;

        public async Task<IReadOnlyList<SolutionSymbol>> QueryAsync(string path, CancellationToken cancellationToken)
        {
            await TaskScheduler.Default;
            if (!_located)
            {
                _executable = Locate();
                _located = true;
            }

            if (_executable == null)
            {
                return Array.Empty<SolutionSymbol>();
            }

            var text = File.ReadAllText(path);
            var result = await RunAsync(_executable, path, text, cancellationToken);
            return ToSymbols(result);
        }

        public ImageMoniker GetIcon(SolutionSymbol symbol) =>
            new ImageMoniker { Guid = ControlIcons.ImagesGuid, Id = ControlIcons.IdFor(symbol.ElementTag) };

        public string GetToolTip(SolutionSymbol symbol) => $"<{symbol.ElementTag}> - line {symbol.Line + 1}";

        /// <summary>The designer's XML pane (a hosted code window; null until it exists), or the frame's own text view for the plain XML editor.</summary>
        public IVsTextView? GetTextView(IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var docView) == VSConstants.S_OK
                && docView is DesignerWindowPane designer)
            {
                return designer.XmlTextView;
            }

            return VsShellUtilities.GetTextView(frame);
        }

        private static string? Locate()
        {
            string? extensionDirectory = null;
            try
            {
                extensionDirectory = Path.GetDirectoryName(typeof(KbviewSolutionSymbolProvider).Assembly.Location);
            }
            catch (Exception)
            {
                // Falls back to PATH and the dev build folders.
            }

            var result = KubunoViewsLanguageServerLocator.Locate(
                KubunoViewsOptionsHost.Current?.LanguageServerPathOverride,
                extensionDirectory,
                new RealKubunoViewsLanguageServerEnvironment());
            if (!result.IsFound)
            {
                KubunoLog.WriteLine("Solution Explorer symbols: kubuno-views-ls was not found, .kbview files show no element tree.");
            }

            return result.Path;
        }

        private static async Task<JToken?> RunAsync(string executable, string path, string text, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
            };

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("kubuno-views-ls did not start.");
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            try
            {
                using var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(process.StandardInput.BaseStream, process.StandardOutput.BaseStream));
                rpc.StartListening();

                var uri = new Uri(path).AbsoluteUri;
                await rpc.InvokeWithParameterObjectAsync<JToken>(
                    "initialize",
                    new { processId = Process.GetCurrentProcess().Id, rootUri = (string?)null, capabilities = new { } },
                    timeout.Token);
                await rpc.NotifyWithParameterObjectAsync("initialized", new { });
                await rpc.NotifyWithParameterObjectAsync(
                    "textDocument/didOpen",
                    new { textDocument = new { uri, languageId = "kbview", version = 1, text } });
                var symbols = await rpc.InvokeWithParameterObjectAsync<JToken>(
                    "textDocument/documentSymbol",
                    new { textDocument = new { uri } },
                    timeout.Token);

                try
                {
                    await rpc.InvokeWithCancellationAsync<JToken>("shutdown", null, timeout.Token);
                    await rpc.NotifyAsync("exit");
                }
                catch (Exception)
                {
                    // The answer is already in hand; the process is killed below if it lingers.
                }

                return symbols;
            }
            finally
            {
                if (!process.WaitForExit(2000))
                {
                    TryKill(process);
                }
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception)
            {
                // Already gone.
            }
        }

        /// <summary>LSP <c>DocumentSymbol[]</c> (name, detail, selectionRange, children) to the Solution Explorer model.</summary>
        internal static IReadOnlyList<SolutionSymbol> ToSymbols(JToken? token)
        {
            var result = new List<SolutionSymbol>();
            if (token is not JArray array)
            {
                return result;
            }

            foreach (var item in array)
            {
                var name = (string?)item["name"];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var start = item["selectionRange"]?["start"] ?? item["range"]?["start"];
                var symbol = new SolutionSymbol(
                    name!,
                    SolutionSymbolKind.Element,
                    SymbolVisibility.Public,
                    (string?)item["detail"],
                    (int?)start?["line"] ?? 0,
                    (int?)start?["character"] ?? 0);
                symbol.Children.AddRange(ToSymbols(item["children"]));
                result.Add(symbol);
            }

            return result;
        }
    }
}

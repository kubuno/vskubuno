using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Core;
using Kubuno.VisualStudio.Core.SolutionExplorer;
using Kubuno.VisualStudio.Infrastructure;
using Kubuno.VisualStudio.Logging;
using Kubuno.VisualStudio.Options;
using Kubuno.VisualStudio.Views.Infrastructure;
using Kubuno.VisualStudio.Views.Locating;
using Kubuno.VisualStudio.Views.Options;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;
using Process = System.Diagnostics.Process;

namespace Kubuno.VisualStudio.SolutionExplorer
{
    /// <summary>Computes the symbol tree of one file for Solution Explorer (docs/RSPROJ.md lot 8).</summary>
    internal interface ISymbolQuery
    {
        Task<IReadOnlyList<SolutionSymbol>> QueryAsync(string path, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Rust items through <c>rust-analyzer symbols</c>: a one-shot process that parses the file piped
    /// to its stdin and prints <c>Analysis::file_structure</c> - the very function rust-analyzer's
    /// <c>textDocument/documentSymbol</c> handler returns. Chosen over sending
    /// <c>documentSymbol</c> through the editor's <c>ILanguageClient</c> because that server only
    /// exists once a <c>.rs</c> file has been opened in an editor, and over a second language server
    /// because it would load the whole Cargo workspace again: this keeps exactly one rust-analyzer
    /// server per workspace, costs a few milliseconds per file and needs no workspace at all.
    /// </summary>
    internal sealed class RustSymbolQuery : ISymbolQuery
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
        private string? _executable;

        public async Task<IReadOnlyList<SolutionSymbol>> QueryAsync(string path, CancellationToken cancellationToken)
        {
            var executable = _executable ??= await LocateAsync();
            if (executable == null)
            {
                return Array.Empty<SolutionSymbol>();
            }

            await TaskScheduler.Default;
            var source = StripBom(File.ReadAllBytes(path));
            var output = await RunAsync(executable, source, cancellationToken);
            return RustSymbolsOutputParser.Parse(output, source);
        }

        private static async Task<string?> LocateAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var options = KubunoPackage.Instance?.GetDialogPage(typeof(RustOptionsPage)) as RustOptionsPage;
            await TaskScheduler.Default;

            var result = RustAnalyzerLocator.Locate(options?.RustAnalyzerPathOverride, new RealRustAnalyzerEnvironment());
            if (!result.IsFound)
            {
                KubunoLog.WriteLine("Solution Explorer symbols: rust-analyzer was not found, .rs files show no symbols.");
            }

            return result.Path;
        }

        private static async Task<string> RunAsync(string executable, byte[] source, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo(executable, "symbols")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("rust-analyzer did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using (var stdin = process.StandardInput.BaseStream)
            {
                await stdin.WriteAsync(source, 0, source.Length, cancellationToken);
            }

            var finished = await Task.WhenAny(stdout, Task.Delay(Timeout, cancellationToken));
            if (finished != stdout)
            {
                TryKill(process);
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("rust-analyzer symbols did not answer in time.");
            }

            var error = await stderr;
            if (!string.IsNullOrWhiteSpace(error))
            {
                KubunoLog.WriteLine("[rust-analyzer symbols stderr] " + error.Trim());
            }

            return await stdout;
        }

        internal static byte[] StripBom(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                var result = new byte[bytes.Length - 3];
                Buffer.BlockCopy(bytes, 3, result, 0, result.Length);
                return result;
            }

            return bytes;
        }

        internal static void TryKill(Process process)
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
    }

    /// <summary>
    /// <c>.kbview</c> element trees through kubuno-views-ls's <c>textDocument/documentSymbol</c>. That
    /// server only answers for documents it was sent with <c>didOpen</c>, so the editor's own
    /// instance cannot be used (a <c>didOpen</c>/<c>didClose</c> pair from here would replace or drop
    /// the editor's copy of an open document). A private, short-lived instance is started instead:
    /// initialize, didOpen, documentSymbol, shutdown. The server is a small native binary with no
    /// workspace to load, so this stays cheap, and it only runs when a node is expanded or its file
    /// changes.
    /// </summary>
    internal sealed class KbviewSymbolQuery : ISymbolQuery
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
        private string? _executable;
        private bool _located;

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

        private static string? Locate()
        {
            string? extensionDirectory = null;
            try
            {
                extensionDirectory = Path.GetDirectoryName(typeof(KbviewSymbolQuery).Assembly.Location);
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
                    RustSymbolQuery.TryKill(process);
                }
            }
        }

        /// <summary>LSP <c>DocumentSymbol[]</c> (name, detail, selectionRange, children) to our model.</summary>
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
                    SolutionSymbolKind.ViewElement,
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

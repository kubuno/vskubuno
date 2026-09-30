using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Rust.Logic;
using Kubuno.Rust.Logic.SolutionExplorer;
using Kubuno.Rust.Infrastructure;
using Kubuno.Core;
using Kubuno.Core.Logging;
using Kubuno.Rust.Options;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Process = System.Diagnostics.Process;

namespace Kubuno.Rust.SolutionExplorer
{
    /// <summary>Computes the symbol tree of one file for Solution Explorer (docs/RSPROJ.md lot 8); other languages' files come from an <see cref="Extensibility.ISolutionSymbolProvider"/>.</summary>
    internal interface ISymbolQuery
    {
        Task<IReadOnlyList<SolutionSymbol>> QueryAsync(string path, CancellationToken cancellationToken);
    }

    /// <summary>The symbols of an <see cref="Extensibility.ISolutionSymbolProvider"/>'s file.</summary>
    internal sealed class ProviderSymbolQuery : ISymbolQuery
    {
        private readonly Extensibility.ISolutionSymbolProvider _provider;

        public ProviderSymbolQuery(Extensibility.ISolutionSymbolProvider provider) => _provider = provider;

        public Task<IReadOnlyList<SolutionSymbol>> QueryAsync(string path, CancellationToken cancellationToken) => _provider.QueryAsync(path, cancellationToken);
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
            var options = KubunoHost.GetDialogPage<RustOptionsPage>();
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
}

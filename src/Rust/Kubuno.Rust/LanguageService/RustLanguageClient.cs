using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Process = System.Diagnostics.Process;
using Kubuno.Rust.Logic;
using Kubuno.Shared.Logic.Lsp;
using Kubuno.Rust.Logic.IntelliSense;
using Kubuno.Rust.Infrastructure;
using Kubuno.Rust.LanguageService.IntelliSense;
using Kubuno.Shared.Logging;
using Kubuno.Rust.Options;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.Rust.LanguageService
{
    /// <summary>
    /// Hosts rust-analyzer as an LSP server for .rs files (content type "rust", see
    /// <see cref="ContentDefinition"/>). Finds the executable via <see cref="RustAnalyzerLocator"/>
    /// (option override, then rustup, then the default cargo bin, then PATH) and, when it cannot be
    /// found, shows an info bar with the exact fix instead of activating silently with no server.
    /// </summary>
    [ContentType(Constants.RustContentType)]
    [Export(typeof(ILanguageClient))]
    public sealed class RustLanguageClient : ILanguageClient, ILanguageClientCustomMessage2
    {
        private readonly RealRustAnalyzerEnvironment _environment = new();
        private readonly RustAnalyzerMiddleLayer _middleLayer = new();
        private Process? _process;

        public RustLanguageClient()
        {
            Instance = this;
            _middleLayer.ReferenceParticipants = () => ReferenceParticipants;
            _middleLayer.DefinitionProviders = () => DefinitionProviders;
#pragma warning disable VSSDK007 // fire-and-forget from an event handler; FileAndForget reports faults to the "Kubuno" pane.
            RustOptionsPage.Applied += (_, _) => ThreadHelper.JoinableTaskFactory.RunAsync(PushConfigurationAsync).FileAndForget("kubuno/rust/pushConfiguration");
#pragma warning restore VSSDK007
        }

        /// <summary>The MEF-created instance, for <see cref="Commands.RestartRustAnalyzerCommand"/> (null until a .rs file activated the client).</summary>
        public static RustLanguageClient? Instance { get; private set; }

        /// <summary>The document versions rust-analyzer was sent (Kubuno's own requests wait for their text).</summary>
        internal RustDocumentVersions DocumentVersions => _middleLayer.DocumentVersions;

        [Import]
        internal IVsFolderWorkspaceService? WorkspaceService { get; set; }

        [Import]
        internal SVsServiceProvider? ServiceProvider { get; set; }

        /// <summary>What the layers above add to a rename or to Find All References (<see cref="Extensibility.IRustReferenceParticipant"/>).</summary>
        [ImportMany]
        internal IEnumerable<Extensibility.IRustReferenceParticipant> ReferenceParticipants { get; set; } = Array.Empty<Extensibility.IRustReferenceParticipant>();

        /// <summary>What the layers above answer for Go To Definition (<see cref="Extensibility.IRustDefinitionProvider"/>).</summary>
        [ImportMany]
        internal IEnumerable<Extensibility.IRustDefinitionProvider> DefinitionProviders { get; set; } = Array.Empty<Extensibility.IRustDefinitionProvider>();

        /// <summary>The client's name (also how Kubuno's own requests pick this server through Visual Studio's broker).</summary>
        public const string ClientName = "Kubuno Rust Language Server";

        public string Name => ClientName;

        public IEnumerable<string> ConfigurationSections => Array.Empty<string>();

        /// <summary>rust-analyzer's settings (<see cref="RustAnalyzerHandshake.InitializationOptions"/>), as the Newtonsoft object Visual Studio's JSON-RPC serializer expects.</summary>
        public object? InitializationOptions => JObject.Parse(RustAnalyzerHandshake.InitializationOptions().ToJsonString());

        public IEnumerable<string>? FilesToWatch => null;

        public object? MiddleLayer => _middleLayer;

        public object? CustomMessageTarget { get; } = new RustClientTarget();

        public bool ShowNotificationOnInitializeFailed => true;

        public JsonRpc? Rpc { get; set; }

        public event AsyncEventHandler<EventArgs>? StartAsync;

        public event AsyncEventHandler<EventArgs>? StopAsync;

        public async Task<Connection?> ActivateAsync(CancellationToken token)
        {
            await TaskScheduler.Default;

            var options = await GetOptionsAsync();
            var locateResult = RustAnalyzerLocator.Locate(options?.RustAnalyzerPathOverride, _environment);
            if (!locateResult.IsFound)
            {
                KubunoLog.WriteLine(
                    "rust-analyzer was not found (checked: option override, 'rustup which rust-analyzer', " +
                    "%USERPROFILE%\\.cargo\\bin\\rust-analyzer.exe, PATH). Not starting the Rust language server.");
                RustAnalyzerMissingInfoBar.ShowIfNeeded();
                return null;
            }

            KubunoLog.WriteLine($"Using rust-analyzer from {locateResult.Path} (source: {locateResult.Source}).");

            var workspaceRoot = await GetWorkspaceRootAsync();
            var workingDirectory = workspaceRoot ?? Path.GetDirectoryName(locateResult.Path!);
            KubunoLog.WriteLine($"Cargo workspace root: {workspaceRoot ?? "(unknown - using rust-analyzer's own directory)"}");

            var startInfo = new ProcessStartInfo
            {
                FileName = locateResult.Path,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory,
            };

            Process process;
            try
            {
                process = Process.Start(startInfo);
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Failed to start rust-analyzer", exception);
                RustAnalyzerMissingInfoBar.ShowIfNeeded();
                return null;
            }

            if (process == null)
            {
                KubunoLog.WriteLine("Failed to start rust-analyzer: Process.Start returned null.");
                return null;
            }

            process.ErrorDataReceived += (_, e) =>
            {
                // Visual Studio echoes every server-to-client message back as a "NotificationReceived" notification, which
                // rust-analyzer reports as an error each time: noise, not a problem.
                if (!string.IsNullOrEmpty(e.Data) && e.Data.IndexOf("method: \"NotificationReceived\"", StringComparison.Ordinal) < 0)
                {
                    KubunoLog.WriteLine($"[rust-analyzer stderr] {e.Data}");
                }
            };
            process.BeginErrorReadLine();

            KubunoLog.WriteLine($"rust-analyzer started (PID {process.Id}).");
            _process = process;
            // The initialize handshake is adjusted on the wire (see LspHandshakeStreams): Kubuno's completion snippets and
            // commands on the client side, the C#-like semantic token legend on the server side.
            var handshake = new LspHandshakeStreams(
                process.StandardInput.BaseStream,
                process.StandardOutput.BaseStream,
                RustAnalyzerHandshake.RewriteInitializeRequest,
                json => RustAnalyzerHandshake.RewriteInitializeResponse(json, map =>
                {
                    _middleLayer.SemanticTokens = map;
                    KubunoLog.WriteLine($"rust-analyzer handshake adjusted: completion snippets on, semantic tokens mapped onto {map.Legend.Count} C# classifications.");
                }))
            {
                OnRewriteError = exception => KubunoLog.WriteException("Could not adjust the rust-analyzer handshake", exception),
            };
            return new Connection(handshake.ServerToClient, handshake.ClientToServer);
        }

        /// <summary>
        /// Tools &gt; "Kubuno: Restart rust-analyzer": stops the server (Visual Studio sends shutdown/exit and
        /// drops the server's diagnostics), makes sure the old process is gone, then starts a fresh one, which
        /// gets every open .rs document again through didOpen. A recovery tool for any server-side state that
        /// went wrong, without closing the solution.
        /// </summary>
        internal async Task RestartAsync()
        {
            KubunoLog.WriteLine("Restarting rust-analyzer (Tools > Kubuno: Restart rust-analyzer).");
            var old = _process;
            if (StopAsync is not null)
            {
                await StopAsync.InvokeAsync(this, EventArgs.Empty);
            }

            await TaskScheduler.Default;
            try
            {
                if (old is not null && !old.HasExited && !old.WaitForExit(5000))
                {
                    KubunoLog.WriteLine($"rust-analyzer (PID {old.Id}) did not exit after shutdown; killing it.");
                    old.Kill();
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone between the check and the kill: nothing left to stop.
            }

            if (StartAsync is not null)
            {
                await StartAsync.InvokeAsync(this, EventArgs.Empty);
            }
        }

        public Task OnLoadedAsync() => StartAsync?.InvokeAsync(this, EventArgs.Empty) ?? Task.CompletedTask;

        public Task OnServerInitializedAsync()
        {
            KubunoLog.WriteLine("rust-analyzer initialized.");
            return Task.CompletedTask;
        }

        public Task<InitializationFailureContext?> OnServerInitializeFailedAsync(ILanguageClientInitializationInfo initializationState)
        {
            var exception = initializationState.InitializationException;
            KubunoLog.WriteException("rust-analyzer failed to initialize", exception ?? new InvalidOperationException("Unknown initialization failure."));
            RustAnalyzerMissingInfoBar.ShowIfNeeded();
            return Task.FromResult<InitializationFailureContext?>(new InitializationFailureContext
            {
                FailureMessage = $"Kubuno: rust-analyzer failed to initialize.\n{exception}",
            });
        }

        public async Task AttachForCustomMessageAsync(JsonRpc rpc)
        {
            Rpc = rpc;

            // Route raw LSP traffic (requests/notifications) into the "Kubuno" Output pane, but
            // only when the user opted in (Tools > Options > Kubuno > Rust > LSP trace): at
            // default (Off) every request/response would otherwise flood the pane, drowning out
            // the startup line and errors that are always logged separately (see ActivateAsync /
            // OnServerInitializeFailedAsync), regardless of this setting.
            var options = await GetOptionsAsync();
            var traceLevel = options?.LspTrace ?? LspTraceLevel.Off;
            if (traceLevel != LspTraceLevel.Off)
            {
                rpc.TraceSource.Switch.Level = traceLevel == LspTraceLevel.Verbose ? SourceLevels.Verbose : SourceLevels.Information;
                rpc.TraceSource.Listeners.Add(KubunoLog.CreateJsonRpcTraceListener());
            }
        }

        public Task StopServerAsync() => StopAsync?.InvokeAsync(this, EventArgs.Empty) ?? Task.CompletedTask;

        /// <summary>
        /// Sends rust-analyzer the current settings (<c>workspace/didChangeConfiguration</c>) so an options change - the
        /// inlay hints kinds - applies to the running server without a restart. No-op until the server is connected.
        /// </summary>
        internal async Task PushConfigurationAsync()
        {
            var rpc = Rpc;
            if (rpc is null)
            {
                return;
            }

            try
            {
                var settings = JObject.Parse(RustAnalyzerHandshake.InitializationOptions().ToJsonString());
                await rpc.NotifyWithParameterObjectAsync("workspace/didChangeConfiguration", new { settings }).ConfigureAwait(false);
                KubunoLog.WriteLine("rust-analyzer settings updated (inlay hints).");
            }
            catch (Exception ex)
            {
                KubunoLog.WriteException("Could not send rust-analyzer its new settings", ex);
            }
        }

        private async Task<RustOptionsPage?> GetOptionsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            return Kubuno.Shared.KubunoHost.GetDialogPage<RustOptionsPage>();
        }

        private async Task<string?> GetWorkspaceRootAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var openedFolder = WorkspaceService?.CurrentWorkspace?.Location;
            var startPath = openedFolder ?? TryGetActiveDocumentPath();
            if (string.IsNullOrEmpty(startPath))
            {
                return null;
            }

            return CargoWorkspaceLocator.FindWorkspaceRoot(startPath, Directory.Exists, File.Exists);
        }

        private string? TryGetActiveDocumentPath()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (ServiceProvider?.GetService(typeof(DTE)) is not DTE dte)
                {
                    return null;
                }

                return dte.ActiveDocument?.FullName;
            }
            catch (Exception)
            {
                // Best effort only: with no Open Folder workspace and no active document (e.g.
                // during startup), rust-analyzer simply falls back to its own directory.
                return null;
            }
        }
    }
}

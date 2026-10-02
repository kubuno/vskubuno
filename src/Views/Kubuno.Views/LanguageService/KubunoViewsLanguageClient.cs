using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Views.Infrastructure;
using Kubuno.Views.Locating;
using Kubuno.Views.Logging;
using Kubuno.Views.Options;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using StreamJsonRpc;
using Process = System.Diagnostics.Process;

namespace Kubuno.Views.LanguageService
{
    /// <summary>
    /// Hosts <c>kubuno-views-ls</c> as an LSP server for <c>.kbview</c> files (content type "kbview",
    /// see <see cref="ContentDefinition"/>). Finds the executable via
    /// <see cref="KubunoViewsLanguageServerLocator"/> (option override, then the extension's own
    /// <c>tools</c> folder, then PATH, then the local dev build folders) and, when it cannot be
    /// found, shows an info bar with the exact fix instead of activating silently with no server.
    ///
    /// Mirrors the sibling VSIX project's <c>RustLanguageClient</c> closely, with two differences
    /// forced by this library not being able to reference that project (see INTEGRATION.md): logging
    /// goes through <see cref="KubunoViewsLogHost"/>/<see cref="IKubunoLog"/> instead of the VSIX's
    /// own static <c>KubunoLog</c>, and options come from <see cref="KubunoViewsOptionsHost"/> instead
    /// of <c>Kubuno.Shared.KubunoHost.Package.GetDialogPage</c>.
    /// </summary>
    [ContentType(KbviewConstants.ContentType)]
    [Export(typeof(ILanguageClient))]
    public sealed class KubunoViewsLanguageClient : ILanguageClient, ILanguageClientCustomMessage2
    {
        private readonly IKubunoViewsLanguageServerEnvironment _environment = new RealKubunoViewsLanguageServerEnvironment();
        private readonly HoverSuppressionMiddleLayer _middleLayer = new HoverSuppressionMiddleLayer();

        public KubunoViewsLanguageClient()
        {
            Current = this;
        }

        /// <summary>The MEF-created instance (null until a .kbview file activated the client), for the VSIX's QuickInfo source.</summary>
        public static KubunoViewsLanguageClient? Current { get; private set; }

        [Import]
        internal IVsFolderWorkspaceService? WorkspaceService { get; set; }

        [Import]
        internal SVsServiceProvider? ServiceProvider { get; set; }

        public string Name => "Kubuno Views Language Server";

        public IEnumerable<string> ConfigurationSections => Array.Empty<string>();

        public object? InitializationOptions => null;

        public IEnumerable<string>? FilesToWatch => null;

        public object? MiddleLayer => _middleLayer;

        public object? CustomMessageTarget => null;

        public bool ShowNotificationOnInitializeFailed => true;

        public JsonRpc? Rpc { get; set; }

        /// <summary>
        /// True once the server answered <c>initialize</c> (<see cref="OnServerInitializedAsync"/>). <see cref="Rpc"/> is
        /// attached BEFORE that, and kubuno-views-ls rejects - and exits on - any request that precedes
        /// <c>initialize</c> (found live: a designer restored with the solution sent <c>kubuno/registry</c>
        /// too early), so custom-message callers must wait for this, not just for <see cref="Rpc"/>.
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// <see cref="Rpc"/> once the server is initialized and its connection still open, otherwise null. Visual Studio
        /// stops the server itself when the solution closes (never through <see cref="StopServerAsync"/>): the old
        /// connection must not be handed out after that (docs/EVENTS.md EVT-7b: the designer of a reopened solution
        /// kept the dead connection and its Properties window stayed empty until Visual Studio restarted).
        /// </summary>
        public JsonRpc? ReadyRpc => IsInitialized && Rpc is { IsDisposed: false } rpc ? rpc : null;

        /// <summary>True while Visual Studio is activating the server (inside <see cref="ActivateAsync"/>).</summary>
        public bool IsActivating { get; private set; }

        public event AsyncEventHandler<EventArgs>? StartAsync;

        public event AsyncEventHandler<EventArgs>? StopAsync;

        /// <summary>
        /// Asks Visual Studio to (re)start the server when it is neither running nor starting: once a solution was
        /// closed and another opened, a designer restored with it would otherwise wait for a server nobody starts.
        /// </summary>
        public async Task EnsureStartedAsync()
        {
            if (ReadyRpc is not null || IsActivating || Rpc is { IsDisposed: false } || StartAsync is null)
            {
                return;
            }

            KubunoViewsLogHost.Current.WriteLine("kubuno-views-ls is not running: asking Visual Studio to start it.");
            await StartAsync.InvokeAsync(this, EventArgs.Empty);
        }

        public async Task<Connection?> ActivateAsync(CancellationToken token)
        {
            IsActivating = true;
            try
            {
                return await ActivateCoreAsync();
            }
            finally
            {
                IsActivating = false;
            }
        }

        private async Task<Connection?> ActivateCoreAsync()
        {
            await TaskScheduler.Default;

            var optionOverride = KubunoViewsOptionsHost.Current?.LanguageServerPathOverride;
            var extensionDirectory = GetExtensionInstallDirectory();
            var locateResult = KubunoViewsLanguageServerLocator.Locate(optionOverride, extensionDirectory, _environment);
            if (!locateResult.IsFound)
            {
                KubunoViewsLogHost.Current.WriteLine(
                    "kubuno-views-ls was not found (checked: option override, " +
                    $"'{extensionDirectory ?? "(unknown extension directory)"}\\{KbviewConstants.ExtensionToolsFolderName}\\{KbviewConstants.LanguageServerExecutableName}', " +
                    "PATH, C:\\kubuno-build\\desktop-target\\debug\\, C:\\kubuno-build\\agent-views-ls\\debug\\). " +
                    "Not starting the Kubuno views language server.");
                KubunoViewsLanguageServerMissingInfoBar.ShowIfNeeded();
                return null;
            }

            KubunoViewsLogHost.Current.WriteLine($"Using kubuno-views-ls from {locateResult.Path} (source: {locateResult.Source}).");

            var workspaceRoot = await GetWorkspaceRootAsync();
            var workingDirectory = workspaceRoot ?? Path.GetDirectoryName(locateResult.Path!);
            KubunoViewsLogHost.Current.WriteLine($"Cargo workspace root: {workspaceRoot ?? "(unknown - using kubuno-views-ls's own directory)"}");

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
            // The diagnostics in Visual Studio's UI language (kubuno_views::messages, docs/DESIGNER.md section 17).
            startInfo.EnvironmentVariables["KUBUNO_UI_LANG"] = Kubuno.Shared.Logic.Localization.UiLanguage.IsFrench ? "fr" : "en";

            Process process;
            try
            {
                process = Process.Start(startInfo);
            }
            catch (Exception exception)
            {
                KubunoViewsLogHost.Current.WriteException("Failed to start kubuno-views-ls", exception);
                KubunoViewsLanguageServerMissingInfoBar.ShowIfNeeded();
                return null;
            }

            if (process == null)
            {
                KubunoViewsLogHost.Current.WriteLine("Failed to start kubuno-views-ls: Process.Start returned null.");
                return null;
            }

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    KubunoViewsLogHost.Current.WriteLine($"[kubuno-views-ls stderr] {e.Data}");
                }
            };
            process.BeginErrorReadLine();

            KubunoViewsLogHost.Current.WriteLine($"kubuno-views-ls started (PID {process.Id}).");
            return new Connection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
        }

        public Task OnLoadedAsync() => StartAsync?.InvokeAsync(this, EventArgs.Empty) ?? Task.CompletedTask;

        public Task OnServerInitializedAsync()
        {
            IsInitialized = true;
            KubunoViewsLogHost.Current.WriteLine("kubuno-views-ls initialized.");
            return Task.CompletedTask;
        }

        public Task<InitializationFailureContext?> OnServerInitializeFailedAsync(ILanguageClientInitializationInfo initializationState)
        {
            var exception = initializationState.InitializationException;
            KubunoViewsLogHost.Current.WriteException("kubuno-views-ls failed to initialize", exception ?? new InvalidOperationException("Unknown initialization failure."));
            KubunoViewsLanguageServerMissingInfoBar.ShowIfNeeded();
            return Task.FromResult<InitializationFailureContext?>(new InitializationFailureContext
            {
                FailureMessage = $"Kubuno: kubuno-views-ls failed to initialize.\n{exception}",
            });
        }

        public Task AttachForCustomMessageAsync(JsonRpc rpc)
        {
            // A new connection is not initialized yet, and a dropped one must stop being handed out (see ReadyRpc).
            IsInitialized = false;
            Rpc = rpc;
            rpc.Disconnected += (_, e) =>
            {
                if (ReferenceEquals(Rpc, rpc))
                {
                    IsInitialized = false;
                    Rpc = null;
                    KubunoViewsLogHost.Current.WriteLine($"kubuno-views-ls disconnected ({e.Reason}).");
                }
            };

            // Unlike RustLanguageClient, there is no per-user trace-level option here yet (see
            // INTEGRATION.md/this library's own scope note): errors and the startup/shutdown lines
            // above are always logged; raw request/response traffic is not, to avoid flooding the
            // "Kubuno" pane by default. A trace-level option can be added to KbviewOptionsPage later,
            // following RustOptionsPage's LspTrace property, without changing this method's shape.
            return Task.CompletedTask;
        }

        public Task StopServerAsync()
        {
            IsInitialized = false;
            return StopAsync?.InvokeAsync(this, EventArgs.Empty) ?? Task.CompletedTask;
        }

        /// <summary>
        /// The directory this library's own assembly is loaded from - the extension's install
        /// directory once shipped inside the VSIX (see INTEGRATION.md), which is where the
        /// <c>tools\kubuno-views-ls.exe</c> candidate is rooted.
        /// </summary>
        private static string? GetExtensionInstallDirectory()
        {
            try
            {
                var location = Assembly.GetExecutingAssembly().Location;
                return string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task<string?> GetWorkspaceRootAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var openedFolder = WorkspaceService?.CurrentWorkspace?.Location;
            if (string.IsNullOrEmpty(openedFolder))
            {
                return null;
            }

            return KbviewWorkspaceLocator.FindWorkspaceRoot(openedFolder, Directory.Exists, File.Exists);
        }
    }
}

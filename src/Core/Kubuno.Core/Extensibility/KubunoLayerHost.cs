using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE80;
using Kubuno.Core.Commands;
using Kubuno.Core.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Core.Extensibility
{
    /// <summary>
    /// Runs the package's initialization for Core and every <see cref="KubunoLayer"/> (docs/ARCHITECTURE.md, "Layers (as
    /// built)"). The package (src/Kubuno.VisualStudio) only declares registrations and lists its layers; the sequence and
    /// the threading rules live here, identical for every layer:
    /// <list type="bullet">
    /// <item>background thread: <see cref="KubunoHost"/> is set, each layer's <see cref="KubunoLayer.InitializeAsync"/>,
    /// the menu command service lookup;</item>
    /// <item>UI thread (reported as the package's UI-thread load time): the Core commands, each layer's
    /// <see cref="KubunoLayer.InitializeOnUIThread"/>;</item>
    /// <item>deferred, once any solution load in progress is over: at UI idle the "Kubuno" Output pane, the MCP bridge's
    /// DTE reads and each layer's <see cref="KubunoLayer.InitializeOnIdle"/>; then in the background the MCP bridge
    /// (docs/MCP.md) and each layer's <see cref="KubunoLayer.InitializeDeferredAsync"/>.</item>
    /// </list>
    /// Load time and UI-thread time are written to the "Kubuno" pane (and to <c>KUBUNO_VS_LOG</c>), as before the layers.
    /// </summary>
    public sealed class KubunoLayerHost
    {
        private readonly AsyncPackage _package;
        private readonly KubunoLayerContext _context;
        private Kubuno.Core.Mcp.Bridge.PipeProtocol.VsMcpBridgeHost? _mcpBridgeHost;

        public KubunoLayerHost(AsyncPackage package, Action<IVsEditorFactory> registerEditorFactory, IReadOnlyList<KubunoLayer> layers)
        {
            _package = package;
            _context = new KubunoLayerContext(package, registerEditorFactory);
            Layers = layers;
        }

        /// <summary>The layers, in initialization order.</summary>
        public IReadOnlyList<KubunoLayer> Layers { get; }

        /// <summary>The package's <c>InitializeAsync</c> body (called on the background thread a background-loaded package starts on).</summary>
        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            var loadTime = Stopwatch.StartNew();
            var uiThreadTime = new Stopwatch();

            // ---- Background thread: file probing, service lookups. No SwitchToMainThreadAsync before the UI section.
            await TaskScheduler.Default;
            KubunoHost.Attach(_package);
            foreach (var layer in Layers)
            {
                await RunAsync(layer, "initialize", () => layer.InitializeAsync(_context, cancellationToken));
            }

            _context.CommandService = await _package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;

            // ---- UI thread: only what must exist before the first Kubuno command, editor or key binding.
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            uiThreadTime.Start();
            if (_context.CommandService is not null)
            {
                DialogGalleryCommand.Initialize(_context.CommandService);
            }

            foreach (var layer in Layers)
            {
                Run(layer, "initialize (UI thread)", () => layer.InitializeOnUIThread(_context));
            }

            uiThreadTime.Stop();
            KubunoLog.WriteLine($"Kubuno: package loaded in {loadTime.ElapsedMilliseconds} ms ({uiThreadTime.ElapsedMilliseconds} ms on the UI thread); the rest follows once the solution is loaded.");

            // Everything else: after the solution (or folder) has finished loading, off the UI thread or on UI idle.
            _package.JoinableTaskFactory.RunAsync(() => InitializeDeferredAsync(_package.DisposalToken)).FileAndForget("Kubuno/Package/DeferredInitialization");
        }

        /// <summary>
        /// The non-essential part of the initialization, none of which a first Kubuno command or editor needs: runs once
        /// any solution load in progress is over, never on the UI thread except for the short idle-time step.
        /// </summary>
        private async Task InitializeDeferredAsync(CancellationToken cancellationToken)
        {
            var uiThreadTime = new Stopwatch();
            try
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                var waitForSolution = KnownUIContexts.SolutionOpeningContext.IsActive && !KnownUIContexts.SolutionExistsAndFullyLoadedContext.IsActive;
                if (waitForSolution)
                {
                    var loaded = new TaskCompletionSource<bool>();
                    KnownUIContexts.SolutionExistsAndFullyLoadedContext.WhenActivated(() => loaded.TrySetResult(true));
                    await loaded.Task.WithCancellation(cancellationToken);
                }

                _context.WaitedForSolutionLoad = waitForSolution;
                await TaskScheduler.Default;
                _context.ComponentModel = await _package.GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
                var outputWindowService = await _package.GetServiceAsync(typeof(SVsOutputWindow));
                var dteService = await _package.GetServiceAsync(typeof(SDTE));
                string? visualStudioVersion = null;
                string? solutionOrFolderPath = null;
                DTE2? dte = null;

                // Idle-time UI work: the "Kubuno" Output pane (log lines are buffered until then), the MCP bridge's DTE
                // reads, then each layer's own idle step.
                await _package.JoinableTaskFactory.StartOnIdle(
                    async () =>
                    {
                        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                        uiThreadTime.Start();
                        if (outputWindowService is IVsOutputWindow outputWindow)
                        {
                            var paneGuid = KubunoGuids.OutputPane;
                            var hr = outputWindow.CreatePane(ref paneGuid, KubunoConstants.OutputPaneTitle, fInitVisible: 1, fClearWithSolution: 0);
                            if (ErrorHandler.Succeeded(hr) &&
                                ErrorHandler.Succeeded(outputWindow.GetPane(ref paneGuid, out var pane)) &&
                                pane != null)
                            {
                                KubunoLog.Initialize(pane);
                            }
                        }

                        dte = dteService as DTE2;
                        if (dte is not null)
                        {
                            visualStudioVersion = dte.Version;
                            solutionOrFolderPath = dte.Solution?.FullName;
                        }

                        foreach (var layer in Layers)
                        {
                            Run(layer, "initialize (idle)", () => layer.InitializeOnIdle(_context));
                        }

                        uiThreadTime.Stop();
                    },
                    VsTaskRunContext.UIThreadIdlePriority).JoinAsync(cancellationToken);

                await TaskScheduler.Default;
                StartMcpBridge(dte, visualStudioVersion, solutionOrFolderPath);
                foreach (var layer in Layers)
                {
                    await RunAsync(layer, "deferred initialization", () => layer.InitializeDeferredAsync(_context, cancellationToken));
                }

                KubunoLog.WriteLine($"Kubuno: deferred initialization done ({uiThreadTime.ElapsedMilliseconds} ms on the UI thread, at idle{(waitForSolution ? ", after the solution load" : string.Empty)}).");
            }
            catch (OperationCanceledException)
            {
                // Visual Studio is closing.
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno: deferred package initialization failed", exception);
            }
        }

        /// <summary>
        /// Starts the MCP bridge (see docs/MCP.md "Integration"): the net48 leg of Kubuno.Core.Mcp.Bridge, loaded in-proc
        /// here, hosts a named pipe that <c>kubuno-vs-mcp.exe</c> (started independently by Claude Code, outside this
        /// process) connects to. Called on a background thread once the solution is loaded (so the discovery file names
        /// it), with the DTE values read at UI idle. Every failure is logged, never thrown: read-only Visual Studio
        /// context for Claude is a convenience no layer depends on.
        /// </summary>
        private void StartMcpBridge(DTE2? dte, string? visualStudioVersion, string? solutionOrFolderPath)
        {
            try
            {
                if (dte is null)
                {
                    KubunoLog.WriteLine("Kubuno: MCP bridge not started - the DTE service is unavailable.");
                    return;
                }

                if (_mcpBridgeHost is not null)
                {
                    return;
                }

                var provider = new Kubuno.Core.Mcp.Bridge.Dte.DteVsContextProvider(dte);
                var host = new Kubuno.Core.Mcp.Bridge.PipeProtocol.VsMcpBridgeHost(provider);
                host.Start(visualStudioVersion: visualStudioVersion, solutionOrFolderPath: solutionOrFolderPath);
                _mcpBridgeHost = host;
                KubunoLog.WriteLine($"Kubuno: MCP bridge started (pipe '{host.PipeName}').");
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno: failed to start the MCP bridge", exception);
            }
        }

        /// <summary>The package's <c>Dispose(true)</c> body, on the UI thread: layers in reverse order, then Core.</summary>
        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (var layer in Layers.Reverse())
            {
                Run(layer, "dispose", () => layer.Dispose(_context));
            }

            _mcpBridgeHost?.Dispose();
            _mcpBridgeHost = null;
            KubunoHost.Detach(_package);
        }

        private static void Run(KubunoLayer layer, string step, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException($"Kubuno: the {layer.Name} layer failed to {step}", exception);
            }
        }

        private static async Task RunAsync(KubunoLayer layer, string step, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException($"Kubuno: the {layer.Name} layer failed to {step}", exception);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Cargo.DesignSurface;
using Kubuno.Cargo.Processes;
using Kubuno.VisualStudio.Designer.DesignSurface;
using Kubuno.VisualStudio.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.VisualStudio.DesignerIntegration
{
    /// <summary>
    /// Gives every <c>.kbview</c> designer pane the design surface of its OWN project (docs/DESIGNER.md
    /// section 15): one <see cref="ProjectRuntimeSource"/> per <c>.rsproj</c>, shared by the panes of that
    /// project, which runs the design build (<see cref="DesignSurfaceBuilder"/>) when the project has
    /// been built and again after every build of it (solution build events), and falls back to the
    /// surface bundled with the extension meanwhile. A document outside a <c>.rsproj</c> gets the bundled
    /// surface.
    /// </summary>
    internal sealed class ProjectDesignSurfaceRuntimeProvider : IDesignSurfaceRuntimeProvider, IVsUpdateSolutionEvents2, IDisposable
    {
        private readonly DesignSurfaceRuntime _bundled;
        private readonly JoinableTaskFactory _joinableTaskFactory;
        private readonly Dictionary<IVsHierarchy, ProjectRuntimeSource> _sources = new Dictionary<IVsHierarchy, ProjectRuntimeSource>();
        private IVsSolutionBuildManager2? _buildManager;
        private uint _cookie;

        public ProjectDesignSurfaceRuntimeProvider(string bundledExePath, JoinableTaskFactory joinableTaskFactory)
        {
            _bundled = new DesignSurfaceRuntime(bundledExePath, isProjectRuntime: false, expectedUiDllSha256: null);
            _joinableTaskFactory = joinableTaskFactory;
        }

        /// <summary>Subscribes to solution build events (UI thread).</summary>
        public void Advise(IVsSolutionBuildManager2 buildManager)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _buildManager = buildManager;
            ErrorHandler.ThrowOnFailure(buildManager.AdviseUpdateSolutionEvents(this, out _cookie));
        }

        public DesignSurfaceRuntimeLease Acquire(DesignSurfaceDocument document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (document.Hierarchy is IVsHierarchy hierarchy && RsprojProperties.Read(hierarchy, _buildManager) is { } project)
            {
                if (!_sources.TryGetValue(hierarchy, out var source))
                {
                    source = new ProjectRuntimeSource(this, hierarchy, project);
                    _sources.Add(hierarchy, source);
                    source.Resolve();
                }

                source.References++;
                return new DesignSurfaceRuntimeLease(source, () => Release(hierarchy, source));
            }

            return new DesignSurfaceRuntimeLease(new FixedDesignSurfaceRuntimeSource(_bundled, DesignSurfaceRuntimeState.NotApplicable), null);
        }

        private void Release(IVsHierarchy hierarchy, ProjectRuntimeSource source)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (--source.References > 0)
            {
                return;
            }

            source.Cancel();
            if (_sources.TryGetValue(hierarchy, out var current) && ReferenceEquals(current, source))
            {
                _sources.Remove(hierarchy);
            }
        }

        // IVsUpdateSolutionEvents2: a finished build (or clean) of a project with an open designer
        // re-runs its design build; a configuration switch re-reads the project's properties.
        public int UpdateProjectCfg_Done(IVsHierarchy pHierProj, IVsCfg pCfgProj, IVsCfg pCfgSln, uint dwAction, int fSuccess, int fCancel)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (pHierProj is not null && _sources.TryGetValue(pHierProj, out var source))
            {
                source.OnProjectBuilt();
            }

            return VSConstants.S_OK;
        }

        public int OnActiveProjectCfgChange(IVsHierarchy pIVsHierarchy)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (pIVsHierarchy is not null && _sources.TryGetValue(pIVsHierarchy, out var source) && RsprojProperties.Read(pIVsHierarchy, _buildManager) is { } project)
            {
                source.Reconfigure(project);
            }

            return VSConstants.S_OK;
        }

        public int UpdateSolution_Begin(ref int pfCancelUpdate) => VSConstants.S_OK;

        public int UpdateSolution_Done(int fSucceeded, int fModified, int fCancelCommand) => VSConstants.S_OK;

        public int UpdateSolution_StartUpdate(ref int pfCancelUpdate) => VSConstants.S_OK;

        public int UpdateSolution_Cancel() => VSConstants.S_OK;

        public int UpdateProjectCfg_Begin(IVsHierarchy pHierProj, IVsCfg pCfgProj, IVsCfg pCfgSln, uint dwAction, ref int pfCancel) => VSConstants.S_OK;

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_buildManager is not null && _cookie != 0)
            {
                _buildManager.UnadviseUpdateSolutionEvents(_cookie);
                _cookie = 0;
            }

            foreach (var source in _sources.Values)
            {
                source.Cancel();
            }

            _sources.Clear();
        }

        /// <summary>The runtime of one project: bundled until a design build against the project succeeds.</summary>
        private sealed class ProjectRuntimeSource : IDesignSurfaceRuntimeSource
        {
            private readonly ProjectDesignSurfaceRuntimeProvider _owner;
            private readonly IVsHierarchy _hierarchy;
            private DesignSurfaceProject _project;
            private DesignSurfaceRuntime _current;
            private DesignSurfaceRuntimeState _state = DesignSurfaceRuntimeState.NotBuilt;
            private string? _detail;
            private CancellationTokenSource? _build;
            private bool _pending;

            public ProjectRuntimeSource(ProjectDesignSurfaceRuntimeProvider owner, IVsHierarchy hierarchy, DesignSurfaceProject project)
            {
                _owner = owner;
                _hierarchy = hierarchy;
                _project = project;
                _current = owner._bundled;
            }

            public int References { get; set; }

            public DesignSurfaceRuntime? Current => _current;

            public DesignSurfaceRuntimeState State => _state;

            public string? Detail => _detail;

            public event EventHandler? Changed;

            /// <summary>Reuse an unchanged design build at once, else start one when the project is built (UI thread).</summary>
            public void Resolve()
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (_build is not null)
                {
                    _pending = true; // re-run once the running design build is done
                    return;
                }

                var project = _project;
                var cancellation = new CancellationTokenSource();
                _build = cancellation;
                _owner._joinableTaskFactory.RunAsync(() => ResolveAsync(project, cancellation)).FileAndForget("Kubuno/Designer/DesignBuild");
            }

            public void OnProjectBuilt()
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Resolve();
            }

            public void Reconfigure(DesignSurfaceProject project)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (string.Equals(project.Identity, _project.Identity, StringComparison.Ordinal))
                {
                    return;
                }

                _project = project;
                _build?.Cancel();
                Resolve();
            }

            public void Cancel() => _build?.Cancel();

            public void RunAction()
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (_state == DesignSurfaceRuntimeState.Building)
                {
                    KubunoLog.WriteLine("Kubuno: design build canceled.");
                    _build?.Cancel();
                    return;
                }

                // "Générer": a normal Visual Studio build of the project; its completion re-runs the design build.
                if (_owner._buildManager is null)
                {
                    return;
                }

                KubunoLog.WriteLine($"Kubuno: building '{_project.ManifestPath}' for the designer preview.");
                var hr = _owner._buildManager.StartSimpleUpdateProjectConfiguration(
                    _hierarchy, null, null, (uint)VSSOLNBUILDUPDATEFLAGS.SBF_OPERATION_BUILD, 0, 0);
                if (ErrorHandler.Failed(hr))
                {
                    KubunoLog.WriteLine($"Kubuno: could not start the project build (0x{hr:X8}) - a build may already be running.");
                }
            }

            public void ReportRejected(string message)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (!_current.IsProjectRuntime)
                {
                    return; // The bundled surface is not rebuilt here; the message says what is wrong.
                }

                KubunoLog.WriteLine("Kubuno: design surface refused (" + message + "); back to the bundled runtime until the project is built again.");
                DesignSurfaceBuilder.Forget(_project);
                Set(_owner._bundled, DesignSurfaceRuntimeState.Failed, message);
            }

            private async Task ResolveAsync(DesignSurfaceProject project, CancellationTokenSource cancellation)
            {
                try
                {
                    await TaskScheduler.Default;
                    var reused = DesignSurfaceBuilder.TryReuse(project);
                    if (reused is not null)
                    {
                        await _owner._joinableTaskFactory.SwitchToMainThreadAsync();
                        SetProject(reused);
                        return;
                    }

                    if (!DesignSurfaceBuilder.IsProjectBuilt(project))
                    {
                        await _owner._joinableTaskFactory.SwitchToMainThreadAsync();
                        Set(_owner._bundled, DesignSurfaceRuntimeState.NotBuilt, null);
                        return;
                    }

                    await _owner._joinableTaskFactory.SwitchToMainThreadAsync();
                    Set(_current, DesignSurfaceRuntimeState.Building, null);
                    KubunoLog.WriteLine($"Kubuno: design build for '{project.ManifestPath}' ({project.Profile}).");
                    await TaskScheduler.Default;
                    var log = new LogProgress();
                    var result = await new DesignSurfaceBuilder(new ProcessRunner()).BuildAsync(project, log, cancellation.Token);
                    await _owner._joinableTaskFactory.SwitchToMainThreadAsync();
                    switch (result.Status)
                    {
                        case DesignSurfaceBuildStatus.Ready when result.Build is not null:
                            SetProject(result.Build);
                            break;
                        case DesignSurfaceBuildStatus.NotApplicable:
                            KubunoLog.WriteLine("Kubuno: the designer keeps the bundled runtime: " + result.Message + ".");
                            Set(_owner._bundled, DesignSurfaceRuntimeState.NotApplicable, result.Message);
                            break;
                        case DesignSurfaceBuildStatus.Canceled:
                            Set(_owner._bundled, DesignSurfaceRuntimeState.NotBuilt, null);
                            break;
                        default:
                            KubunoLog.WriteLine("Kubuno: design build failed: " + result.Message + ".");
                            Set(_owner._bundled, DesignSurfaceRuntimeState.Failed, result.Message);
                            break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    KubunoLog.WriteException("Kubuno: design build", ex);
                    await _owner._joinableTaskFactory.SwitchToMainThreadAsync();
                    Set(_owner._bundled, DesignSurfaceRuntimeState.Failed, ex.Message);
                }
                finally
                {
                    await _owner._joinableTaskFactory.SwitchToMainThreadAsync();
                    if (ReferenceEquals(_build, cancellation))
                    {
                        _build = null;
                    }

                    cancellation.Dispose();
                    if (_pending)
                    {
                        _pending = false;
                        Resolve();
                    }
                }
            }

            private void SetProject(DesignSurfaceBuild build)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Set(new DesignSurfaceRuntime(build.ExePath, isProjectRuntime: true, expectedUiDllSha256: build.UiDllSha256), DesignSurfaceRuntimeState.Project, null);
            }

            private void Set(DesignSurfaceRuntime runtime, DesignSurfaceRuntimeState state, string? detail)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (runtime.IsSameAs(_current) && state == _state && string.Equals(detail, _detail, StringComparison.Ordinal))
                {
                    return;
                }

                if (runtime.IsProjectRuntime && !runtime.IsSameAs(_current))
                {
                    KubunoLog.WriteLine($"Kubuno: the designer now renders with the project's kubuno_ui.dll ({runtime.ExePath}).");
                }

                _current = runtime;
                _state = state;
                _detail = detail;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Writes each design build line to the Kubuno output pane at once, in order (no SynchronizationContext hop).</summary>
    internal sealed class LogProgress : IProgress<string>
    {
        public void Report(string value) => KubunoLog.WriteLine(value);
    }

    /// <summary>Reads a <c>.rsproj</c>'s cargo build settings (the SDK's evaluated properties) for the design build.</summary>
    internal static class RsprojProperties
    {
        public static DesignSurfaceProject? Read(IVsHierarchy hierarchy, IVsSolutionBuildManager2? buildManager)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (hierarchy is not IVsBuildPropertyStorage storage)
            {
                return null;
            }

            var configuration = ActiveConfiguration(hierarchy, buildManager);
            string? Get(string name)
            {
                if (configuration is not null && storage.GetPropertyValue(name, configuration, (uint)_PersistStorageType.PST_PROJECT_FILE, out var value) == VSConstants.S_OK)
                {
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }

                return storage.GetPropertyValue(name, null, (uint)_PersistStorageType.PST_PROJECT_FILE, out var fallback) == VSConstants.S_OK && !string.IsNullOrWhiteSpace(fallback)
                    ? fallback
                    : null;
            }

            var manifest = Get("CargoManifestPath");
            if (manifest is null)
            {
                return null; // Not a .rsproj.
            }

            return new DesignSurfaceProject(manifest, Get("CargoProfile") ?? "debug")
            {
                ProfileDirectoryName = Get("CargoProfileDirName"),
                Package = Get("CargoPackage"),
                Bin = Get("CargoBin"),
                TargetDirectory = Get("CargoTargetDir"),
                ExpectedTargetDirectory = Get("_KubunoEffectiveTargetDir"),
                ExtraArgs = DesignSurfaceProject.SplitExtraArgs(Get("CargoExtraArgs")),
            };
        }

        /// <summary>The project's active configuration canonical name ("Debug|x64"), or null.</summary>
        private static string? ActiveConfiguration(IVsHierarchy hierarchy, IVsSolutionBuildManager2? buildManager)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (buildManager is null)
            {
                return null;
            }

            var configs = new IVsProjectCfg[1];
            return ErrorHandler.Succeeded(buildManager.FindActiveProjectCfg(IntPtr.Zero, IntPtr.Zero, hierarchy, configs))
                && configs[0] is { } config && ErrorHandler.Succeeded(config.get_CanonicalName(out var name))
                ? name
                : null;
        }
    }
}

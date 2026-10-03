using System;

namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>
    /// Which design surface exe a designer pane runs (docs/DESIGNER.md section 15): the one statically
    /// linked against the project's own <c>kubuno_desktop_ui</c> build (<see cref="IsProjectRuntime"/>), or the
    /// fallback bundled with the extension (<c>tools\surface\</c>) while the project has not been built.
    /// </summary>
    public sealed class DesignSurfaceRuntime
    {
        public DesignSurfaceRuntime(string exePath, bool isProjectRuntime)
        {
            ExePath = exePath ?? throw new ArgumentNullException(nameof(exePath));
            IsProjectRuntime = isProjectRuntime;
        }

        public string ExePath { get; }

        public bool IsProjectRuntime { get; }

        /// <summary>EVT-7b: the registry the project surface exported after its design build (its linked project controls), when known.</summary>
        public string? RegistryPath { get; set; }

        /// <summary>EVT-7b: the project crate the surface links (its controls render for real), when it does.</summary>
        public string? ProjectCrate { get; set; }

        /// <summary>The project's name (its <c>.rsproj</c>), for the Toolbox's "&lt;Project&gt; Composants" tab.</summary>
        public string? ProjectName { get; set; }

        /// <summary>A stable key of the project (its manifest), under which its Toolbox choices are kept.</summary>
        public string? ProjectKey { get; set; }

        public bool IsSameAs(DesignSurfaceRuntime? other) =>
            other is not null && string.Equals(ExePath, other.ExePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What the designer's info bar says about the runtime (docs/DESIGNER.md section 15).</summary>
    public enum DesignSurfaceRuntimeState
    {
        /// <summary>The project's own runtime is in use: no info bar.</summary>
        Project,

        /// <summary>Bundled runtime: the project has not been built yet ("Générer").</summary>
        NotBuilt,

        /// <summary>Bundled runtime while the design build runs ("Annuler").</summary>
        Building,

        /// <summary>Bundled runtime: the design build failed ("Réessayer").</summary>
        Failed,

        /// <summary>Bundled runtime: the document is not in a project that uses kubuno-desktop-views (no action).</summary>
        NotApplicable,

        /// <summary>
        /// The project's runtime, but a control of the project (a user control's view, a control's code) changed since it
        /// was built: the views using it show its previous version until the project is built again ("Générer").
        /// </summary>
        OutOfDate,
    }

    /// <summary>
    /// The runtime of one project configuration, shared by every designer pane of that project. Raises
    /// <see cref="Changed"/> (on any thread) when <see cref="Current"/> or <see cref="State"/> changes -
    /// a pane then restarts its surface on the new exe (hot swap, selection kept) and updates its bar.
    /// </summary>
    public interface IDesignSurfaceRuntimeSource
    {
        DesignSurfaceRuntime? Current { get; }

        DesignSurfaceRuntimeState State { get; }

        /// <summary>A short detail for the bar (e.g. why the build failed); may be null.</summary>
        string? Detail { get; }

        event EventHandler? Changed;

        /// <summary>The bar's action: build the project (<see cref="DesignSurfaceRuntimeState.NotBuilt"/>/<see cref="DesignSurfaceRuntimeState.Failed"/>) or cancel the design build (<see cref="DesignSurfaceRuntimeState.Building"/>).</summary>
        void RunAction();

        /// <summary>A pane reports that a surface refused the handshake (another protocol version): the source rebuilds.</summary>
        void ReportRejected(string message);
    }

    /// <summary>
    /// The document a designer pane edits, handed to <see cref="IDesignSurfaceHostFactory.Create(DesignSurfaceDocument)"/>
    /// so the factory can find the project (and hence the runtime) it belongs to.
    /// </summary>
    public sealed class DesignSurfaceDocument
    {
        public DesignSurfaceDocument(string path, object? hierarchy, uint itemId)
        {
            Path = path ?? string.Empty;
            Hierarchy = hierarchy;
            ItemId = itemId;
        }

        public string Path { get; }

        /// <summary>The owning <c>IVsHierarchy</c> (typed object: see <see cref="VsFilterKeysBridge"/> for why VS types stay out of signatures here).</summary>
        public object? Hierarchy { get; }

        public uint ItemId { get; }
    }

    /// <summary>Gives a pane the runtime source of its document's project; set by the VSIX.</summary>
    public interface IDesignSurfaceRuntimeProvider
    {
        /// <summary>A source for <paramref name="document"/> (never null: a document outside a project gets a bundled-only source). Release it with <see cref="IDisposable.Dispose"/> of the returned lease.</summary>
        DesignSurfaceRuntimeLease Acquire(DesignSurfaceDocument document);
    }

    /// <summary>A pane's reference to a shared <see cref="IDesignSurfaceRuntimeSource"/>.</summary>
    public sealed class DesignSurfaceRuntimeLease : IDisposable
    {
        private Action? _release;

        public DesignSurfaceRuntimeLease(IDesignSurfaceRuntimeSource source, Action? release)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            _release = release;
        }

        public IDesignSurfaceRuntimeSource Source { get; }

        public void Dispose()
        {
            var release = _release;
            _release = null;
            release?.Invoke();
        }
    }

    /// <summary>A source that always runs one fixed exe (the bundled surface, tests, the spike).</summary>
    public sealed class FixedDesignSurfaceRuntimeSource : IDesignSurfaceRuntimeSource
    {
        public FixedDesignSurfaceRuntimeSource(DesignSurfaceRuntime runtime, DesignSurfaceRuntimeState state = DesignSurfaceRuntimeState.Project, string? detail = null)
        {
            Current = runtime ?? throw new ArgumentNullException(nameof(runtime));
            State = state;
            Detail = detail;
        }

        public DesignSurfaceRuntime? Current { get; }

        public DesignSurfaceRuntimeState State { get; }

        public string? Detail { get; }

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public void RunAction()
        {
        }

        public void ReportRejected(string message)
        {
        }
    }
}

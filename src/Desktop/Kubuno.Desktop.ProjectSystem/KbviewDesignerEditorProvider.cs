using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem;
using Kubuno.Rust.ProjectSystem;

namespace Kubuno.Desktop.ProjectSystem
{
    /// <summary>
    /// Makes the Kubuno View Designer the DEFAULT editor of <c>.kbview</c> files inside a <c>.rsproj</c>,
    /// so a double-click in Solution Explorer opens <c>main_view.kbview [Design]</c> like a WinForms form -
    /// while the same file in Open Folder (or outside any project) keeps opening in the plain XML editor
    /// (the global per-extension default, languages.pkgdef).
    ///
    /// The supported CPS seam for "project-specific default editor" is
    /// <see cref="IProjectSpecificEditorProvider"/> (checked by decompiling the installed
    /// <c>Microsoft.VisualStudio.ProjectSystem.VS.Implementation.dll</c>: <c>FileDocumentManager</c> opens an
    /// item with the provider's <see cref="IProjectSpecificEditorInfo.EditorFactory"/> and, for the
    /// Primary view, its <see cref="IProjectSpecificEditorInfo.DefaultView"/> when
    /// <see cref="IProjectSpecificEditorInfo.IsDefaultEditor"/> is set; <c>VsProjectSpecificEditorMap</c>
    /// also lists it in "Open With..." under <see cref="IProjectSpecificEditorInfo.DisplayName"/>). CPS
    /// asks the providers in preference order (higher <see cref="OrderAttribute"/> first) and takes the first
    /// non-null answer, so this one returns null for every other extension.
    /// </summary>
    [Export(typeof(IProjectSpecificEditorProvider))]
    [AppliesTo(RustProjectCapabilities.RustProjectSystem)]
    [Order(1000)]
    internal sealed class KbviewDesignerEditorProvider : IProjectSpecificEditorProvider
    {
        /// <summary>Kubuno.Desktop.Designer's <c>DesignerConstants.EditorFactoryGuidString</c> (this assembly does not reference the designer library - keep in sync).</summary>
        internal static readonly Guid DesignerEditorFactory = new Guid("1DCF5C91-A51E-4DE8-B066-680D6636C094");

        /// <summary><c>VSConstants.LOGVIEWID_Designer</c> (verified by reflection on Microsoft.VisualStudio.Shell.15.0).</summary>
        internal static readonly Guid LogicalViewDesigner = new Guid("7651a702-06e5-11d1-8ebd-00a0c90f26ea");

        public Task<IProjectSpecificEditorInfo?> GetSpecificEditorAsync(string documentMoniker)
        {
            var isView = !string.IsNullOrEmpty(documentMoniker) &&
                string.Equals(Path.GetExtension(documentMoniker), ".kbview", StringComparison.OrdinalIgnoreCase);
            return Task.FromResult<IProjectSpecificEditorInfo?>(isView ? EditorInfo.Instance : null);
        }

        public Task<bool> SetUseGlobalEditorAsync(string documentMoniker, bool setUseGlobalEditor) => Task.FromResult(false);

        private sealed class EditorInfo : IProjectSpecificEditorInfo
        {
            internal static readonly EditorInfo Instance = new EditorInfo();

            public Guid EditorFactory => DesignerEditorFactory;

            public bool IsDefaultEditor => true;

            public string DisplayName => "Kubuno View Designer";

            public Guid DefaultView => LogicalViewDesigner;
        }
    }
}

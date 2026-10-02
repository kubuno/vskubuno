using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem;
using Kubuno.Rust.ProjectSystem;

namespace Kubuno.Desktop.ProjectSystem
{
    /// <summary>
    /// Makes the Kubuno Settings Editor the DEFAULT editor of <c>.kbsettings</c> files inside a <c>.rsproj</c> (a
    /// double-click in Solution Explorer opens the grid, like Windows Forms' Settings.settings; docs/STORAGE-COMPONENTS.md).
    /// Same CPS seam as <see cref="KbresEditorProvider"/>; the editor factory itself is registered for the extension
    /// everywhere by the attributes on KubunoPackage.
    /// </summary>
    [Export(typeof(IProjectSpecificEditorProvider))]
    [AppliesTo(RustProjectCapabilities.RustProjectSystem)]
    [Order(1000)]
    internal sealed class KbsettingsEditorProvider : IProjectSpecificEditorProvider
    {
        /// <summary>Kubuno.Views' <c>KbsettingsEditorFactory.GuidString</c> (this assembly does not reference it - keep in sync).</summary>
        internal static readonly Guid SettingsEditorFactory = new Guid("5C2E9A47-1B3D-4E6F-8A90-2D7C4B1E6F38");

        public Task<IProjectSpecificEditorInfo?> GetSpecificEditorAsync(string documentMoniker)
        {
            var isSettings = !string.IsNullOrEmpty(documentMoniker) &&
                string.Equals(Path.GetExtension(documentMoniker), ".kbsettings", StringComparison.OrdinalIgnoreCase);
            return Task.FromResult<IProjectSpecificEditorInfo?>(isSettings ? EditorInfo.Instance : null);
        }

        public Task<bool> SetUseGlobalEditorAsync(string documentMoniker, bool setUseGlobalEditor) => Task.FromResult(false);

        private sealed class EditorInfo : IProjectSpecificEditorInfo
        {
            internal static readonly EditorInfo Instance = new EditorInfo();

            public Guid EditorFactory => SettingsEditorFactory;

            public bool IsDefaultEditor => true;

            public string DisplayName => "Kubuno Settings Editor";

            public Guid DefaultView => KbviewDesignerEditorProvider.LogicalViewDesigner;
        }
    }
}

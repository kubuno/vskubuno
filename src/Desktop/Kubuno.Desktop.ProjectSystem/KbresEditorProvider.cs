using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem;
using Kubuno.Rust.ProjectSystem;

namespace Kubuno.Desktop.ProjectSystem
{
    /// <summary>
    /// Makes the Kubuno Resource Editor the DEFAULT editor of <c>.kbres</c> files inside a <c>.rsproj</c> (a double-click
    /// in Solution Explorer opens the grid / thumbnails, like a .resx). Same CPS seam as
    /// <see cref="KbviewDesignerEditorProvider"/>; the editor factory itself is registered for the extension
    /// everywhere (Open Folder, outside any project) by the attributes on KubunoPackage.
    /// </summary>
    [Export(typeof(IProjectSpecificEditorProvider))]
    [AppliesTo(RustProjectCapabilities.RustProjectSystem)]
    [Order(1000)]
    internal sealed class KbresEditorProvider : IProjectSpecificEditorProvider
    {
        /// <summary>Kubuno.Desktop's <c>KbresEditorFactory.GuidString</c> (this assembly does not reference it - keep in sync).</summary>
        internal static readonly Guid ResourceEditorFactory = new Guid("7E4B2A19-3C5D-4F86-9A21-B5D8E0C47F63");

        public Task<IProjectSpecificEditorInfo?> GetSpecificEditorAsync(string documentMoniker)
        {
            var isResource = !string.IsNullOrEmpty(documentMoniker) &&
                string.Equals(Path.GetExtension(documentMoniker), ".kbres", StringComparison.OrdinalIgnoreCase);
            return Task.FromResult<IProjectSpecificEditorInfo?>(isResource ? EditorInfo.Instance : null);
        }

        public Task<bool> SetUseGlobalEditorAsync(string documentMoniker, bool setUseGlobalEditor) => Task.FromResult(false);

        private sealed class EditorInfo : IProjectSpecificEditorInfo
        {
            internal static readonly EditorInfo Instance = new EditorInfo();

            public Guid EditorFactory => ResourceEditorFactory;

            public bool IsDefaultEditor => true;

            public string DisplayName => "Kubuno Resource Editor";

            public Guid DefaultView => KbviewDesignerEditorProvider.LogicalViewDesigner;
        }
    }
}

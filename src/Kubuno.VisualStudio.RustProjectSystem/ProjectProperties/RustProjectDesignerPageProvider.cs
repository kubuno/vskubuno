using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.VS.Properties;

namespace Kubuno.VisualStudio.RustProjectSystem.ProjectProperties
{
    /// <summary>
    /// Opts a <c>.rsproj</c> into the modern Project Properties editor (the searchable document tab
    /// .NET SDK projects open), docs/RSPROJ.md "Project properties like .NET" addendum.
    ///
    /// Verified by decompiling the installed Visual Studio assemblies: "Properties" on a project node
    /// opens the editor the hierarchy reports for <c>VSHPROPID_ProjectDesignerEditor</c>. CPS's
    /// <c>ProjectNode</c> reports the App Designer (<c>GUID_ProjectDesignerEditor</c>) only when at least
    /// one <see cref="IVsProjectDesignerPageProvider"/> applies to the project
    /// (<c>VsProjectDesignerPageService.IsProjectDesignerSupported</c>); otherwise it falls back to the
    /// legacy modal property-page frame. The App Designer's own editor factory then forwards to the
    /// new editor (<c>ProjectPropertiesEditorFactory</c>, {990036EB-F67A-4B8A-93D4-4663DB2A1033}) when
    /// the project has the <c>ProjectPropertiesEditor</c> capability, which Kubuno.Rust.Sdk declares.
    /// The new editor itself lists every <c>PageTemplate="generic"</c> rule of the project context
    /// (through the Project Query API), so this provider needs no COM property pages of its own.
    /// </summary>
    [Export(typeof(IVsProjectDesignerPageProvider))]
    [AppliesTo(RustProjectCapabilities.RustProjectSystem)]
    internal sealed class RustProjectDesignerPageProvider : IVsProjectDesignerPageProvider
    {
        private static readonly Task<IReadOnlyCollection<IPageMetadata>> NoPages =
            Task.FromResult<IReadOnlyCollection<IPageMetadata>>(Array.Empty<IPageMetadata>());

        public Task<IReadOnlyCollection<IPageMetadata>> GetPagesAsync() => NoPages;
    }
}

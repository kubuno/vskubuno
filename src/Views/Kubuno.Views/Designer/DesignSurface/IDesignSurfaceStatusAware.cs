using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Kubuno.Views.Logging;

namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>
    /// A host that reports what its preview shows (docs/DESIGNER.md section 17) - what
    /// <see cref="UI.DesignerSplitView"/> reads to show the error banner and the "preview keeps crashing" bar.
    /// </summary>
    public interface IDesignSurfaceStatusAware
    {
        /// <summary>The last <c>renderStatus</c> the surface sent (kept across a restart until the new surface sends its own).</summary>
        DesignSurfaceRenderStatus? RenderStatus { get; }

        /// <summary>Raised on the UI thread when <see cref="RenderStatus"/> changes.</summary>
        event EventHandler? RenderStatusChanged;

        /// <summary>The surface exited unexpectedly several times in a row.</summary>
        bool IsFailingRepeatedly { get; }

        /// <summary>Raised on the UI thread when <see cref="IsFailingRepeatedly"/> changes.</summary>
        event EventHandler? HealthChanged;

        /// <summary>Raised on the UI thread when a marker of the surface asks to show a finding in the XML (<c>goToSource</c>).</summary>
        event EventHandler<DesignSurfaceDiagnostic>? SourceNavigationRequested;

        /// <summary>Starts the surface again now (the « Relancer » action).</summary>
        void Restart();
    }
}

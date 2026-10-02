using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Kubuno.Views.Designer;
using Kubuno.Views.Designer.DesignSurface;

namespace Kubuno.Desktop.Designer.DesignSurface
{
    /// <summary>
    /// The designer's zoom (like the XAML designer's zoom box): a factor, or « fit » (0), which the surface turns into
    /// the largest factor up to 100 % that shows the whole designed window. Sent as <c>setZoom</c>, and again when the
    /// surface process restarts.
    /// </summary>
    public sealed partial class RustDesignSurfaceHost
    {
        private double _zoom = 1.0;

        /// <summary>The zoom asked for: a factor (1 = 100 %), 0 for « fit ».</summary>
        public double Zoom => _zoom;

        /// <summary>Sets the zoom (a factor, 0 for « fit »).</summary>
        public void SetZoom(double zoom)
        {
            _zoom = zoom < 0 ? 1.0 : zoom;
            SendZoom();
        }

        private void SendZoom() => SendLine(DesignSurfaceZoomProtocol.EncodeSetZoom(_zoom));
    }
}

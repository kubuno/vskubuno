using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

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

    /// <summary>The pure half of <c>setZoom</c>.</summary>
    public static class DesignSurfaceZoomProtocol
    {
        /// <summary>The choices the zoom box offers: « fit » (0) and the usual factors.</summary>
        public static readonly double[] Choices = { 0, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };

        public static string EncodeSetZoom(double zoom) => "{\"type\":\"setZoom\",\"zoom\":" + zoom.ToString("0.###", CultureInfo.InvariantCulture) + "}";

        /// <summary>What the zoom box shows for <paramref name="zoom"/>.</summary>
        public static string Label(double zoom) => zoom <= 0
            ? (DesignerText.IsFrench ? "Ajuster" : "Fit")
            : (zoom * 100).ToString("0", CultureInfo.InvariantCulture) + " %";
    }

    /// <summary>The zoom box in the Design | XML | Split strip.</summary>
    internal static class DesignZoomPicker
    {
        /// <summary>Adds the zoom box at the end of <paramref name="strip"/> when <paramref name="host"/> is a real design surface.</summary>
        public static void Attach(Panel strip, IDesignSurfaceHost host)
        {
            if (host is not RustDesignSurfaceHost surface)
            {
                return;
            }

            var label = new TextBlock { Text = DesignerText.IsFrench ? "Zoom :" : "Zoom:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 6, 0) };
            label.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            var combo = new ComboBox { MinWidth = 80, Margin = new Thickness(0, 4, 4, 4), VerticalAlignment = VerticalAlignment.Center };
            combo.SetResourceReference(FrameworkElement.StyleProperty, VsResourceKeys.ComboBoxStyleKey);
            System.Windows.Automation.AutomationProperties.SetName(combo, "Zoom");
            foreach (var choice in DesignSurfaceZoomProtocol.Choices)
            {
                combo.Items.Add(new ComboBoxItem { Content = DesignSurfaceZoomProtocol.Label(choice), Tag = choice });
            }

            combo.SelectedIndex = System.Array.IndexOf(DesignSurfaceZoomProtocol.Choices, 1.0);
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem { Tag: double zoom })
                {
                    surface.SetZoom(zoom);
                }
            };
            strip.Children.Add(label);
            strip.Children.Add(combo);
        }
    }
}

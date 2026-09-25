using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Kubuno.VisualStudio.Designer.DesignSurface
{
    /// <summary>
    /// The stand-in for the Design pane until DSG-7 embeds the real <c>kubuno-views-designer</c>
    /// surface (see <see cref="IDesignSurfaceHost"/>'s own remarks for the full seam contract). Shows a
    /// short, honest status message rather than a blank panel or a fake preview, so opening a
    /// <c>.kbview</c> file today never looks broken or silently wrong - it looks exactly like what it
    /// is: a real split editor whose Design half is not implemented yet.
    /// </summary>
    public sealed class PlaceholderDesignSurfaceHost : IDesignSurfaceHost
    {
        private readonly Grid _root;

        public PlaceholderDesignSurfaceHost()
        {
            _root = new Grid
            {
                Background = SystemColors.ControlBrush,
            };

            var message = new TextBlock
            {
                Text = "Design surface placeholder\n\n" +
                       "The embedded kubuno-views-designer render surface is not wired up yet " +
                       "(docs/DESIGNER.md §3/§7, work package DSG-7). Edit the XML pane on the " +
                       "right - it is the real Visual Studio text editor.",
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SystemColors.GrayTextBrush,
                MaxWidth = 360,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(24),
            };
            _root.Children.Add(message);
        }

        public FrameworkElement Content => _root;

        public void SetDocumentText(string xmlText)
        {
            // Intentionally a no-op: the placeholder never renders the document. A real
            // IDesignSurfaceHost (DSG-7) forwards this to kubuno-views-designer's `kubuno/setBuffer`.
        }

#pragma warning disable CS0067 // never raised by the placeholder; part of the interface contract for real hosts.
        public event EventHandler<DesignSurfaceSelectionChangedEventArgs>? SelectionChanged;
#pragma warning restore CS0067

        public void Dispose()
        {
            // No unmanaged/process resources to release for a plain WPF placeholder.
        }
    }
}

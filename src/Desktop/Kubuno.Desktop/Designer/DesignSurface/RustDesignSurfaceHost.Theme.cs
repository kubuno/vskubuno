using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Kubuno.Views.Logging;
using Kubuno.Views.Designer;
using Kubuno.Views.Designer.DesignSurface;

namespace Kubuno.Desktop.Designer.DesignSurface
{
    /// <summary>
    /// The canvas around the edited view follows Visual Studio's theme, live: the surface is told the
    /// theme's designer background (<c>setCanvasBackground</c>, <c>kubuno_desktop_views::protocol::HostMessage::SetCanvasBackground</c>)
    /// when it starts and again whenever the theme changes, like the WinForms designer's own canvas.
    /// </summary>
    public sealed partial class RustDesignSurfaceHost
    {
        private static readonly object ThemeLock = new object();
        private static readonly List<WeakReference<RustDesignSurfaceHost>> ThemedHosts = new List<WeakReference<RustDesignSurfaceHost>>();
        private static bool _themeSubscribed;

        /// <summary>Sends the current theme's designer background to the surface, and keeps sending it on every theme change.</summary>
        private void SendCanvasTheme()
        {
            var color = CurrentCanvasColor();
            if (color is null)
            {
                return;
            }

            SendLine(DesignSurfaceThemeProtocol.EncodeSetCanvasBackground(color));
            lock (ThemeLock)
            {
                ThemedHosts.RemoveAll(w => !w.TryGetTarget(out var h) || ReferenceEquals(h, this));
                ThemedHosts.Add(new WeakReference<RustDesignSurfaceHost>(this));
                if (!_themeSubscribed)
                {
                    _themeSubscribed = true;
                    Microsoft.VisualStudio.PlatformUI.VSColorTheme.ThemeChanged += _ => OnThemeChanged();
                }
            }
        }

        /// <summary>Visual Studio switched theme: every live surface gets the new canvas colour.</summary>
        private static void OnThemeChanged()
        {
            var color = CurrentCanvasColor();
            if (color is null)
            {
                return;
            }

            List<RustDesignSurfaceHost> hosts = new List<RustDesignSurfaceHost>();
            lock (ThemeLock)
            {
                ThemedHosts.RemoveAll(w => !w.TryGetTarget(out _));
                foreach (var w in ThemedHosts)
                {
                    if (w.TryGetTarget(out var h))
                    {
                        hosts.Add(h);
                    }
                }
            }

            foreach (var h in hosts)
            {
                if (h._surface != null)
                {
                    h.SendLine(DesignSurfaceThemeProtocol.EncodeSetCanvasBackground(color));
                }
            }
        }

        /// <summary>The theme's designer background as <c>#RRGGBB</c>; null outside Visual Studio (tests).</summary>
        private static string? CurrentCanvasColor()
        {
            try
            {
                var c = Microsoft.VisualStudio.PlatformUI.VSColorTheme.GetThemedColor(Microsoft.VisualStudio.PlatformUI.EnvironmentColors.DesignerBackgroundColorKey);
                return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or TypeInitializationException or System.IO.FileNotFoundException or MissingMethodException)
            {
                KubunoViewsLogHost.Current.WriteLine("[designer] no Visual Studio theme for the canvas: " + ex.Message);
                return null;
            }
        }
    }
}

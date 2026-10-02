using System;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Core.UI
{
    /// <summary>The current Visual Studio theme, for the colors the extension defines itself (classifications, icons).</summary>
    public static class VsTheme
    {
        /// <summary>True when the current theme is a dark one (its tool window background is dark).</summary>
        public static bool IsDark()
        {
            try
            {
                var background = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
                return (background.R * 0.299) + (background.G * 0.587) + (background.B * 0.114) < 128;
            }
            catch (Exception exception) when (exception is InvalidOperationException or NullReferenceException)
            {
                return false;
            }
        }
    }
}

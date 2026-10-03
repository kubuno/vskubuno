using System;
using System.IO;

namespace Kubuno.Desktop.DesignerIntegration
{
    /// <summary>
    /// Finds <c>kubuno-views-surface.exe</c> (DSG-6/DSG-7's design surface, built from
    /// <c>kubuno-views/examples/view_embed.rs</c> - see <c>Kubuno.Views.Designer</c>'s own
    /// INTEGRATION.md §6): the extension's own <c>tools\surface\</c> folder first (where this VSIX
    /// ships it once packaged - a dedicated subfolder, see the csproj's own comment on the shipping
    /// <c>Content</c> items), then a local dev build folder -
    /// mirroring <c>Kubuno.Views.Locating.KubunoViewsLanguageServerLocator</c>'s own
    /// shape, minus the Tools &gt; Options override this library has no options page field for yet.
    /// Never throws; a caller must treat <see langword="null"/> as "not found" (log it, leave the
    /// placeholder design surface in place) rather than fail package load over it. Since docs/DESIGNER.md
    /// section 15 this bundled surface is only the FALLBACK runtime: a designer whose project has been
    /// built renders with a surface statically linked against that project's own kubuno_ui build
    /// (<see cref="ProjectDesignSurfaceRuntimeProvider"/>).
    /// </summary>
    internal static class KubunoViewsSurfaceLocator
    {
        // NOT renamed to "kubuno-views-surface.exe": verified live (inspecting the built .vsix's zip
        // contents) that VSSDK's VSIX packaging step ships a Content item's OWN source filename here,
        // honoring the <Link> metadata's DIRECTORY (tools\surface\) but not its filename - the same
        // "silently drops/renames differently than expected" class of packaging quirk this csproj's
        // own System.Text.Json Content-item comment already documents elsewhere. Simpler to track
        // reality than to keep fighting it.
        private const string ExeName = "view_embed.exe";

        public static string? Locate(string? extensionInstallDirectory, string? devBuildDirectory)
        {
            if (!string.IsNullOrWhiteSpace(extensionInstallDirectory))
            {
                var toolsCandidate = CombineSafely(extensionInstallDirectory!, "tools", "surface", ExeName);
                if (toolsCandidate != null && File.Exists(toolsCandidate))
                {
                    return toolsCandidate;
                }
            }

            if (!string.IsNullOrWhiteSpace(devBuildDirectory))
            {
                var devCandidate = CombineSafely(devBuildDirectory!, ExeName);
                if (devCandidate != null && File.Exists(devCandidate))
                {
                    return devCandidate;
                }
            }

            return null;
        }

        private static string? CombineSafely(string first, string second, string third, string fourth)
        {
            try
            {
                return Path.Combine(first, second, third, fourth);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static string? CombineSafely(string first, string second)
        {
            try
            {
                return Path.Combine(first, second);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}

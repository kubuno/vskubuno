using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using VsImageAttributes = Microsoft.VisualStudio.Imaging.Interop.ImageAttributes;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Kubuno.VisualStudio.Designer.Registry;
using Kubuno.VisualStudio.Views.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.VisualStudio.Designer.Toolbox
{
    /// <summary>
    /// Fills Visual Studio's OWN Toolbox window with the Kubuno components, grouped by registry family
    /// into tabs ("Affichage", "Choix", "Texte", "Conteneurs", "Données"...), like the WinForms designer's
    /// "Contrôles communs"/"Conteneurs" tabs.
    ///
    /// <para><b>Mechanism</b> (the classic, still-supported one third-party designers use - checked by
    /// reflection on the installed <c>Microsoft.VisualStudio.Interop.dll</c>): <c>IVsToolbox.AddTab</c> +
    /// <c>IVsToolbox.AddItem</c> with a data object carrying the private <see cref="ToolboxItemFormat"/>, and
    /// the designer pane implementing <c>IVsToolboxUser</c>. The Toolbox asks the ACTIVE document's
    /// <c>IVsToolboxUser.IsSupported</c> for every item and hides (or greys, with "Show All") the ones it
    /// refuses, so the Kubuno tabs appear only while a <c>.kbview</c> designer is the active document -
    /// no visibility logic of our own. Items are added with <c>TBXIF_DONTPERSIST</c>: they are rebuilt
    /// from the live <c>kubuno/registry</c> in every session (the registry is the truth and may change
    /// with the Kubuno version), and never accumulate as stale duplicates in the persisted toolbox.</para>
    ///
    /// <para><b>Icons</b>: the Solution Explorer element glyph of lot 8 (a <c>KnownMonikers</c> control
    /// image per component, resolved by the VSIX through <see cref="IconMoniker"/>), rendered to a 16x16
    /// bitmap by the image service (the legacy toolbox API only takes an <c>HBITMAP</c>) with a magenta
    /// transparency key.</para>
    /// </summary>
    public static class NativeToolboxInstaller
    {
        private const uint TransparentMagenta = 0x00FF00FF;
        private static bool s_installed;

        /// <summary>Set by the VSIX: the image moniker of a component's toolbox icon (null: no icon).</summary>
        public static Func<string, ImageMoniker?>? IconMoniker { get; set; }

        /// <summary>Whether the Kubuno items were added to the Toolbox in this session.</summary>
        public static bool IsInstalled => s_installed;

        /// <summary>Adds every component of <paramref name="registry"/> once per Visual Studio session (idempotent afterwards). Best-effort: failures are logged, never thrown.</summary>
        public static void EnsureInstalled(ComponentRegistry registry)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (s_installed || registry is null || registry.Components.Count == 0)
            {
                return;
            }

            if (Package.GetGlobalService(typeof(SVsToolbox)) is not IVsToolbox toolbox)
            {
                KubunoViewsLogHost.Current.WriteLine("[designer] Toolbox: SVsToolbox unavailable - the Kubuno components are not added to Visual Studio's Toolbox.");
                return;
            }

            s_installed = true;
            var added = 0;
            foreach (var family in registry.FamilyNames)
            {
                var tabName = DesignerText.ToolboxTabName(family);

                // The tab may already exist (a previous session's, or another designer's tab of the same
                // name, e.g. WinForms' "Conteneurs"): a failure here only means "exists", and AddItem into
                // an existing tab is fine - the IsSupported filtering keeps each designer's items apart.
                toolbox.AddTab(tabName);

                foreach (var component in SortedForToolbox(registry.Families[family]))
                {
                    try
                    {
                        if (AddItem(toolbox, component, tabName))
                        {
                            added++;
                        }
                    }
                    catch (Exception ex) when (ex is COMException or ArgumentException or ExternalException)
                    {
                        KubunoViewsLogHost.Current.WriteException($"[designer] Toolbox: could not add '{component.Name}'", ex);
                    }
                }
            }

            toolbox.UpdateToolboxUI();
            KubunoViewsLogHost.Current.WriteLine($"[designer] Toolbox: added {added} Kubuno component(s) in {registry.FamilyNames.Count} tab(s).");
        }

        private static bool AddItem(IVsToolbox toolbox, ComponentMeta component, string tabName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var data = new OleDataObject();
            data.SetData(ToolboxItemFormat.FormatName, new MemoryStream(ToolboxItemFormat.Encode(component.Name)));

            var bitmap = CreateBitmap(component.Name);
            var info = new TBXITEMINFO
            {
                bstrText = component.Name,
                hBmp = bitmap,
                clrTransparent = TransparentMagenta,
                // DELETEBITMAP: the toolbox takes ownership of the HBITMAP; DONTPERSIST: see the class doc.
                dwFlags = (uint)(__TBXITEMINFOFLAGS.TBXIF_DONTPERSIST | (bitmap != IntPtr.Zero ? __TBXITEMINFOFLAGS.TBXIF_DELETEBITMAP : 0)),
            };

            var hr = toolbox.AddItem(data, new[] { info }, tabName);
            if (ErrorHandler.Failed(hr))
            {
                KubunoViewsLogHost.Current.WriteLine($"[designer] Toolbox: AddItem('{component.Name}') failed (0x{hr:X8}).");
                return false;
            }

            return true;
        }

        /// <summary>16x16 HBITMAP of the component's moniker on a magenta key, or <see cref="IntPtr.Zero"/> (the toolbox then shows its generic glyph).</summary>
        private static IntPtr CreateBitmap(string componentName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (IconMoniker?.Invoke(componentName) is not { } moniker ||
                    Package.GetGlobalService(typeof(SVsImageService)) is not IVsImageService2 imageService)
                {
                    return IntPtr.Zero;
                }

                // The toolbox background: the image service picks the Light/Dark/HighContrast variant of the
                // vector icon from it, and the icon is blended onto it below (anti-aliased edges, no halo).
                var background = Microsoft.VisualStudio.PlatformUI.VSColorTheme.GetThemedColor(Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolboxBackgroundColorKey);
                var attributes = new VsImageAttributes
                {
                    StructSize = Marshal.SizeOf(typeof(VsImageAttributes)),
                    Flags = unchecked((uint)(_ImageAttributesFlags.IAF_RequiredFlags | _ImageAttributesFlags.IAF_Background)),
                    // RGBA packing (R in the low byte), as Microsoft.VisualStudio.PlatformUI's ToColorFromRgba decodes it.
                    Background = 0xFF000000u | ((uint)background.B << 16) | ((uint)background.G << 8) | background.R,
                    ImageType = (uint)_UIImageType.IT_Bitmap,
                    Format = (uint)_UIDataFormat.DF_WinForms,
                    LogicalWidth = 16,
                    LogicalHeight = 16,
                    Dpi = 96,
                };

                var uiObject = imageService.GetImage(moniker, attributes);
                if (uiObject is null || ErrorHandler.Failed(uiObject.get_Data(out var data)) || data is not Bitmap source)
                {
                    return IntPtr.Zero;
                }

                // The legacy toolbox API takes an opaque HBITMAP plus a transparency key: blend the anti-aliased
                // icon onto the toolbox background instead of thresholding its alpha (which gave jagged edges).
                using var opaque = new Bitmap(16, 16, PixelFormat.Format24bppRgb);
                for (var y = 0; y < 16; y++)
                {
                    for (var x = 0; x < 16; x++)
                    {
                        var pixel = x < source.Width && y < source.Height ? source.GetPixel(x, y) : Color.Transparent;
                        var alpha = pixel.A / 255.0;
                        opaque.SetPixel(x, y, Color.FromArgb(
                            (int)Math.Round((pixel.R * alpha) + (background.R * (1 - alpha))),
                            (int)Math.Round((pixel.G * alpha) + (background.G * (1 - alpha))),
                            (int)Math.Round((pixel.B * alpha) + (background.B * (1 - alpha)))));
                    }
                }

                return opaque.GetHbitmap();
            }
            catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or ExternalException)
            {
                KubunoViewsLogHost.Current.WriteException($"[designer] Toolbox: no icon for '{componentName}'", ex);
                return IntPtr.Zero;
            }
        }

        /// <summary>Alphabetical inside a tab, like the WinForms Toolbox (Visual Studio adds the "Pointer" entry on top of each tab itself).</summary>
        public static IEnumerable<ComponentMeta> SortedForToolbox(IEnumerable<ComponentMeta> components) =>
            components.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase);

        /// <summary>The component names of <paramref name="registry"/> in toolbox order (tests / diagnostics).</summary>
        public static IEnumerable<(string Tab, string Component)> Layout(ComponentRegistry registry)
        {
            foreach (var family in registry.FamilyNames)
            {
                foreach (var component in SortedForToolbox(registry.Families[family]))
                {
                    yield return (DesignerText.ToolboxTabName(family), component.Name);
                }
            }
        }
    }
}

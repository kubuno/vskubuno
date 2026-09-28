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
    /// <para><b>Icons</b>: the Kubuno control icon of each component (the same Lucide-based XAML as its
    /// Solution Explorer element node, <c>KubunoControls.imagemanifest</c>), rendered to a 16x16 bitmap on a
    /// magenta transparency key (the legacy toolbox API only takes an <c>HBITMAP</c>) - see
    /// <see cref="CreateBitmap"/>.</para>
    /// </summary>
    public static class NativeToolboxInstaller
    {
        private const uint TransparentMagenta = 0x00FF00FF;
        private static bool s_installed;
        private static int s_iconFailures;
        // Items typed as object: a VS type in a static field initialiser would load the Shell assembly in unit tests (Layout).
        private static readonly List<(object Data, string Component)> s_items = new List<(object, string)>();

        /// <summary>Set by the VSIX: the base name of a component's icon XAML (e.g. "Button", "Control" for an unknown component; null: no icon).</summary>
        public static Func<string, string?>? IconName { get; set; }

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

            // Icons are rendered for the current theme; re-render them when it changes (like VS's own).
            Microsoft.VisualStudio.PlatformUI.VSColorTheme.ThemeChanged += _ => RefreshIcons();
            toolbox.UpdateToolboxUI();
            KubunoViewsLogHost.Current.WriteLine($"[designer] Toolbox: added {added} Kubuno component(s) in {registry.FamilyNames.Count} tab(s), {added - s_iconFailures} with their icon.");
            if (s_iconFailures > 0)
            {
                RetryMissingIconsLater();
            }
        }

        /// <summary>
        /// Safety net: an icon that could not be rendered at install time (e.g. its resource assembly not
        /// loadable yet) is retried in the background for 30 s instead of leaving the Toolbox's placeholder glyph.
        /// </summary>
        private static void RetryMissingIconsLater()
        {
#pragma warning disable VSSDK007 // no package-owned JoinableTaskFactory reachable from this static installer - same precedent as DesignSurfaceEditingCoordinator.
            ThreadHelper.JoinableTaskFactory.RunAsync(RetryMissingIconsAsync).FileAndForget("Kubuno/Designer/ToolboxIcons");
#pragma warning restore VSSDK007
        }

        private static async System.Threading.Tasks.Task RetryMissingIconsAsync()
        {
            for (var attempt = 1; attempt <= 20; attempt++)
            {
                await System.Threading.Tasks.Task.Delay(1500).ConfigureAwait(false);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (RefreshIcons() == 0)
                {
                    KubunoViewsLogHost.Current.WriteLine($"[designer] Toolbox: every icon resolved after {attempt} retry(ies).");
                    return;
                }
            }

            KubunoViewsLogHost.Current.WriteLine($"[designer] Toolbox: {s_iconFailures} icon(s) still unresolved; the Toolbox keeps its placeholder glyph for them.");
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

            s_items.Add((data, component.Name));

            return true;
        }

        /// <summary>
        /// The component's icon as the 16x16 HBITMAP the legacy toolbox API takes (28x28 and 32x32 bitmaps
        /// were tried live at 175%: the Toolbox reserves the slot but draws nothing, so at high DPI it
        /// upscales the 16x16 bitmap - there is no moniker-based item API to avoid that), on the magenta
        /// transparency key (<c>clrTransparent</c>). Checked by decompiling the installed Toolbox
        /// (<c>Microsoft.VisualStudio.Toolbox.ItemInfo</c>): the HBITMAP goes through <c>Image.FromHbitmap</c>,
        /// which drops any alpha channel, and the pixels equal to the key are made transparent - a colour key
        /// is the only transparency it honours.
        ///
        /// <para>The vector icon (the Light/Dark/HighContrast XAML of <c>KubunoControls.imagemanifest</c>) is
        /// rendered here with WPF rather than through <c>IVsImageService2.GetImage</c>: found live that the
        /// image service's 16x16 raster of these stroked icons is thin and mostly translucent, so once the
        /// translucent pixels were keyed out or blended the circles showed as rings of dots. Here the strokes
        /// are widened to 1.5 px (<see cref="ToolboxStrokeScale"/>: the weight of Visual Studio's own toolbox
        /// glyphs) and every covered pixel is pre-composited onto the tool-window background of the current
        /// theme, so anti-aliased edges become solid blended colours; only fully uncovered pixels take the
        /// key. <see cref="IntPtr.Zero"/> when there is no icon (the Toolbox then shows its generic glyph).</para>
        /// </summary>
        private static IntPtr CreateBitmap(string componentName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (IconName?.Invoke(componentName) is not { } iconName)
                {
                    return IntPtr.Zero;
                }

                var background = Microsoft.VisualStudio.PlatformUI.VSColorTheme.GetThemedColor(Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowBackgroundColorKey);
                var pixels = RenderIcon(iconName, IconVariantFor(background));
                if (pixels is null)
                {
                    s_iconFailures++;
                    return IntPtr.Zero;
                }

                using var keyed = new Bitmap(IconSize, IconSize, PixelFormat.Format24bppRgb);
                for (var y = 0; y < IconSize; y++)
                {
                    for (var x = 0; x < IconSize; x++)
                    {
                        var (r, g, b) = Composite(pixels, (y * IconSize) + x, background);
                        keyed.SetPixel(x, y, r is null ? Color.FromArgb(255, 0, 255) : Color.FromArgb(r.Value, g, b));
                    }
                }

                // Opt out of Visual Studio's image theming (ImageThemingUtilities.ThemeDIBits, which the
                // Toolbox applies to item bitmaps): it inverts the luminosity of a bitmap drawn for a light
                // background, so our already-themed Dark icon had its light ink turned to near-background -
                // found live with a test pattern (white became black), which is what made the strokes look
                // dotted. The marker is a cyan top-right pixel; VS clears it to transparent afterwards.
                keyed.SetPixel(IconSize - 1, 0, Color.FromArgb(0, 255, 255));

                return keyed.GetHbitmap();
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or ExternalException or System.Windows.Markup.XamlParseException)
            {
                s_iconFailures++;
                KubunoViewsLogHost.Current.WriteException($"[designer] Toolbox: no icon for '{componentName}'", ex);
                return IntPtr.Zero;
            }
        }

        private const int IconSize = 16;

        /// <summary>Stroke widening of the Toolbox rendering: Lucide's 2-unit stroke on a 24-unit canvas is 1.33 px at 16 px; x1.125 makes it 1.5 px.</summary>
        internal const double ToolboxStrokeScale = 1.125;

        /// <summary>The icon variant for a background, as the image service chooses it: HighContrast in a high-contrast theme, otherwise Dark on a dark background.</summary>
        internal static string IconVariantFor(Color background) =>
            System.Windows.SystemParameters.HighContrast ? "HighContrast"
            : ((0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B)) < 128 ? "Dark" : "Light";

        /// <summary>
        /// One pixel of the premultiplied BGRA render over <paramref name="background"/>: null red when the
        /// pixel is (almost) uncovered, which becomes the transparency key; never the key colour itself.
        /// </summary>
        public static (int? R, int G, int B) Composite(byte[] premultipliedBgra, int index, Color background)
        {
            var o = index * 4;
            var alpha = premultipliedBgra[o + 3];
            if (alpha < 8)
            {
                return (null, 0, 0);
            }

            var rest = (255 - alpha) / 255.0;
            var r = Math.Min(255, (int)Math.Round(premultipliedBgra[o + 2] + (background.R * rest)));
            var g = Math.Min(255, (int)Math.Round(premultipliedBgra[o + 1] + (background.G * rest)));
            var b = Math.Min(255, (int)Math.Round(premultipliedBgra[o] + (background.B * rest)));
            return r == 255 && g == 0 && b == 255 ? (254, 0, 255) : (r, g, b);
        }

        /// <summary>Renders an icon's XAML (compiled into Kubuno.VisualStudio.RustProjectSystem) at 16x16, premultiplied BGRA; null when the resource is missing.</summary>
        private static byte[]? RenderIcon(string iconName, string variant)
        {
            var uri = new Uri($"/Kubuno.VisualStudio.RustProjectSystem;component/Resources/Icons/Controls/{iconName}.{variant}.xaml", UriKind.Relative);
            if (System.Windows.Application.LoadComponent(uri) is not System.Windows.FrameworkElement icon)
            {
                return null;
            }

            WidenStrokes(icon);
            icon.Measure(new System.Windows.Size(IconSize, IconSize));
            icon.Arrange(new System.Windows.Rect(0, 0, IconSize, IconSize));
            icon.UpdateLayout();
            var target = new System.Windows.Media.Imaging.RenderTargetBitmap(IconSize, IconSize, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            target.Render(icon);
            var pixels = new byte[IconSize * IconSize * 4];
            target.CopyPixels(pixels, IconSize * 4, 0);
            return pixels;
        }

        private static void WidenStrokes(System.Windows.DependencyObject node)
        {
            if (node is System.Windows.Shapes.Shape shape)
            {
                shape.StrokeThickness *= ToolboxStrokeScale;
            }

            foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(node))
            {
                if (child is System.Windows.DependencyObject dependencyObject)
                {
                    WidenStrokes(dependencyObject);
                }
            }
        }

        /// <summary>Re-renders every Kubuno item's icon for the current theme (<c>IVsToolbox.SetItemInfo</c>); returns how many icons could not be rendered.</summary>
        private static int RefreshIcons()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsToolbox)) is not IVsToolbox toolbox)
            {
                return 0;
            }

            s_iconFailures = 0;

            foreach (var (data, component) in s_items)
            {
                var bitmap = CreateBitmap(component);
                var info = new TBXITEMINFO
                {
                    bstrText = component,
                    hBmp = bitmap,
                    clrTransparent = TransparentMagenta,
                    dwFlags = (uint)(__TBXITEMINFOFLAGS.TBXIF_DONTPERSIST | (bitmap != IntPtr.Zero ? __TBXITEMINFOFLAGS.TBXIF_DELETEBITMAP : 0)),
                };
                toolbox.SetItemInfo((Microsoft.VisualStudio.OLE.Interop.IDataObject)data, new[] { info });
            }

            toolbox.UpdateToolboxUI();
            return s_iconFailures;
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

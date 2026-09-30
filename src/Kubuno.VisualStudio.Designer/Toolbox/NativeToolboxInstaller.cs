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
    /// <c>IVsToolboxUser.IsSupported</c> for every item; the items, and the Kubuno tabs they need, exist
    /// only while a <c>.kbview</c> designer is the active document (<see cref="EnsureInstalled"/>). Items are added with <c>TBXIF_DONTPERSIST</c>: they are rebuilt
    /// from the live <c>kubuno/registry</c> in every session (the registry is the truth and may change
    /// with the Kubuno version), and never accumulate as stale duplicates in the persisted toolbox.</para>
    ///
    /// <para><b>Icons</b>: the Kubuno control icon of each component (the same Lucide-based XAML as its
    /// Solution Explorer element node, <c>KubunoControls.imagemanifest</c>), drawn pixel-hinted into a 16x16 bitmap on a
    /// magenta transparency key (the legacy toolbox API only takes an <c>HBITMAP</c>) - see
    /// <see cref="CreateBitmap"/>.</para>
    /// </summary>
    public static class NativeToolboxInstaller
    {
        private const uint TransparentMagenta = 0x00FF00FF;
        private static bool s_installed;
        private static int s_iconFailures;
        // Items typed as object: a VS type in a static field initialiser would load the Shell assembly in unit tests (Layout).
        private static readonly List<(object Data, string Component, string Icon, bool Project)> s_items = new List<(object, string, string, bool)>();

        /// <summary>Set by the VSIX: the base name of a component's icon XAML (e.g. "Button", "Control" for an unknown component; null: no icon).</summary>
        public static Func<string, string?>? IconName { get; set; }

        /// <summary>Whether the Kubuno items were added to the Toolbox in this session.</summary>
        public static bool IsInstalled => s_installed;

        private static ComponentRegistry? s_registry;
        private static bool s_subscribed;

        // EVT-7b: the "<Project> Composants" tab (docs/EVENTS.md): the project's own controls compiled into its last
        // design build (WinForms' AutoToolboxPopulate), plus those chosen from other crates ("Choisir des éléments…").
        private static string? s_projectTab;
        private static IReadOnlyList<ComponentMeta> s_projectComponents = Array.Empty<ComponentMeta>();
        private static string? s_installedProjectTab;

        /// <summary>
        /// Sets the project tab (<paramref name="tabName"/>, e.g. "RoundApp Composants") and its controls, and updates
        /// the Toolbox when the Kubuno items are shown. Null or no component: no project tab.
        /// </summary>
        public static void SetProjectComponents(string? tabName, IReadOnlyList<ComponentMeta> components)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var list = components?.Where(c => c.Browsable && !string.IsNullOrEmpty(c.Name)).ToList() ?? new List<ComponentMeta>();
            string Describe(ComponentMeta c) => c.Name + "|" + c.ToolboxIcon + "|" + c.Kind;
            if (string.Equals(list.Count == 0 ? null : tabName, s_projectTab, StringComparison.Ordinal) && list.Select(Describe).SequenceEqual(s_projectComponents.Select(Describe)))
            {
                return;
            }

            s_projectTab = list.Count == 0 ? null : tabName;
            s_projectComponents = list;
            if (s_installed && Package.GetGlobalService(typeof(SVsToolbox)) is IVsToolbox toolbox)
            {
                RemoveProjectItems(toolbox);
                AddProjectItems(toolbox);
                toolbox.UpdateToolboxUI();
            }
        }

        /// <summary>
        /// The Kubuno control icon of element <paramref name="tag"/> as a WPF element (its vector XAML, for the current
        /// theme), for the designer's own UI (the component tray); null when it has none.
        /// </summary>
        public static System.Windows.FrameworkElement? LoadIconElement(string tag)
        {
            try
            {
                if (IconName?.Invoke(tag) is not { } iconName)
                {
                    return null;
                }

                var background = Microsoft.VisualStudio.PlatformUI.VSColorTheme.GetThemedColor(Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowBackgroundColorKey);
                var uri = new Uri($"/Kubuno.VisualStudio.RustProjectSystem;component/Resources/Icons/Controls/{iconName}.{IconVariantFor(background)}.xaml", UriKind.Relative);
                return System.Windows.Application.LoadComponent(uri) as System.Windows.FrameworkElement;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or System.Windows.Markup.XamlParseException)
            {
                return null;
            }
        }

        /// <summary>The project tab's current content (tests / diagnostics).</summary>
        public static (string? Tab, IReadOnlyList<string> Components) ProjectLayout =>
            (s_projectTab, SortedForToolbox(s_projectComponents).Select(c => c.Name).ToList());

        /// <summary>
        /// The icon of a project control: its <c>#[toolbox(icon = …)]</c> when it names a known icon (<c>"circle"</c>,
        /// <c>"Button"</c>, <c>"star"</c>), else the icon of its kind (custom control, user control, component).
        /// </summary>
        public static string ProjectIconKey(ComponentMeta component)
        {
            if (!string.IsNullOrWhiteSpace(component.ToolboxIcon))
            {
                var pascal = string.Concat(component.ToolboxIcon!.Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
                if (IconName?.Invoke(pascal) is { } known && known != "Control")
                {
                    return pascal;
                }
            }

            return component.Kind switch
            {
                "user_control" => "UserControl",
                "component" => "Component",
                _ => "CustomControl",
            };
        }

        private static void AddProjectItems(IVsToolbox toolbox)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (s_projectTab is null || s_projectComponents.Count == 0)
            {
                return;
            }

            if (!ListTabs(toolbox).Contains(s_projectTab))
            {
                toolbox.AddTab(s_projectTab);
            }

            s_installedProjectTab = s_projectTab;
            foreach (var component in SortedForToolbox(s_projectComponents))
            {
                try
                {
                    AddItem(toolbox, component, s_projectTab, ProjectIconKey(component), project: true);
                }
                catch (Exception ex) when (ex is COMException or ArgumentException or ExternalException)
                {
                    KubunoViewsLogHost.Current.WriteException($"[designer] Toolbox: could not add '{component.Name}'", ex);
                }
            }

            KubunoViewsLogHost.Current.WriteLine($"[designer] Toolbox: {s_projectComponents.Count} project control(s) in '{s_projectTab}'.");
        }

        private static void RemoveProjectItems(IVsToolbox toolbox)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (var item in s_items.Where(i => i.Project).ToList())
            {
                toolbox.RemoveItem((Microsoft.VisualStudio.OLE.Interop.IDataObject)item.Data);
                s_items.Remove(item);
            }

            if (s_installedProjectTab is { } tab && ListTabs(toolbox).Contains(tab) && IsEmpty(toolbox, tab))
            {
                toolbox.RemoveTab(tab);
            }

            s_installedProjectTab = null;
        }

        /// <summary>
        /// Shows the Kubuno components in the Toolbox while a <c>.kbview</c> designer is the active document,
        /// and removes them - and the tabs they created - otherwise (the designer's command UI context,
        /// <see cref="DesignerConstants.CommandUiContextGuidString"/>), like the WinForms designer's tabs
        /// only appear for a WinForms designer. Found live: with the items merely refused by the active
        /// document's <c>IsSupported</c>, the Toolbox still showed each Kubuno tab with its "no usable
        /// control in this group" text for every other document; the Toolbox (native <c>msenv</c> code) has
        /// no per-tab visibility API, so the tabs are really added and removed. Best-effort: failures are
        /// logged, never thrown.
        /// </summary>
        public static void EnsureInstalled(ComponentRegistry registry)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (registry is null || registry.Components.Count == 0)
            {
                return;
            }

            s_registry = registry;
            if (s_installed && Package.GetGlobalService(typeof(SVsToolbox)) is IVsToolbox installedToolbox && LibrarySignature(registry) != s_librarySignature)
            {
                // The language server declared (or dropped) library components since the Toolbox was filled.
                RemoveLibraryItems(installedToolbox);
                AddLibraryItems(installedToolbox, registry);
                installedToolbox.UpdateToolboxUI();
            }

            var context = UIContext.FromUIContextGuid(new Guid(DesignerConstants.CommandUiContextGuidString));
            if (!s_subscribed)
            {
                s_subscribed = true;
                context.UIContextChanged += (_, e) =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    if (e.Activated)
                    {
                        Install();
                    }
                    else
                    {
                        Uninstall();
                    }
                };
                // Icons are rendered for the current theme; re-render them when it changes (like VS's own).
                Microsoft.VisualStudio.PlatformUI.VSColorTheme.ThemeChanged += _ => RefreshIcons();
            }

            if (context.IsActive)
            {
                Install();
            }
        }

        /// <summary>Removes the Kubuno items and every Kubuno tab left empty, e.g. a tab persisted by an earlier version (UI thread; loads the Toolbox - see <see cref="UninstallIfLeftBehind"/>).</summary>
        public static void Uninstall()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsToolbox)) is not IVsToolbox toolbox)
            {
                return;
            }

            try
            {
                foreach (var item in s_items)
                {
                    toolbox.RemoveItem((Microsoft.VisualStudio.OLE.Interop.IDataObject)item.Data);
                }

                s_items.Clear();
                s_libraryItems.Clear();
                s_librarySignature = string.Empty;
                var existing = ListTabs(toolbox);
                var kubunoTabs = DesignerText.AllToolboxTabNames().ToList();
                if (s_installedProjectTab is { } projectTab)
                {
                    kubunoTabs.Add(projectTab);
                    s_installedProjectTab = null;
                }

                foreach (var tab in kubunoTabs)
                {
                    // A shared tab (e.g. WinForms' "Conteneurs") keeps its own items: only an empty one goes.
                    if (existing.Contains(tab) && IsEmpty(toolbox, tab))
                    {
                        toolbox.RemoveTab(tab);
                    }
                }

                toolbox.UpdateToolboxUI();
                SetTabsMayBePersisted(false);
            }
            catch (Exception ex) when (ex is COMException or ArgumentException or InvalidCastException)
            {
                KubunoViewsLogHost.Current.WriteException("[designer] Toolbox: could not remove the Kubuno tabs", ex);
            }
            finally
            {
                s_installed = false;
            }
        }

        private const string MarkerCollection = @"Kubuno\Toolbox";
        private const string MarkerProperty = "TabsMayBePersisted";

        /// <summary>
        /// <see cref="Uninstall"/>, but only when an earlier session may have left Kubuno tabs in the persisted
        /// Toolbox: it closed (or crashed) while a <c>.kbview</c> designer was active, or it predates this marker.
        /// Asking for <c>SVsToolbox</c> loads the whole Toolbox (every WinForms/WPF item it knows about), so the
        /// package must not do it on every load; the marker is a user-settings flag, set while Kubuno items are in
        /// the Toolbox and cleared once they are removed. Call on the UI thread, when idle.
        /// </summary>
        public static void UninstallIfLeftBehind()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var store = MarkerStore();
            if (store is not null && store.CollectionExists(MarkerCollection) && store.PropertyExists(MarkerCollection, MarkerProperty)
                && !store.GetBoolean(MarkerCollection, MarkerProperty))
            {
                return;
            }

            Uninstall();
            SetTabsMayBePersisted(false);
        }

        private static void SetTabsMayBePersisted(bool value)
        {
            try
            {
                if (MarkerStore() is not { } store)
                {
                    return;
                }

                if (!store.CollectionExists(MarkerCollection))
                {
                    store.CreateCollection(MarkerCollection);
                }

                store.SetBoolean(MarkerCollection, MarkerProperty, value);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or COMException)
            {
                // Best effort: without the marker, the next session simply runs the cleanup again.
            }
        }

        private static Microsoft.VisualStudio.Settings.WritableSettingsStore? MarkerStore()
        {
            try
            {
                return new Microsoft.VisualStudio.Shell.Settings.ShellSettingsManager(ServiceProvider.GlobalProvider)
                    .GetWritableSettingsStore(Microsoft.VisualStudio.Settings.SettingsScope.UserSettings);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or COMException)
            {
                return null;
            }
        }

        private static HashSet<string> ListTabs(IVsToolbox toolbox)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var tabs = new HashSet<string>(StringComparer.Ordinal);
            if (ErrorHandler.Failed(toolbox.EnumTabs(out var tabEnum)) || tabEnum is null)
            {
                return tabs;
            }

            var one = new string[1];
            while (tabEnum.Next(1, one, out var fetched) == VSConstants.S_OK && fetched == 1)
            {
                tabs.Add(one[0]);
            }

            return tabs;
        }

        private static bool IsEmpty(IVsToolbox toolbox, string tab)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ErrorHandler.Failed(toolbox.EnumItems(tab, out var itemEnum)) || itemEnum is null)
            {
                return true;
            }

            var one = new Microsoft.VisualStudio.OLE.Interop.IDataObject[1];
            return !(itemEnum.Next(1, one, out var fetched) == VSConstants.S_OK && fetched == 1);
        }

        private static void Install()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var registry = s_registry;
            if (s_installed || registry is null)
            {
                return;
            }

            if (Package.GetGlobalService(typeof(SVsToolbox)) is not IVsToolbox toolbox)
            {
                KubunoViewsLogHost.Current.WriteLine("[designer] Toolbox: SVsToolbox unavailable - the Kubuno components are not added to Visual Studio's Toolbox.");
                return;
            }

            s_installed = true;
            SetTabsMayBePersisted(true);
            s_iconFailures = 0;
            var added = 0;
            var existing = ListTabs(toolbox);
            foreach (var family in registry.FamilyNames.Where(f => f != "project"))
            {
                var tabName = DesignerText.ToolboxTabName(family);

                // The tab may already exist (another designer's tab of the same name, e.g. WinForms'
                // "Conteneurs"): AddItem into it is fine - the IsSupported filtering keeps each designer's
                // items apart, and Uninstall leaves a tab that still has items.
                if (!existing.Contains(tabName))
                {
                    toolbox.AddTab(tabName);
                }

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

            added += AddLibraryItems(toolbox, registry);
            AddProjectItems(toolbox);
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

        private static bool AddItem(IVsToolbox toolbox, ComponentMeta component, string tabName, string? iconKey = null, bool project = false)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var data = new OleDataObject();
            data.SetData(ToolboxItemFormat.FormatName, new MemoryStream(ToolboxItemFormat.Encode(component.Name)));

            var icon = iconKey ?? component.Name;
            var bitmap = CreateBitmap(icon);
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

            s_items.Add((data, component.Name, icon, project));

            return true;
        }

        /// <summary>
        /// The component's icon as the 16x16 HBITMAP the legacy toolbox API takes, on the magenta transparency
        /// key (<c>clrTransparent</c>). Only 16x16 is drawn: checked live at 175 % with 28, 32 and 48 px bitmaps
        /// (with and without <c>iImageWidth</c>/<c>iImageIndex</c>) - the Toolbox widens the slot but paints
        /// nothing - and the WinForms and XAML designers hand it 16x16 bitmaps too; Visual Studio upscales them
        /// (nearest-neighbour to 200 %, then bicubic). The icon is therefore drawn FOR the pixel grid
        /// (<see cref="ToolboxIconRasterizer"/>: 1-px strokes on pixel centres, symmetric pixel circles, a 1-px
        /// margin), which that upscale keeps crisp, like the WinForms toolbox bitmaps. The decompiled Toolbox
        /// (<c>Microsoft.VisualStudio.Toolbox.ItemInfo</c>) passes the HBITMAP through <c>Image.FromHbitmap</c>,
        /// which drops alpha, and keys out the pixels equal to <c>clrTransparent</c>: every covered pixel is
        /// pre-composited onto the tool-window background of the current theme and only uncovered pixels take
        /// the key - except the 8 neighbours of an inked pixel, which get the opaque background: found live with
        /// test patterns that the Toolbox's upscale erases every 1-px feature lying between key pixels (one-pixel
        /// lines and a checkerboard vanished, only the outer row/column survived), which had left the hinted
        /// icons dotted. <see cref="IntPtr.Zero"/> when there is no icon (the Toolbox then shows its generic glyph).
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
                var ink = new bool[IconSize * IconSize];
                for (var i = 0; i < ink.Length; i++)
                {
                    ink[i] = Composite(pixels, i, background).R is not null;
                }

                for (var y = 0; y < IconSize; y++)
                {
                    for (var x = 0; x < IconSize; x++)
                    {
                        var (r, g, b) = Composite(pixels, (y * IconSize) + x, background);
                        keyed.SetPixel(x, y, r is not null ? Color.FromArgb(r.Value, g, b)
                            : TouchesInk(ink, x, y) ? OpaqueBackground(background)
                            : Color.FromArgb(255, 0, 255));
                    }
                }

                // Opt out of Visual Studio's image theming (ImageThemingUtilities.ThemeDIBits, which the
                // Toolbox applies to item bitmaps): it inverts the luminosity of a bitmap drawn for a light
                // background, so an icon already drawn for the current theme must opt out - a cyan top-right
                // pixel (inside the icon's transparent margin), which VS clears afterwards.
                keyed.SetPixel(IconSize - 1, 0, Color.FromArgb(0, 255, 255));

                return keyed.GetHbitmap();
            }            catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or ExternalException or System.Windows.Markup.XamlParseException)
            {
                s_iconFailures++;
                KubunoViewsLogHost.Current.WriteException($"[designer] Toolbox: no icon for '{componentName}'", ex);
                return IntPtr.Zero;
            }
        }

        private const int IconSize = ToolboxIconRasterizer.Size;

        /// <summary>The icon variant for a background, as the image service chooses it: HighContrast in a high-contrast theme, otherwise Dark on a dark background.</summary>
        internal static string IconVariantFor(Color background) =>
            System.Windows.SystemParameters.HighContrast ? "HighContrast"
            : ((0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B)) < 128 ? "Dark" : "Light";

        /// <summary>Whether an uncovered pixel has an inked pixel among its 8 neighbours.</summary>
        public static bool TouchesInk(bool[] ink, int x, int y)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if ((dx != 0 || dy != 0) && nx >= 0 && ny >= 0 && nx < IconSize && ny < IconSize && ink[(ny * IconSize) + nx])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>The background as an opaque pixel colour that can never be mistaken for the transparency key.</summary>
        private static Color OpaqueBackground(Color background) =>
            background.R == 255 && background.G == 0 && background.B == 255 ? Color.FromArgb(254, 0, 255) : Color.FromArgb(background.R, background.G, background.B);

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

        /// <summary>Renders an icon's XAML (compiled into Kubuno.VisualStudio.RustProjectSystem) pixel-hinted at 16x16 (<see cref="ToolboxIconRasterizer"/>), premultiplied BGRA; null when the resource is missing.</summary>
        private static byte[]? RenderIcon(string iconName, string variant)
        {
            var uri = new Uri($"/Kubuno.VisualStudio.RustProjectSystem;component/Resources/Icons/Controls/{iconName}.{variant}.xaml", UriKind.Relative);
            return System.Windows.Application.LoadComponent(uri) is System.Windows.FrameworkElement icon ? ToolboxIconRasterizer.Render(icon) : null;
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

            foreach (var (data, component, icon, _) in s_items)
            {
                var bitmap = CreateBitmap(icon);
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

        /// <summary>
        /// The crates of the Kubuno libraries whose components get Toolbox tabs of their own, by their
        /// <c>#[toolbox(category = …)]</c>: <c>kubuno-print</c>'s "Impression" (docs/PRINTING.md) and <c>kubuno-data</c>'s
        /// "Données" - like the WinForms Toolbox's "Printing" and "Data" tabs, present in every project that links
        /// them (through the <c>kubuno</c> crate), without "Choose Items…".
        /// </summary>
        private static readonly string[] LibraryCrates = { "kubuno_print", "kubuno_data" };

        private static readonly HashSet<string> s_libraryItems = new HashSet<string>(StringComparer.Ordinal);
        private static string s_librarySignature = string.Empty;

        /// <summary>The library components of <paramref name="registry"/> and their tabs, in Toolbox order (see <see cref="LibraryCrates"/>).</summary>
        public static IEnumerable<(string Tab, ComponentMeta Component)> LibraryItems(ComponentRegistry registry) =>
            registry.Components
                .Where(c => c.IsProject && c.Browsable && c.CrateName is { } crate && LibraryCrates.Contains(crate))
                .GroupBy(c => c.Name, StringComparer.Ordinal)
                .Select(g => g.First())
                .GroupBy(c => DesignerText.ToolboxTabName((c.ToolboxCategory ?? "Components").ToLowerInvariant()), StringComparer.Ordinal)
                .SelectMany(g => SortedForToolbox(g).Select(c => (g.Key, c)));

        private static string LibrarySignature(ComponentRegistry registry) =>
            string.Join(";", LibraryItems(registry).Select(i => i.Tab + "/" + i.Component.Name));

        /// <summary>Adds the library components' tabs and items; returns how many items were added.</summary>
        private static int AddLibraryItems(IVsToolbox toolbox, ComponentRegistry registry)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var added = 0;
            var existing = ListTabs(toolbox);
            foreach (var (tab, component) in LibraryItems(registry))
            {
                if (!existing.Contains(tab))
                {
                    toolbox.AddTab(tab);
                    existing.Add(tab);
                }

                try
                {
                    if (AddItem(toolbox, component, tab))
                    {
                        s_libraryItems.Add(component.Name);
                        added++;
                    }
                }
                catch (Exception ex) when (ex is COMException or ArgumentException or ExternalException)
                {
                    KubunoViewsLogHost.Current.WriteException($"[designer] Toolbox: could not add '{component.Name}'", ex);
                }
            }

            s_librarySignature = LibrarySignature(registry);
            return added;
        }

        private static void RemoveLibraryItems(IVsToolbox toolbox)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (var item in s_items.Where(i => !i.Project && s_libraryItems.Contains(i.Component)).ToList())
            {
                toolbox.RemoveItem((Microsoft.VisualStudio.OLE.Interop.IDataObject)item.Data);
                s_items.Remove(item);
            }

            s_libraryItems.Clear();
            s_librarySignature = string.Empty;
        }

        /// <summary>Alphabetical inside a tab, like the WinForms Toolbox (Visual Studio adds the "Pointer" entry on top of each tab itself).</summary>
        public static IEnumerable<ComponentMeta> SortedForToolbox(IEnumerable<ComponentMeta> components) =>
            components.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase);

        /// <summary>The component names of <paramref name="registry"/> in toolbox order (tests / diagnostics).</summary>
        public static IEnumerable<(string Tab, string Component)> Layout(ComponentRegistry registry)
        {
            foreach (var family in registry.FamilyNames.Where(f => f != "project"))
            {
                foreach (var component in SortedForToolbox(registry.Families[family]))
                {
                    yield return (DesignerText.ToolboxTabName(family), component.Name);
                }
            }

            foreach (var (tab, component) in LibraryItems(registry))
            {
                yield return (tab, component.Name);
            }
        }
    }
}

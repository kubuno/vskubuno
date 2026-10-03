using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using VsImageAttributes = Microsoft.VisualStudio.Imaging.Interop.ImageAttributes;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Kubuno.Views.Designer.Registry;
using Kubuno.Views.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.Views.Designer.Toolbox
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

        /// <summary>
        /// Set by the target layer with <see cref="IconName"/>: the assembly whose WPF resources hold the icons
        /// (<c>Resources/Icons/Controls/&lt;name&gt;.&lt;Light|Dark|HighContrast&gt;.xaml</c>) - the desktop layer's
        /// Kubuno.Desktop.ProjectSystem, next to its KubunoControls.imagemanifest. Named, never referenced, so this layer does
        /// not depend on a target.
        /// </summary>
        public static string? IconAssembly { get; set; }

        /// <summary>The pack URI of an icon XAML in <see cref="IconAssembly"/>.</summary>
        private static Uri IconUri(string iconName, string variant) =>
            new Uri($"/{IconAssembly};component/Resources/Icons/Controls/{iconName}.{variant}.xaml", UriKind.Relative);

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
                if (IconAssembly is null || IconName?.Invoke(tag) is not { } iconName)
                {
                    return null;
                }

                var background = Microsoft.VisualStudio.PlatformUI.VSColorTheme.GetThemedColor(Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowBackgroundColorKey);
                var uri = IconUri(iconName, IconVariantFor(background));
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
            // WinForms' [ToolboxBitmap]: #[toolbox(bitmap = "address_editor.png")] registers the image's absolute path.
            if (IsImageFile(component.ToolboxIcon))
            {
                return ImageKeyPrefix + component.ToolboxIcon;
            }

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
                // Icons are rendered for the current theme; re-render them when it changes (like VS's own),
                // and put back the items the Toolbox dropped while it re-themed (see OnThemeChanged).
                Microsoft.VisualStudio.PlatformUI.VSColorTheme.ThemeChanged += _ => OnThemeChanged();
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
                var background = Microsoft.VisualStudio.PlatformUI.VSColorTheme.GetThemedColor(Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowBackgroundColorKey);
                byte[]? pixels;
                if (componentName.StartsWith(ImageKeyPrefix, StringComparison.Ordinal))
                {
                    pixels = ImageFileIcon(componentName.Substring(ImageKeyPrefix.Length));
                }
                else if (IconAssembly is null || IconName?.Invoke(componentName) is not { } iconName)
                {
                    return IntPtr.Zero;
                }
                else
                {
                    pixels = RenderIcon(iconName, IconVariantFor(background));
                }

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
            }            catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or ExternalException or OutOfMemoryException or System.Windows.Markup.XamlParseException)
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

        /// <summary>The icon key of a project control whose Toolbox icon is an image file (followed by its path).</summary>
        internal const string ImageKeyPrefix = "file:";

        /// <summary>Whether <paramref name="icon"/> names an image file (<c>.png</c>, <c>.bmp</c>, <c>.ico</c>, <c>.gif</c>, <c>.jpg</c>) rather than a glyph.</summary>
        public static bool IsImageFile(string? icon) =>
            !string.IsNullOrWhiteSpace(icon) && new[] { ".png", ".bmp", ".ico", ".gif", ".jpg", ".jpeg" }.Any(ext => icon!.Trim().EndsWith(ext, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// A project control's own Toolbox bitmap (WinForms' <c>[ToolboxBitmap]</c>), scaled to 16x16 with high quality,
        /// as premultiplied BGRA like <see cref="RenderIcon"/>; null when the file cannot be read.
        /// </summary>
        public static byte[]? ImageFileIcon(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var source = Image.FromFile(path);
            using var scaled = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                // Edge pixels sample the image itself, not the transparent outside (a soft halo otherwise).
                using var wrap = new System.Drawing.Imaging.ImageAttributes();
                wrap.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
                g.DrawImage(source, new Rectangle(0, 0, IconSize, IconSize), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, wrap);
            }

            var data = scaled.LockBits(new Rectangle(0, 0, IconSize, IconSize), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var bytes = new byte[IconSize * IconSize * 4];
                for (var y = 0; y < IconSize; y++)
                {
                    Marshal.Copy(data.Scan0 + (y * data.Stride), bytes, y * IconSize * 4, IconSize * 4);
                }

                return bytes;
            }
            finally
            {
                scaled.UnlockBits(data);
            }
        }

        /// <summary>Renders an icon's XAML (compiled into <see cref="IconAssembly"/>) pixel-hinted at 16x16 (<see cref="ToolboxIconRasterizer"/>), premultiplied BGRA; null when the resource is missing.</summary>
        private static byte[]? RenderIcon(string iconName, string variant)
        {
            var uri = IconUri(iconName, variant);
            return System.Windows.Application.LoadComponent(uri) is System.Windows.FrameworkElement icon ? ToolboxIconRasterizer.Render(icon) : null;
        }
        /// <summary>
        /// A live theme switch. The Toolbox rebuilds its window on <c>ThemeChanged</c> and may do so after our
        /// handler ran: items added with <c>TBXIF_DONTPERSIST</c> that it reloads from its store are then gone,
        /// while <see cref="s_installed"/> still says they are there - the Toolbox stayed empty until the next
        /// session. The check therefore runs once the shell is idle again, and a missing item reinstalls them all.
        /// </summary>
        private static void OnThemeChanged()
        {
#pragma warning disable VSSDK007 // no package-owned JoinableTaskFactory reachable from this static installer - same precedent as RetryMissingIconsLater.
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                for (var pass = 0; pass < 3; pass++)
                {
                    // After the Toolbox's own re-theming (it runs from the same event, then on idle).
                    await System.Threading.Tasks.Task.Delay(pass == 0 ? 250 : 1500).ConfigureAwait(false);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (!s_installed)
                    {
                        // The designer's UI context may have flickered off and on while the shell re-themed,
                        // its events arriving out of order: the items were removed though a designer is active.
                        if (s_registry is not null && UIContext.FromUIContextGuid(new Guid(DesignerConstants.CommandUiContextGuidString)).IsActive)
                        {
                            KubunoViewsLogHost.Current.WriteLine("[designer] Toolbox: a .kbview designer is active but its items were removed during a theme change; reinstalling them.");
                            Install();
                        }

                        continue;
                    }

                    RepairOrRefresh("theme change");
                }
            }).FileAndForget("Kubuno/Designer/ToolboxTheme");
#pragma warning restore VSSDK007
        }

        /// <summary>Re-renders the icons, or reinstalls every Kubuno item when the Toolbox lost some of them.</summary>
        private static void RepairOrRefresh(string reason)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsToolbox)) is not IVsToolbox toolbox)
            {
                return;
            }

            var present = CountPresentItems(toolbox);
            if (present >= s_items.Count)
            {
                RefreshIcons();
                return;
            }

            KubunoViewsLogHost.Current.WriteLine($"[designer] Toolbox: {s_items.Count - present} of {s_items.Count} Kubuno item(s) gone after a {reason}; reinstalling them.");
            Uninstall();
            Install();
        }

        /// <summary>How many of the Kubuno items (<see cref="s_items"/>) the Toolbox still lists, in the Kubuno tabs.</summary>
        private static int CountPresentItems(IVsToolbox toolbox)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var tabs = DesignerText.AllToolboxTabNames().ToList();
            if (s_installedProjectTab is { } projectTab)
            {
                tabs.Add(projectTab);
            }

            var existing = ListTabs(toolbox);
            var count = 0;
            foreach (var tab in tabs.Where(existing.Contains).Distinct(StringComparer.Ordinal))
            {
                if (ErrorHandler.Failed(toolbox.EnumItems(tab, out var itemEnum)) || itemEnum is null)
                {
                    continue;
                }

                var one = new Microsoft.VisualStudio.OLE.Interop.IDataObject[1];
                while (itemEnum.Next(1, one, out var fetched) == VSConstants.S_OK && fetched == 1)
                {
                    if (ToolboxDataObjectReader.HasComponent(one[0]))
                    {
                        count++;
                    }
                }
            }

            return count;
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
        /// <c>#[toolbox(category = …)]</c>: <c>kubuno-desktop-print</c>'s "Impression" (docs/PRINTING.md) and <c>kubuno-desktop-data</c>'s
        /// "Données" - like the WinForms Toolbox's "Printing" and "Data" tabs, present in every project that links
        /// them (through the <c>kubuno-desktop</c> crate), without "Choose Items…". <c>kubuno-desktop-app-storage-components</c>'s
        /// "Stockage" / "Storage" (Settings, SecretStore, RegistryKey: docs/STORAGE-COMPONENTS.md) likewise. Their names before
        /// the 2026-10 rename (<c>kubuno_print</c>, ...) are kept for older desktop checkouts.
        /// </summary>
        private static readonly string[] LibraryCrates =
        {
            "kubuno_desktop_print", "kubuno_desktop_data", "kubuno_desktop_app_storage_components",
            "kubuno_print", "kubuno_data", "kubuno_app_storage_components",
        };

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

using System;
using System.Collections.Generic;
using System.Linq;
using Kubuno.Views.Designer.Menus;
using Kubuno.Views.Designer.Selection;

namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>
    /// The title bar's smart tag (docs/SHELL-CONTROLS.md section 5): shown at the window's top-right corner when the view
    /// itself is selected. « Ajouter un bouton à la barre de titre » in each region (Left, Center, Right) and one switch per
    /// standard header item (ShowSearch … ShowAccount). Pure, unit-tested; each task is one undo unit.
    /// </summary>
    public static class TitleBarDesignerTasks
    {
        /// <summary>The title-bar regions a button can be added to (<c>TitleBar.Region</c>).</summary>
        public static readonly IReadOnlyList<string> Regions = new[] { "Left", "Center", "Right" };

        /// <summary>The view's properties that show the header's standard items, in the web header's order.</summary>
        public static readonly IReadOnlyList<string> HeaderItems = new[] { "ShowSearch", "ShowNotifications", "ShowSettings", "ShowHelp", "ShowWaffle", "ShowAccount" };

        private static string T(string english, string french) => DesignerText.IsFrench ? french : english;

        /// <summary>The tasks for the view in <paramref name="text"/>: empty when its root is not a window (a user control).</summary>
        public static IReadOnlyList<MenuVerb> Verbs(string text)
        {
            if (ElementAttributeReader.Read(text, StableElementId.Root) is not { } root || root.TagName == "UserControl")
            {
                return Array.Empty<MenuVerb>();
            }

            var verbs = Regions.Select(r => new MenuVerb(MenuVerbKind.AddTitleBarButton, AddButtonText(r), r)).ToList();
            foreach (var item in HeaderItems)
            {
                var shown = root.Attributes.TryGetValue(item, out var value) && value is not null && value.Trim() != "false" && value.Trim().Length > 0;
                verbs.Add(new MenuVerb(MenuVerbKind.ToggleHeaderItem, (shown ? "✓ " : string.Empty) + ItemText(item), item));
            }

            return verbs;
        }

        /// <summary>« Ajouter un bouton à la barre de titre (à droite) ».</summary>
        public static string AddButtonText(string region) => region switch
        {
            "Left" => T("Add a Button to the Title Bar (Left)", "Ajouter un bouton à la barre de titre (à gauche)"),
            "Center" => T("Add a Button to the Title Bar (Center)", "Ajouter un bouton à la barre de titre (au centre)"),
            _ => T("Add a Button to the Title Bar (Right)", "Ajouter un bouton à la barre de titre (à droite)"),
        };

        /// <summary>The menu text of a standard item's switch.</summary>
        public static string ItemText(string property) => property switch
        {
            "ShowSearch" => T("Search Button", "Bouton de recherche"),
            "ShowNotifications" => T("Notifications Bell", "Cloche des notifications"),
            "ShowSettings" => T("Settings Button", "Bouton des paramètres"),
            "ShowHelp" => T("Help Button", "Bouton d'aide"),
            "ShowWaffle" => T("Apps Launcher (Waffle)", "Lanceur d'applications (gaufrier)"),
            "ShowAccount" => T("Account Avatar", "Avatar du compte"),
            _ => property,
        };

        /// <summary>
        /// The button a « Ajouter un bouton » task inserts in <paramref name="region"/>: an <c>IconButton</c> sized for the
        /// view's title bar (<see cref="BandHeight"/>): 36 DIP in the tall 64 DIP header, the caption buttons' 30 in a band
        /// of 40 DIP or more, 24 in the standard 32 DIP one — the web header's look.
        /// </summary>
        public static string ButtonXml(string text, string region)
        {
            var height = BandHeight(text);
            var side = height >= 56 ? "36" : height >= 40 ? "30" : "24";
            var glyph = height >= 56 ? "18" : height >= 40 ? "16" : "14";
            return "<IconButton TitleBar.Region=\"" + region + "\" Icon=\"Star\" Diameter=\"" + side + "\" Glyph=\"" + glyph + "\" Width=\"" + side + "\" Height=\"" + side + "\"/>";
        }

        /// <summary>
        /// The height of the view's title bar, as the runtime resolves it (kubuno-desktop-views <c>window::title_bar_style</c>):
        /// <c>TitleBarHeight</c> when set, else <c>TitleBarStyle</c> (<c>Standard</c> 32, <c>Tall</c> 64), else 64 for a view
        /// hosting the header's menus (a <c>Show…</c> header item, or a <c>HeaderActions</c> / <c>WaffleButton</c> /
        /// <c>AccountButton</c> element) and 32 for any other.
        /// </summary>
        public static double BandHeight(string text)
        {
            var root = ElementAttributeReader.Read(text, StableElementId.Root);
            if (root is null)
            {
                return 32;
            }

            if (root.Attributes.TryGetValue("TitleBarHeight", out var h) &&
                double.TryParse(h, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var height) && height > 0)
            {
                return height;
            }

            if (root.Attributes.TryGetValue("TitleBarStyle", out var style) && style is not null)
            {
                switch (style.Trim())
                {
                    case "Tall":
                        return 64;
                    case "Standard":
                        return 32;
                }
            }

            var hostsMenus = HeaderItems.Any(item => IsShown(text, item)) ||
                new[] { "<HeaderActions", "<WaffleButton", "<AccountButton" }.Any(tag => text.IndexOf(tag, StringComparison.Ordinal) >= 0);
            return hostsMenus ? 64 : 32;
        }

        /// <summary>Whether the view shows <paramref name="property"/>'s item now (its switch turns it off).</summary>
        public static bool IsShown(string text, string property) =>
            ElementAttributeReader.Read(text, StableElementId.Root) is { } root && root.Attributes.TryGetValue(property, out var value) &&
            value is not null && value.Trim().Length > 0 && value.Trim() != "false";
    }
}

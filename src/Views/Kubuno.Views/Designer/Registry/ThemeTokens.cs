using System;
using System.Collections.Generic;
using System.Linq;

namespace Kubuno.Views.Designer.Registry
{
    /// <summary>One Kubuno theme colour: its name as written in a view (<c>BackColor="Primary"</c>) and its value in each theme.</summary>
    public sealed class ThemeToken
    {
        public ThemeToken(string name, string light, string dark, string highContrast, string doc, string docFr)
        {
            Name = name;
            Light = light;
            Dark = dark;
            HighContrast = highContrast;
            Doc = doc;
            DocFr = docFr;
        }

        /// <summary>The token name (<c>"Primary"</c>).</summary>
        public string Name { get; }

        /// <summary>The colour in the light theme, <c>#RRGGBB</c> or <c>#RRGGBBAA</c>.</summary>
        public string Light { get; }

        /// <summary>The colour in the dark theme.</summary>
        public string Dark { get; }

        /// <summary>The Windows system colour used in a high-contrast theme (<c>"Highlight"</c>).</summary>
        public string HighContrast { get; }

        public string Doc { get; }

        public string DocFr { get; }

        /// <summary>The description in Visual Studio's UI language.</summary>
        public string LocalizedDoc => DesignerText.IsFrench ? DocFr : Doc;
    }

    /// <summary>
    /// The Kubuno theme colours offered first by the colour editor (the "theme tokens by default + free colours allowed"
    /// policy, docs/EVENTS.md): a token follows the light, dark and high-contrast themes on its own, a free colour does
    /// not. Mirrors the runtime's own table (<c>kubuno-desktop-views</c>); a test compares the two through the generated
    /// <c>theme-tokens.json</c> fixture when it is present.
    /// </summary>
    public static class ThemeTokens
    {
        public static IReadOnlyList<ThemeToken> All { get; } = new[]
        {
            new ThemeToken("Primary", "#1A73E8", "#8AB4F8", "Highlight", "The accent colour: main buttons, links, selection.", "Couleur d'accent : boutons principaux, liens, sélection."),
            new ThemeToken("PrimaryHover", "#1557B0", "#AECBFA", "Highlight", "The accent colour under the mouse pointer.", "Couleur d'accent sous le pointeur de la souris."),
            new ThemeToken("PrimaryLight", "#D3E3FD", "#1A3A5C", "Window", "A pale tint of the accent colour, for highlighted areas.", "Teinte pâle de la couleur d'accent, pour les zones mises en avant."),
            new ThemeToken("OnPrimary", "#FFFFFF", "#202124", "HighlightText", "Text and icons shown on the accent colour.", "Texte et icônes affichés sur la couleur d'accent."),
            new ThemeToken("Background", "#F8FAFD", "#17181B", "Window", "The window background.", "Fond de la fenêtre."),
            new ThemeToken("Surface", "#FFFFFF", "#202124", "Window", "The main content surface.", "Surface principale du contenu."),
            new ThemeToken("Surface1", "#F8F9FA", "#292A2D", "Window", "A slightly raised surface (cards).", "Surface légèrement surélevée (cartes)."),
            new ThemeToken("Surface2", "#F1F3F4", "#35363A", "Window", "A surface one step darker (hovered items).", "Surface un cran plus foncée (éléments survolés)."),
            new ThemeToken("Surface3", "#E8EAED", "#444746", "Window", "A surface two steps darker (pressed items).", "Surface deux crans plus foncée (éléments enfoncés)."),
            new ThemeToken("TextPrimary", "#202124", "#E8EAED", "WindowText", "The main text colour.", "Couleur principale du texte."),
            new ThemeToken("TextSecondary", "#5F6368", "#9AA0A6", "WindowText", "Secondary text: descriptions, hints.", "Texte secondaire : descriptions, indications."),
            new ThemeToken("TextTertiary", "#80868B", "#80868B", "WindowText", "Discreet text: column headers, subtle labels.", "Texte discret : en-têtes de colonnes, libellés secondaires."),
            new ThemeToken("Border", "#E0E0E0", "#5F6368", "WindowText", "The usual border colour.", "Couleur habituelle des bordures."),
            new ThemeToken("BorderStrong", "#BDC1C6", "#80868B", "WindowText", "A more visible border (hovered fields).", "Bordure plus visible (champs survolés)."),
            new ThemeToken("Divider", "#E0E0E0", "#5F63688C", "WindowText", "Separator lines.", "Lignes de séparation."),
            new ThemeToken("Danger", "#D93025", "#F28B82", "WindowText", "Errors and destructive actions.", "Erreurs et actions destructrices."),
            new ThemeToken("DangerLight", "#FCE8E6", "#3D1C1C", "Window", "A pale background for errors.", "Fond pâle pour les erreurs."),
            new ThemeToken("Success", "#1E8E3E", "#81C995", "WindowText", "Success and confirmation.", "Succès et confirmation."),
            new ThemeToken("SuccessLight", "#E6F4EA", "#1A3A27", "Window", "A pale background for successes.", "Fond pâle pour les succès."),
            new ThemeToken("Warning", "#F9AB00", "#FDD663", "WindowText", "Warnings.", "Avertissements."),
            new ThemeToken("WarningLight", "#FEF7E0", "#3D3218", "Window", "A pale background for warnings.", "Fond pâle pour les avertissements."),
            new ThemeToken("Caution", "#9D5D00", "#FDD663", "WindowText", "Warning text that stays readable on a light background.", "Texte d'avertissement qui reste lisible sur un fond clair."),
            new ThemeToken("LinkVisited", "#663399", "#C5A0E8", "HotTrack", "A link that has already been followed.", "Lien déjà suivi."),
            new ThemeToken("Selection", "#E8F0FE", "#22344D", "Highlight", "The background of selected items.", "Fond des éléments sélectionnés."),
            new ThemeToken("Hover", "#E4ECF7", "#2B2C30", "Window", "The background of an item under the mouse pointer.", "Fond d'un élément sous le pointeur de la souris."),
            new ThemeToken("ListSelected", "#E8F0FE", "#22344D", "Highlight", "The background of a selected item of a list (the same colour as Selection).", "Fond d'un élément sélectionné d'une liste (même couleur que Selection)."),
            new ThemeToken("ControlFillHover", "#E8EAED", "#35363A", "Window", "The background of a subtle control (an icon button, a menu row) under the mouse pointer.", "Fond d'un contrôle discret (bouton icône, ligne de menu) sous le pointeur de la souris."),
            new ThemeToken("TitleBarBackground", "#00000000", "#00000000", "ActiveCaption", "The background of the window's title bar (transparent: the window's backdrop shows through).", "Fond de la barre de titre de la fenêtre (transparent : le fond de la fenêtre transparaît)."),
            new ThemeToken("TooltipBackground", "#3C4043F2", "#3C4043F2", "Info", "The background of tooltips.", "Fond des info-bulles."),
            new ThemeToken("TooltipForeground", "#FFFFFF", "#FFFFFF", "InfoText", "The text of tooltips.", "Texte des info-bulles."),
        };

        /// <summary>The token named <paramref name="name"/> (case-insensitive), or null.</summary>
        public static ThemeToken? Find(string? name) =>
            string.IsNullOrWhiteSpace(name) ? null : All.FirstOrDefault(t => string.Equals(t.Name, name!.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

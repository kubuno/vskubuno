using System;
using System.Globalization;

namespace Kubuno.VisualStudio.Designer
{
    /// <summary>
    /// The handful of user-visible strings the designer puts into Visual Studio's OWN chrome (Toolbox tab
    /// names, Properties window categories, the document tab caption suffix, validation messages) -
    /// localized for the two UI languages this extension is used with (French and English), picked from
    /// Visual Studio's own UI culture (<see cref="CultureInfo.CurrentUICulture"/>, which VS sets to its
    /// installed language pack). Kept as a tiny code table rather than .resx satellites: there are only a
    /// few dozen strings and this library ships no resources today.
    /// </summary>
    public static class DesignerText
    {
        /// <summary>Test seam: forces a language instead of reading the current UI culture.</summary>
        public static bool? ForceFrench { get; set; }

        public static bool IsFrench => ForceFrench ?? string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "fr", StringComparison.OrdinalIgnoreCase);

        private static string T(string english, string french) => IsFrench ? french : english;

        /// <summary>Appended to the document tab caption of the Design view, like WinForms' <c>Form1.cs [Design]</c>.</summary>
        public static string DesignCaptionSuffix => T(" [Design]", " [Conception]");

        public static string CategoryDesign => T("Design", "Design");

        public static string CategoryLayout => T("Layout", "Disposition");

        public static string CategoryAppearance => T("Appearance", "Apparence");

        public static string CategoryBehavior => T("Behavior", "Comportement");

        public static string CategoryData => T("Data", "Données");

        public static string CategoryMisc => T("Misc", "Divers");

        public static string CategoryAction => T("Action", "Action");

        public static string NameDescription => T(
            "Name of the element, used to refer to it from the code (for example in handler names).",
            "Nom de l'élément, utilisé pour y faire référence dans le code (par exemple dans les noms de gestionnaires).");

        /// <summary>User documentation of the layout attributes every element accepts (Dock, Anchor, X, Y, Width, Height).</summary>
        public static string CommonAttributeDoc(string name)
        {
            switch (name)
            {
                case "Dock": return T("Edge of the parent panel the element is docked to, or Fill to take the remaining space.", "Bord du panneau parent auquel l'élément est ancré, ou Fill pour occuper l'espace restant.");
                case "Anchor": return T("Edges of the parent panel the element stays attached to when it is resized, for example Top, Left.", "Bords du panneau parent auxquels l'élément reste attaché quand il est redimensionné, par exemple Top, Left.");
                case "X": return T("Distance from the left edge of the parent panel, in pixels.", "Distance depuis le bord gauche du panneau parent, en pixels.");
                case "Y": return T("Distance from the top edge of the parent panel, in pixels.", "Distance depuis le bord supérieur du panneau parent, en pixels.");
                case "Width": return T("Width of the element, in pixels.", "Largeur de l'élément, en pixels.");
                case "Height": return T("Height of the element, in pixels.", "Hauteur de l'élément, en pixels.");
                default: return string.Empty;
            }
        }

        /// <summary>The description of a greyed-out Dock/Anchor row, under a parent that does not lay out by them.</summary>
        public static string DockAnchorOnlyInPanel(string name) => T(
            $"{name} applies only to a child of a Panel (a container that places its children by Dock and Anchor); this element's container ignores it.",
            $"{name} ne s'applique qu'à un enfant d'un Panel (un conteneur qui place ses enfants par Dock et Anchor) ; le conteneur de cet élément l'ignore.");

        /// <summary>User documentation of the design-time attributes shown for the view (the root element).</summary>
        public static string DesignTimeAttributeDoc(string name) => name == "DesignWidth"
            ? T("Width of the view in the designer, in pixels (design time only, ignored when the app runs).", "Largeur de la vue dans le concepteur, en pixels (conception uniquement, ignorée à l'exécution).")
            : T("Height of the view in the designer, in pixels (design time only, ignored when the app runs).", "Hauteur de la vue dans le concepteur, en pixels (conception uniquement, ignorée à l'exécution).");

        // ---- Design surface context menus (docs/DESIGNER.md §12) ----

        public static string MenuCreateHandler => T("Create Handler", "Créer un gestionnaire");

        public static string MenuDuplicate => T("Duplicate", "Dupliquer");

        public static string MenuSelect => T("Select", "Sélectionner");

        public static string MenuWrapIn => T("Wrap In", "Envelopper dans");

        public static string MenuUnwrap => T("Remove Container", "Retirer le conteneur");

        public static string MenuProperties => T("Properties", "Propriétés");

        public static string MenuViewProperties => T("View Properties", "Propriétés de la vue");

        public static string MenuDesignSize => T("Design Size...", "Taille de conception…");

        // ---- Layout submenus (docs/DESIGNER.md §13) - the Windows Forms designer's Format menu names ----

        public static string MenuAlign => T("Align", "Aligner");

        public static string MenuMakeSameSize => T("Make Same Size", "Uniformiser la taille");

        public static string MenuHorizontalSpacing => T("Horizontal Spacing", "Espacement horizontal");

        public static string MenuVerticalSpacing => T("Vertical Spacing", "Espacement vertical");

        public static string MenuCenterInView => T("Center in View", "Centrer dans la vue");

        public static string DesignSizeTitle => T("Design Size", "Taille de conception");

        public static string DesignSizeWidth => T("Width:", "Largeur :");

        public static string DesignSizeHeight => T("Height:", "Hauteur :");

        public static string DesignSizeHint => T(
            "Size of the view on the design canvas, in pixels. It is written to Width/Height when the view declares them, else to the design-time DesignWidth/DesignHeight.",
            "Taille de la vue sur le canevas de conception, en pixels. Elle est écrite dans Width/Height si la vue les déclare, sinon dans DesignWidth/DesignHeight (conception uniquement).");

        public static string Ok => "OK";

        public static string Cancel => T("Cancel", "Annuler");

        public static string InvalidDesignSize => T("Enter whole numbers of at least 120 × 80.", "Saisissez des nombres entiers d'au moins 120 × 80.");

        public static string StatusClipboardEmpty => T("The clipboard holds no Kubuno view element to paste.", "Le Presse-papiers ne contient aucun élément de vue Kubuno à coller.");

        public static string StatusPasteRefused(string tag) => T(
            $"<{tag}> cannot be pasted here: neither the selection nor its container accepts it.",
            $"<{tag}> ne peut pas être collé ici : ni la sélection ni son conteneur ne l'acceptent.");

        public static string StatusDuplicateRefused(string tag) => T(
            $"<{tag}> cannot be duplicated: its container does not accept another one.",
            $"<{tag}> ne peut pas être dupliqué : son conteneur n'en accepte pas un autre.");

        public static string StatusWrapRefused(string tag, string container) => T(
            $"<{tag}> cannot be wrapped in a <{container}> here.",
            $"<{tag}> ne peut pas être enveloppé dans un <{container}> ici.");

        public static string StatusUnwrapRefused(string tag) => T(
            $"The container <{tag}> cannot be removed: its parent does not accept its children.",
            $"Le conteneur <{tag}> ne peut pas être retiré : son parent n'accepte pas ses enfants.");

        public static string StatusRootNotRemovable => T(
            "The view's root element cannot be cut or deleted.",
            "L'élément racine de la vue ne peut pas être coupé ni supprimé.");

        public static string InvalidBool(string value) => T(
            $"'{value}' is not a valid value: expected true, false or a {{Binding ...}} expression.",
            $"« {value} » n'est pas une valeur valide : true, false ou une expression {{Binding ...}} est attendue.");

        public static string InvalidNumber(string value) => T(
            $"'{value}' is not a valid number (use a dot as decimal separator) or {{Binding ...}} expression.",
            $"« {value} » n'est pas un nombre valide (utilisez le point comme séparateur décimal) ni une expression {{Binding ...}}.");

        public static string InvalidEnum(string value, string allowed) => T(
            $"'{value}' is not one of: {allowed} (or a {{Binding ...}} expression).",
            $"« {value} » ne fait pas partie de : {allowed} (ni une expression {{Binding ...}}).");

        public static string InvalidName(string value) => T(
            $"'{value}' is not a valid x:Name (letters, digits, '_' and '-', not starting with a digit).",
            $"« {value} » n'est pas un x:Name valide (lettres, chiffres, « _ » et « - », sans chiffre initial).");

        public static string InvalidHandlerName(string value) => T(
            $"'{value}' is not a valid Rust function name.",
            $"« {value} » n'est pas un nom de fonction Rust valide.");

        /// <summary>Toolbox tab display name for a registry family (<c>ComponentMeta.Family</c>).</summary>
        public static string ToolboxTabName(string family)
        {
            switch ((family ?? string.Empty).ToLowerInvariant())
            {
                case "core": return T("Common Controls", "Contrôles communs");
                case "display": return T("Display", "Affichage");
                case "choice": return T("Choice", "Choix");
                case "text": return T("Text", "Texte");
                case "containers": return T("Containers", "Conteneurs");
                case "data": return T("Data", "Données");
                default:
                    return family is null || family.Length == 0 ? "Kubuno" : char.ToUpperInvariant(family[0]) + family.Substring(1);
            }
        }
    }
}

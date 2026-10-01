using System;
using System.Globalization;
using Kubuno.Core.Logic.Localization;

namespace Kubuno.Desktop.Designer
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
        /// <summary>Test seam: forces a language instead of reading the current UI culture (the whole extension's, <see cref="UiLanguage"/>).</summary>
        public static bool? ForceFrench
        {
            get => UiLanguage.ForceFrench;
            set => UiLanguage.ForceFrench = value;
        }

        public static bool IsFrench => UiLanguage.IsFrench;

        private static string T(string english, string french) => IsFrench ? french : english;

        /// <summary>Appended to the document tab caption of the Design view, like WinForms' <c>Form1.cs [Design]</c>.</summary>
        public static string DesignCaptionSuffix => T(" [Design]", " [Conception]");

        /// <summary>The designer's runtime info bar (docs/DESIGNER.md section 15): its message, before the action link.</summary>
        public static string RuntimeBarMessage(DesignSurface.DesignSurfaceRuntimeState state) => state switch
        {
            DesignSurface.DesignSurfaceRuntimeState.NotBuilt => T(
                "Preview: bundled runtime - build the project to use its kubuno_ui.dll.",
                "Aperçu : runtime intégré — générez le projet pour utiliser sa kubuno_ui.dll."),
            DesignSurface.DesignSurfaceRuntimeState.Building => T(
                "Preview: building the preview with the project's kubuno_ui.dll...",
                "Aperçu : compilation de l'aperçu avec la kubuno_ui.dll du projet…"),
            DesignSurface.DesignSurfaceRuntimeState.Failed => T(
                "Preview: bundled runtime - the preview could not be built with the project's kubuno_ui.dll (see Output > Kubuno).",
                "Aperçu : runtime intégré — l'aperçu n'a pas pu être compilé avec la kubuno_ui.dll du projet (voir Sortie > Kubuno)."),
            DesignSurface.DesignSurfaceRuntimeState.NotApplicable => T(
                "Preview: bundled runtime - this view is not part of a project that uses kubuno-views.",
                "Aperçu : runtime intégré — cette vue n'appartient pas à un projet qui utilise kubuno-views."),
            _ => string.Empty,
        };

        /// <summary>The info bar's action link, empty when the state offers none.</summary>
        public static string RuntimeBarAction(DesignSurface.DesignSurfaceRuntimeState state) => state switch
        {
            DesignSurface.DesignSurfaceRuntimeState.NotBuilt => T("Build", "Générer"),
            DesignSurface.DesignSurfaceRuntimeState.Building => T("Cancel", "Annuler"),
            DesignSurface.DesignSurfaceRuntimeState.Failed => T("Build", "Générer"),
            _ => string.Empty,
        };

        /// <summary>Shown in the design pane when a surface's kubuno_ui.dll does not match the one it was built against.</summary>
        public static string RuntimeRejected(string detail) => T(
            "Preview refused: the kubuno_ui.dll this preview loaded is not the one it was built against (" + detail + "). Build the project again.",
            "Aperçu refusé : la kubuno_ui.dll chargée par l'aperçu n'est pas celle avec laquelle il a été compilé (" + detail + "). Générez à nouveau le projet.");

        public static string CategoryDesign => T("Design", "Design");

        public static string CategoryLayout => T("Layout", "Disposition");

        public static string CategoryAppearance => T("Appearance", "Apparence");

        public static string CategoryBehavior => T("Behavior", "Comportement");

        public static string CategoryData => T("Data", "Données");

        /// <summary>The printing components' category and Toolbox tab (docs/PRINTING.md), WinForms' "Printing" / "Impression".</summary>
        public static string CategoryPrinting => T("Printing", "Impression");

        public static string CategoryMisc => T("Misc", "Divers");

        public static string CategoryAction => T("Action", "Action");

        public static string CategoryAccessibility => T("Accessibility", "Accessibilité");

        public static string CategoryFocus => T("Focus", "Focus");

        /// <summary>The view's window properties (the "Vue/Form" category: ControlBox, TopMost, Opacity...).</summary>
        public static string CategoryWindowStyle => T("Window Style", "Style de fenêtre");

        /// <summary>The view's title bar properties (TitleBarHeight, TitleBarBackground, Subtitle, CaptionButtons...).</summary>
        public static string CategoryTitleBar => T("Title Bar", "Barre de titre");

        // ---- Rich property editors (docs/EVENTS.md, "WinForms-rich property sets") ----

        public static string InvalidColor(string value) => T(
            $"'{value}' is not a colour: use a theme colour (Primary, Surface...), #RRGGBB, #RRGGBBAA, a colour name (Red, CornflowerBlue) or a Windows colour (Control, WindowText).",
            $"« {value} » n'est pas une couleur : utilisez une couleur du thème (Primary, Surface…), #RRGGBB, #RRGGBBAA, un nom de couleur (Red, CornflowerBlue) ou une couleur Windows (Control, WindowText).");

        public static string InvalidFont(string value) => T(
            $"'{value}' is not a font: write it like 'Segoe UI, 12pt' or 'Segoe UI, 12pt, style=Bold, Italic'.",
            $"« {value} » n'est pas une police : écrivez-la comme « Segoe UI, 12pt » ou « Segoe UI, 12pt, style=Bold, Italic ».");

        public static string InvalidComposite(string value, string example) => T(
            $"'{value}' is not valid here: write it like '{example}'.",
            $"« {value} » n'est pas valide ici : écrivez-la comme « {example} ».");

        public static string InvalidOpacity(string value) => T(
            $"'{value}' is not an opacity: write a percentage between 0 % and 100 %.",
            $"« {value} » n'est pas une opacité : écrivez un pourcentage entre 0 % et 100 %.");

        /// <summary>The description of a sub-property of an expandable row (X under Location, Left under Padding...).</summary>
        public static string CompositePartDoc(string row, string part)
        {
            switch (part)
            {
                case "X": return T("Distance from the left edge of the container, in pixels.", "Distance depuis le bord gauche du conteneur, en pixels.");
                case "Y": return T("Distance from the top edge of the container, in pixels.", "Distance depuis le bord supérieur du conteneur, en pixels.");
                case "Width": return row == PropertyBrowser.KbviewElementObject.SizeRow
                    ? T("Width of the element, in pixels. auto: its natural width.", "Largeur de l'élément, en pixels. auto : sa largeur naturelle.")
                    : T("Width, in pixels. 0 means no limit.", "Largeur, en pixels. 0 signifie aucune limite.");
                case "Height": return row == PropertyBrowser.KbviewElementObject.SizeRow
                    ? T("Height of the element, in pixels. auto: its natural height.", "Hauteur de l'élément, en pixels. auto : sa hauteur naturelle.")
                    : T("Height, in pixels. 0 means no limit.", "Hauteur, en pixels. 0 signifie aucune limite.");
                case "All": return T("The same space on the four sides, in pixels (empty when the sides differ).", "Le même espace sur les quatre côtés, en pixels (vide quand les côtés diffèrent).");
                case "Left": return T("Space on the left side, in pixels.", "Espace du côté gauche, en pixels.");
                case "Top": return T("Space on the top side, in pixels.", "Espace du côté haut, en pixels.");
                case "Right": return T("Space on the right side, in pixels.", "Espace du côté droit, en pixels.");
                case "Bottom": return T("Space on the bottom side, in pixels.", "Espace du côté bas, en pixels.");
                default: return string.Empty;
            }
        }

        /// <summary>The Size row's name for a component that has a Size property of its own (a button's size).</summary>
        public static string DimensionsName => T("Dimensions", "Dimensions");

        /// <summary>The Location row's name for a component that has a Location property of its own.</summary>
        public static string PositionName => T("Position", "Position");

        public static string LocationDoc => T(
            "Position of the element's top-left corner in its container, in pixels (X, Y).",
            "Position du coin supérieur gauche de l'élément dans son conteneur, en pixels (X, Y).");

        public static string SizeDoc => T(
            "Size of the element, in pixels (width, height). auto keeps its natural size.",
            "Taille de l'élément, en pixels (largeur, hauteur). auto garde sa taille naturelle.");

        public static string DataBindingsDoc => T(
            "Values of this element taken from the view model: expand to link a property to a field of the view model.",
            "Valeurs de cet élément prises dans le modèle de vue : développez pour lier une propriété à un champ du modèle de vue.");

        public static string AdvancedBindingsName => T("(Advanced)", "(Avancé)");

        public static string AdvancedBindingsDoc => T(
            "Opens the list of every property of the element, to link any of them to the view model.",
            "Ouvre la liste de toutes les propriétés de l'élément, pour lier n'importe laquelle au modèle de vue.");

        public static string BindingPartDoc(string property) => T(
            $"The view model field {property} is linked to. Empty: {property} keeps the value written in the view.",
            $"Champ du modèle de vue auquel {property} est lié. Vide : {property} garde la valeur écrite dans la vue.");

        public static string CollectionValue => T("(Collection)", "(Collection)");

        /// <summary>The description of a collection row (Columns, TabPages, Items...).</summary>
        public static string CollectionDoc(string row, string childTag) => row switch
        {
            "Columns" => T("The columns of the list. Click ... to add, remove, reorder or change them.", "Colonnes de la liste. Cliquez sur ... pour les ajouter, les supprimer, les réordonner ou les modifier."),
            "TabPages" => T("The tabs. Click ... to add, remove, reorder or rename them.", "Onglets. Cliquez sur ... pour les ajouter, les supprimer, les réordonner ou les renommer."),
            "Steps" => T("The steps. Click ... to add, remove, reorder or change them.", "Étapes. Cliquez sur ... pour les ajouter, les supprimer, les réordonner ou les modifier."),
            "Sections" => T("The sections. Click ... to add, remove, reorder or change them.", "Sections. Cliquez sur ... pour les ajouter, les supprimer, les réordonner ou les modifier."),
            _ => T($"The {childTag} elements it contains. Click ... to add, remove, reorder or change them.", $"Éléments {childTag} qu'il contient. Cliquez sur ... pour les ajouter, les supprimer, les réordonner ou les modifier."),
        };

        public static string StringListDoc => T(
            "The items of the list, one per line. Click ... to edit them.",
            "Éléments de la liste, un par ligne. Cliquez sur ... pour les modifier.");

        public static string StringListTitle => T("String Collection Editor", "Éditeur de collections de chaînes");

        public static string StringListHint => T("Enter the items, one per line:", "Entrez les éléments, un par ligne :");

        public static string CollectionEditorTitle(string row) => T($"{row} Collection Editor", $"Éditeur de collections {row}");

        public static string CollectionMembers => T("Members:", "Membres :");

        public static string MoveUp => T("Move up", "Monter");

        public static string MoveDown => T("Move down", "Descendre");

        public static string CollectionProperties(string item) => T($"{item} properties:", $"Propriétés de {item} :");

        public static string Add => T("Add", "Ajouter");

        public static string Remove => T("Remove", "Supprimer");

        public static string Browse => T("Browse...", "Parcourir…");

        public static string ToolTipOn(string? component) => string.IsNullOrEmpty(component) ? "ToolTip" : T($"ToolTip on {component}", $"ToolTip sur {component}");

        public static string ToolTipDoc => T(
            "Text of the tooltip shown when the mouse pointer rests on the element.",
            "Texte de l'info-bulle affichée quand le pointeur de la souris s'arrête sur l'élément.");

        // Colour editor.
        public static string ColorTabTheme => T("Theme", "Thème");

        public static string ColorTabCustom => T("Custom", "Personnalisée");

        public static string ColorTabWeb => T("Web", "Web");

        public static string ColorTabSystem => T("System", "Système");

        public static string ColorDefine => T("Define colors...", "Définir les couleurs…");

        public static string ColorLightDark => T("light / dark", "clair / sombre");

        /// <summary>The contrast line of the colour editor: <c>✓ 7.2:1 light · ⚠ 3.1:1 dark</c>.</summary>
        public static string ContrastLine(double light, double dark)
        {
            string Part(double ratio, string theme) => (ratio >= PropertyBrowser.ColorText.MinimumContrast ? "✓ " : "⚠ ") + ratio.ToString("0.0", CultureInfo.InvariantCulture) + ":1 " + theme;
            return T("Contrast: ", "Contraste : ") + Part(light, T("light", "clair")) + " · " + Part(dark, T("dark", "sombre"));
        }

        public static string ContrastWarning => T(
            "Hard to read in a theme: at least 4.5:1 is recommended.",
            "Difficile à lire dans un thème : au moins 4,5:1 est recommandé.");

        // Image editor.
        public static string ImageEditorTitle => T("Select Resource", "Sélectionner la ressource");

        public static string ImageProjectResources => T("Images of the project:", "Images du projet :");

        public static string ImageNone => T("(none)", "(aucune)");

        public static string ImageCopyPrompt(string file, string folder) => T(
            $"{file} is outside the view's folder. Copy it into {folder} so the application finds it?",
            $"{file} est en dehors du dossier de la vue. Le copier dans {folder} pour que l'application le trouve ?");

        public static string ImageFilter => T("Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico|All files|*.*", "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico|Tous les fichiers|*.*");

        // Binding editor.
        public static string BindingEditorTitle => T("Data Binding", "Liaison de données");

        public static string BindingPath => T("View model field:", "Champ du modèle de vue :");

        public static string BindingMode => T("Mode:", "Mode :");

        public static string BindingModeDoc(string mode) => mode switch
        {
            "TwoWay" => T("TwoWay: the element also updates the field (a text typed, a box checked).", "TwoWay : l'élément met aussi à jour le champ (texte saisi, case cochée)."),
            "OneTime" => T("OneTime: the field is read once, when the view opens.", "OneTime : le champ n'est lu qu'une fois, à l'ouverture de la vue."),
            _ => T("OneWay: the element shows the field and follows its changes.", "OneWay : l'élément affiche le champ et suit ses changements."),
        };

        public static string BindingNone => T("(none)", "(aucune)");

        public static string BindingsAdvancedTitle => T("Advanced Binding", "Liaison avancée");

        /// <summary>
        /// An event category of the registry export (English, <c>kubuno_views::registry::EventCategory::name</c>) in
        /// Visual Studio's UI language - the Windows Forms designer's own names (Action, Comportement, Focus,
        /// Touche, Souris, Glisser-déplacer, Disposition, Propriété modifiée...). Unknown or missing = Action.
        /// </summary>
        public static string EventCategory(string? category)
        {
            switch (category)
            {
                case "Behavior": return CategoryBehavior;
                case "Data": return CategoryData;
                case "Drag Drop": return T("Drag Drop", "Glisser-déplacer");
                case "Focus": return T("Focus", "Focus");
                case "Key": return T("Key", "Touche");
                case "Layout": return CategoryLayout;
                case "Mouse": return T("Mouse", "Souris");
                case "Property Changed": return T("Property Changed", "Propriété modifiée");
                case "Appearance": return CategoryAppearance;
                default: return CategoryAction;
            }
        }

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

        /// <summary>"Add" › on a ribbon element (docs/RIBBON.md section 9).</summary>
        public static string MenuRibbonAdd => T("Add", "Ajouter");

        public static string MenuRibbonCreateCommand => T("Create Command from this Button", "Créer une commande à partir de ce bouton");

        public static string MenuRibbonChooseIcon => T("Choose Icon...", "Choisir l'icône...");

        public static string MenuRibbonEditLabel => T("Edit Label...", "Modifier le libellé...");

        public static string MenuRibbonSize => T("Size", "Taille");

        /// <summary>"Edit Items...", "Edit Groups..."... - a ribbon element's collection editor.</summary>
        public static string MenuRibbonEditCollection(string row) => row switch
        {
            "Groups" => T("Edit Groups...", "Modifier les groupes..."),
            "Tabs" => T("Edit Tabs...", "Modifier les onglets..."),
            "DropDownItems" => T("Edit Drop-Down Items...", "Modifier les éléments du menu..."),
            _ => T("Edit Items...", "Modifier les éléments..."),
        };

        public static string MenuSelect => T("Select", "Sélectionner");

        public static string MenuWrapIn => T("Wrap In", "Envelopper dans");

        public static string MenuUnwrap => T("Remove Container", "Retirer le conteneur");

        public static string MenuProperties => T("Properties", "Propriétés");

        public static string MenuViewProperties => T("View Properties", "Propriétés de la vue");

        public static string MenuDesignSize => T("Design Size...", "Taille de conception…");

        // ---- Handler commands (docs/EVENTS.md §5.4/§5.5, EVT-5) ----

        public static string MenuConvertHandlers => T("Convert to Typed Handlers", "Convertir en gestionnaires typés");

        public static string MenuChooseToolboxItems => T("Choose Toolbox Items...", "Choisir des éléments...");

        /// <summary>The server refused a handler command (<paramref name="reason"/> is its English explanation).</summary>
        public static string HandlerCommandRefused(string action, string reason) => T($"{action}: {reason}.", $"{action} : {reason}.");

        public static string HandlerCommandNothingToDo(string action) => T($"{action}: nothing to change.", $"{action} : rien à modifier.");

        public static string HandlerCommandFailed(string action, string file, string failure) => T(
            $"{action}: {file} could not be edited ({failure}).",
            $"{action} : {file} n'a pas pu être modifié ({failure}).");

        public static string HandlersConverted(int count) => T(
            $"{count} handler(s) converted to typed handlers.",
            $"{count} gestionnaire(s) converti(s) en gestionnaires typés.");

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
        /// <summary>Every Toolbox tab name the registry families map to, in both languages (stale-tab cleanup).</summary>
        public static string[] AllToolboxTabNames() => new[]
        {
            "Common Controls", "Contrôles communs", "Display", "Affichage", "Choice", "Choix", "Text", "Texte",
            "Containers", "Conteneurs", "Data", "Données", "Components", "Composants", "Printing", "Impression",
            "Docking", "Ancrage", "Navigation", "Ribbon", "Ruban",
        };

        /// <summary>The Toolbox tab of a project's own controls (docs/EVENTS.md EVT-7b), like WinForms' "&lt;Project&gt; Components".</summary>
        public static string ProjectToolboxTabName(string project) => T(project + " Components", project + " Composants");

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
                case "docking": return T("Docking", "Ancrage");
                case "navigation": return "Navigation";
                case "ribbon": return T("Ribbon", "Ruban");
                case "printing": return CategoryPrinting;
                case "components": return T("Components", "Composants");
                default:
                    return family is null || family.Length == 0 ? "Kubuno" : char.ToUpperInvariant(family[0]) + family.Substring(1);
            }
        }
    }
}

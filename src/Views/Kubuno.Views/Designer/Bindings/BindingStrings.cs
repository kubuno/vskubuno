using System;

namespace Kubuno.Desktop.Designer.Bindings
{
    /// <summary>What a property shows for a sample value through a binding (<c>kubuno/bindingPreview</c>).</summary>
    public sealed class BindingPreview
    {
        public BindingPreview(string? text, string? note)
        {
            Text = text;
            Note = note;
        }

        /// <summary>The value shown; null when the property falls back to its default.</summary>
        public string? Text { get; }

        /// <summary>Why it may differ at run time (a project converter the server cannot run), or null.</summary>
        public string? Note { get; }
    }

    /// <summary>The texts of the data binding UI (picker, dialog, Properties window markers, menu), French or English like Visual Studio.</summary>
    public static class BindingStrings
    {
        private static string T(string english, string french) => DesignerText.IsFrench ? french : english;

        public static string DialogTitle => T("Data Binding", "Liaison de données");

        public static string Property => T("Property:", "Propriété :");

        public static string Source => T("Binding source", "Source de la liaison");

        public static string PathLabel => T("Path:", "Chemin :");

        public static string SourceLabel => T("Source (component):", "Source (composant) :");

        public static string ModeLabel => T("Mode:", "Mode :");

        public static string TriggerLabel => T("Update source:", "Mise à jour de la source :");

        public static string ConverterLabel => T("Converter:", "Convertisseur :");

        public static string ConverterParameterLabel => T("Converter parameter:", "Paramètre du convertisseur :");

        public static string FormatLabel => T("Format (StringFormat):", "Format (StringFormat) :");

        public static string CultureLabel => T("Culture:", "Culture :");

        public static string NullValueLabel => T("Value when null (TargetNullValue):", "Valeur si nulle (TargetNullValue) :");

        public static string FallbackLabel => T("Fallback value (FallbackValue):", "Valeur de repli (FallbackValue) :");

        public static string DesignValueLabel => T("Design-time value (d:):", "Valeur de conception (d:) :");

        public static string SampleLabel => T("Sample source value:", "Valeur source d'exemple :");

        public static string PreviewLabel => T("Shown:", "Affichage :");

        public static string ExpressionLabel => T("Expression:", "Expression :");

        public static string Formatting => T("Format and conversion", "Format et conversion");

        public static string Behaviour => T("Behaviour", "Comportement");

        public static string RemoveBinding => T("Remove binding (restore the value)", "Supprimer la liaison (rétablir la valeur)");

        public static string CreateBinding => T("Create Data Binding...", "Créer une liaison...");

        public static string EditBinding => T("Edit Data Binding...", "Modifier la liaison...");

        public static string GoToDefinition => T("Go To Definition", "Aller à la définition");

        public static string Advanced => T("Advanced...", "Avancé...");

        public static string EditValue => T("Edit the value...", "Modifier la valeur...");

        public static string Values => T("Values", "Valeurs");

        public static string Search => T("Search", "Rechercher");

        public static string None => T("(none)", "(aucune)");

        public static string NoSources => T(
            "The language server lists nothing to bind here: write a #[bind] field, a #[property] of the user control, or an impl ViewModel in the view's code-behind.",
            "Le serveur de langage ne liste rien à lier ici : ajoutez un champ #[bind], une #[property] du contrôle utilisateur ou un impl ViewModel dans le code-behind de la vue.");

        public static string OpenContext => T("The data context may answer other paths (type them).", "Le contexte de données peut répondre à d'autres chemins (saisissez-les).");

        /// <summary>The title of a group of the picker.</summary>
        public static string Group(string id, string label) => id switch
        {
            "item" => T("Item (row of ", "Élément (ligne de ") + label + ")",
            "context" => label.Length > 0 ? T("Data context", "Contexte de données") + " — " + label : T("Data context", "Contexte de données"),
            "sources" => T("Data sources", "Sources de données"),
            "resources" => T("Resources (.kbres)", "Ressources (.kbres)"),
            _ => label,
        };

        public static string Mode(string mode) => mode switch
        {
            "TwoWay" => T("TwoWay: reads the source and writes the user's changes back.", "TwoWay : lit la source et y écrit les modifications de l'utilisateur."),
            "OneTime" => T("OneTime: reads the source once, when the view is built.", "OneTime : lit la source une seule fois, à la construction de la vue."),
            "OneWayToSource" => T("OneWayToSource: only writes the user's changes to the source.", "OneWayToSource : écrit seulement les modifications de l'utilisateur dans la source."),
            _ => T("OneWay (default): the property follows the source.", "OneWay (défaut) : la propriété suit la source."),
        };

        public static string Trigger(string trigger) => trigger switch
        {
            "LostFocus" => T("LostFocus: when the control loses the focus.", "LostFocus : quand le contrôle perd le focus."),
            "Explicit" => T("Explicit: when the code calls binding::update_sources.", "Explicit : quand le code appelle binding::update_sources."),
            _ => T("PropertyChanged (default): at each change.", "PropertyChanged (défaut) : à chaque modification."),
        };

        /// <summary>The short label of a binding problem shown in the value cell.</summary>
        public static string IssueShort(string code) => code switch
        {
            "binding-unknown-path" => T("unknown path", "chemin inconnu"),
            "binding-type" => T("type mismatch", "type incompatible"),
            "binding-unknown-converter" => T("unknown converter", "convertisseur inconnu"),
            "binding-read-only" => T("read-only", "lecture seule"),
            "binding-no-path" => T("no path", "pas de chemin"),
            _ => T("check the binding", "liaison à vérifier"),
        };

        public static string Malformed(string text) => T(
            $"'{text}' is not a binding expression the runtime reads: write {{Binding Path[, Key=Value…]}} or {{Res key}} (or pick one with the drop-down).",
            $"« {text} » n'est pas une expression de liaison lue par le runtime : écrivez {{Binding Chemin[, Clé=Valeur…]}} ou {{Res clé}} (ou choisissez-la dans la liste déroulante).");

        public static string NotBound => T("The property is not bound.", "La propriété n'est pas liée.");

        public static string NoDefinition => T("No definition found for this binding.", "Aucune définition trouvée pour cette liaison.");

        public static string DroppedOn(string member, string property, string element) => T(
            $"Bound {element}.{property} to {member}",
            $"{element}.{property} lié à {member}");

        public static string Converted(string name, bool project) => project ? name + T(" (project)", " (projet)") : name;

        /// <summary>The type text of a member in the picker.</summary>
        public static string TypeOf(BindingMember m) => m.Writable ? m.TypeText : m.TypeText + T(", read-only", ", lecture seule");

        public static string Incompatible(BindingShape have, BindingShape? want) =>
            want is null ? string.Empty : T($"{have} does not fit a {want} property", $"{have} ne convient pas à une propriété {want}");

        public static string WithIssue(string message) => "⚠ " + message;

        public static string Combine(params string?[] parts) => string.Join(Environment.NewLine, Array.FindAll(parts, p => !string.IsNullOrEmpty(p)));
    }
}

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
            "The element's x:Name: a stable identity used by handlers and focus (not a generated field).",
            "Le x:Name de l'élément : identité stable utilisée par les gestionnaires et le focus (pas un champ généré).");

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

namespace Kubuno.Views.Designer.Icons
{
    /// <summary>The icon picker's and icon editors' user text, in English or French like the rest of the designer (<see cref="DesignerText"/>).</summary>
    public static class IconText
    {
        private static string T(string english, string french) => DesignerText.IsFrench ? french : english;

        public static string PickerTitle => T("Select Icon", "Sélectionner l'icône");

        public static string CategoryIcon => T("Icon", "Icône");

        public static string TabKubuno => T("Kubuno icons", "Icônes Kubuno");

        public static string TabProject => T("Project images", "Images du projet");

        public static string Search => T("Search icons (name or keyword)", "Rechercher une icône (nom ou mot-clé)");

        public static string SetAll => T("All sets", "Tous les jeux");

        public static string SetRecent => T("Recently used", "Utilisées récemment");

        public static string SetName(string set) => set switch
        {
            "Lucide" => "Lucide",
            "Kubuno" => T("Kubuno (themed)", "Kubuno (à thème)"),
            "Modules" => T("Module logos", "Logos des modules"),
            _ => set,
        };

        public static string None => T("(none)", "(aucune)");

        public static string NoneButton => T("No icon", "Aucune icône");

        public static string Preview => T("Preview", "Aperçu");

        public static string Light => T("Light", "Clair");

        public static string Dark => T("Dark", "Sombre");

        public static string Import => T("Import...", "Importer…");

        public static string ProjectResource => T("Resource of the project...", "Ressource du projet…");

        public static string Count(int shown, int total) => T($"{shown} of {total} icons", $"{shown} icônes sur {total}");

        public static string NoProjectImages => T(
            "No image in the project yet. Import an SVG, PNG, ICO... file: it is copied into the view's resources folder.",
            "Aucune image dans le projet pour l'instant. Importez un fichier SVG, PNG, ICO… : il est copié dans le dossier resources de la vue.");

        public static string CatalogUnavailable => T(
            "The Kubuno icon set is not available yet (the views language server is starting). Type a name, or try again in a moment.",
            "Le jeu d'icônes Kubuno n'est pas encore disponible (le serveur de langage des vues démarre). Tapez un nom, ou réessayez dans un instant.");

        public static string ValueLabel => T("Value:", "Valeur :");

        public static string ImportFilter => T(
            "Images|*.svg;*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|All files|*.*",
            "Images|*.svg;*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|Tous les fichiers|*.*");

        public static string ImportPrompt(string file, string folder) => T(
            $"Copy {file} into the project ({folder})? Choose No to use it where it is.",
            $"Copier {file} dans le projet ({folder}) ? Choisissez Non pour l'utiliser là où il se trouve.");

        public static string ChooseIconVerb => T("Choose Icon...", "Choisir l'icône…");

        public static string AlignmentName(string value) => value switch
        {
            "TopLeft" => T("Top left", "En haut à gauche"),
            "TopCenter" => T("Top centre", "En haut au centre"),
            "TopRight" => T("Top right", "En haut à droite"),
            "MiddleLeft" => T("Middle left", "Au milieu à gauche"),
            "MiddleCenter" => T("Middle centre", "Au centre"),
            "MiddleRight" => T("Middle right", "Au milieu à droite"),
            "BottomLeft" => T("Bottom left", "En bas à gauche"),
            "BottomCenter" => T("Bottom centre", "En bas au centre"),
            "BottomRight" => T("Bottom right", "En bas à droite"),
            _ => value,
        };
    }
}

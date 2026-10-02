using System.Globalization;
using Kubuno.Shared.Logic.Localization;

namespace Kubuno.Views.Resources
{
    /// <summary>
    /// The user-visible strings of the <c>.kbres</c> resource editor (docs/RESOURCES.md): English and French, picked
    /// from the extension's UI language (<see cref="UiLanguage"/>) - same pattern as <c>Designer\DesignerText</c>.
    /// </summary>
    public static class ResourceText
    {
        private static string T(string english, string french) => UiLanguage.IsFrench ? french : english;

        // ---- Resource editor ----

        public static string Strings => T("Strings", "Chaînes");

        public static string Images => T("Images", "Images");

        public static string Icons => T("Icons", "Icônes");

        public static string Audio => T("Audio", "Audio");

        public static string Files => T("Files", "Fichiers");

        public static string Other => T("Other", "Autres");

        public static string AddResource => T("Add Resource", "Ajouter une ressource");

        public static string AddExistingFile => T("Add Existing File...", "Ajouter un fichier existant...");

        public static string AddNewString => T("Add New String", "Ajouter une chaîne");

        public static string NewImage => T("Add New Image", "Nouvelle image");

        public static string NewIcon => T("Add New Icon", "Nouvelle icône");

        public static string NewTextFile => T("Add New Text File", "Nouveau fichier texte");

        public static string RemoveResource => T("Remove Resource", "Supprimer la ressource");

        public static string Persistence => T("Persistence:", "Persistance :");

        public static string Linked => T("Linked to file", "Lié au fichier");

        public static string Embedded => T("Embedded in .kbres", "Incorporé dans le .kbres");

        public static string AddCulture => T("Add culture...", "Ajouter une culture...");

        public static string ShowCultures => T("Show cultures", "Afficher les cultures");

        public static string ColName => T("Name", "Nom");

        public static string ColValue => T("Value", "Valeur");

        public static string ColComment => T("Comment", "Commentaire");

        public static string Open => T("Open", "Ouvrir");

        public static string Rename => T("Rename", "Renommer");

        public static string EditComment => T("Edit Comment...", "Modifier le commentaire...");

        public static string CopyReference => T("Copy reference {Res key}", "Copier la référence {Res clé}");

        public static string Remove => T("Remove", "Supprimer");

        public static string PersistenceMenu => T("Persistence", "Persistance");

        public static string RenameTitle => T("Rename resource", "Renommer la ressource");

        public static string RenamePrompt => T("New name:", "Nouveau nom :");

        public static string CommentTitle => T("Resource comment", "Commentaire de la ressource");

        public static string CommentPrompt => T("Comment:", "Commentaire :");

        public static string CultureTitle => T("Add culture", "Ajouter une culture");

        public static string CulturePrompt => T("Culture name (for example fr, fr-FR, zh-Hans):", "Nom de la culture (par exemple fr, fr-FR, zh-Hans) :");

        public static string CultureInvalid => T("This is not a culture name (fr, fr-FR, zh-Hans...).", "Ce n'est pas un nom de culture (fr, fr-FR, zh-Hans...).");

        public static string Ok => T("OK", "OK");

        public static string Cancel => T("Cancel", "Annuler");

        public static string EditorTitle => T("Kubuno Resource Editor", "Éditeur de ressources Kubuno");

        public static string ResourceEditorGallery => T("Resource editor", "Éditeur de ressources") + " (ResourceEditorView)";

        public static string NeutralColumn => T("(neutral)", "(neutre)");

        public static string NoResources => T("No resources in this category. Drop files here or use Add Resource.", "Aucune ressource dans cette catégorie. Déposez des fichiers ici ou utilisez Ajouter une ressource.");

        public static string ConfirmRemove(int count, string firstName) => count == 1
            ? T("Remove the resource `" + firstName + "` (in every culture)?", "Supprimer la ressource `" + firstName + "` (dans toutes les cultures) ?")
            : T("Remove the " + count.ToString(CultureInfo.InvariantCulture) + " selected resources (in every culture)?", "Supprimer les " + count.ToString(CultureInfo.InvariantCulture) + " ressources sélectionnées (dans toutes les cultures) ?");

        public static string Entries(int count) => count == 1 ? T("1 resource", "1 ressource") : count.ToString(CultureInfo.InvariantCulture) + T(" resources", " ressources");

        public static string CultureStatus(string culture, int missing) => missing == 0
            ? culture
            : culture + " (" + missing.ToString(CultureInfo.InvariantCulture) + T(" missing", " manquante(s)") + ")";

        public static string InvalidFile(string message, int line) => T(
            "This file cannot be edited here: " + message + " (line " + line.ToString(CultureInfo.InvariantCulture) + "). Fix the XML in the code view.",
            "Ce fichier ne peut pas être modifié ici : " + message + " (ligne " + line.ToString(CultureInfo.InvariantCulture) + "). Corrigez le XML dans la vue code.");

        public static string ViewCode => T("View Code", "Afficher le code");

        public static string DetailKind => T("Kind", "Type");

        public static string DetailFormat => T("Format", "Format");

        public static string DetailSize => T("Size", "Taille");

        public static string DetailPersistence => T("Persistence", "Persistance");

        public static string DetailPath => T("Path", "Chemin");

        public static string DetailComment => T("Comment", "Commentaire");

        public static string DetailMissingFile => T("(file not found)", "(fichier introuvable)");

        public static string PersistenceName(bool embedded) => embedded ? Embedded : Linked;

        public static string KindName(Kubuno.Views.Logic.Resources.ResourceKind kind) => kind switch
        {
            Kubuno.Views.Logic.Resources.ResourceKind.String => T("String", "Chaîne"),
            Kubuno.Views.Logic.Resources.ResourceKind.Image => T("Image", "Image"),
            Kubuno.Views.Logic.Resources.ResourceKind.Icon => T("Icon", "Icône"),
            Kubuno.Views.Logic.Resources.ResourceKind.Audio => T("Audio", "Audio"),
            Kubuno.Views.Logic.Resources.ResourceKind.File => T("File", "Fichier"),
            Kubuno.Views.Logic.Resources.ResourceKind.Color => T("Color", "Couleur"),
            _ => T("Font", "Police"),
        };

        public static string AddFilesFilter => T("All files (*.*)|*.*", "Tous les fichiers (*.*)|*.*");

        public static string CannotCreateFile(string path, string detail) => T("Cannot create `" + path + "`: " + detail, "Impossible de créer `" + path + "` : " + detail);

        public static string SatelliteOpenFailed(string path, string detail) => T("Cannot open the culture file `" + path + "`: " + detail, "Impossible d'ouvrir le fichier de culture `" + path + "` : " + detail);

        // ---- Select Resource dialog (Designer\Resources) ----

        public static string SelectResourceTitle => T("Select Resource", "Sélectionner la ressource");

        public static string ResourceContext => T("Resource context", "Contexte de la ressource");

        public static string LocalResource => T("_Local resource:", "Ressource _locale :");

        public static string ProjectResourceFile => T("_Project resource file:", "Fichier de ressources du _projet :");

        public static string Import => T("_Import...", "_Importer...");

        public static string Clear => T("_Clear", "_Effacer");

        public static string None => T("(none)", "(aucune)");

        public static string NewResourceFile(string name) => T("(new) " + name, "(nouveau) " + name);

        public static string NoPreview => T("No preview", "Aucun aperçu");

        public static string SvgPreview => T("SVG image (shown at run time)", "Image SVG (affichée à l'exécution)");

        public static string ImageFilter => T(
            "Images|*.svg;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.tif;*.tiff;*.webp|All files|*.*",
            "Images|*.svg;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.tif;*.tiff;*.webp|Tous les fichiers|*.*");

        public static string IconFilter => T("Icons|*.ico;*.svg;*.png|All files|*.*", "Icônes|*.ico;*.svg;*.png|Tous les fichiers|*.*");

        public static string ImportFailed(string message) => T("The file could not be imported: " + message, "Le fichier n'a pas pu être importé : " + message);

        public static string CopyIntoProject(string file, string folder) => T(
            "\"" + file + "\" is outside the view's folder. Copy it into \"" + folder + "\"?",
            "« " + file + " » est hors du dossier de la vue. Le copier dans « " + folder + " » ?");

        public static string Details(string kind, string format, string size, string persistence) => kind + " · " + format + (size.Length > 0 ? " · " + size : string.Empty) + " · " + persistence;

        public static string DetailLinked => T("linked", "lié");

        public static string DetailEmbedded => T("embedded", "incorporé");

        // ---- Design-time language (designer tab strip) ----

        public static string DesignLanguage => T("Language:", "Langue :");

        public static string DesignLanguageTip => T(
            "Previews the view in another culture (its {Res …} values); does not change the application.",
            "Affiche la vue dans une autre culture (valeurs {Res …}) ; ne change pas l'application.");

        public static string DefaultLanguage => T("(Default)", "(Par défaut)");
    }
}

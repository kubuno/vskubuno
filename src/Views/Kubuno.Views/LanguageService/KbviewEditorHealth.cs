using System;
using System.IO;

namespace Kubuno.Views.LanguageService
{
    /// <summary>
    /// What went wrong when a <c>.kbview</c>/<c>.kbcontrol</c> file opened in an editor whose buffer is not of the "kbview"
    /// content type (Visual Studio's XML editor, most often), and how to repair it. Pure logic, used by
    /// <see cref="KbviewWrongEditorDetector"/> for its Output pane line and info bar.
    ///
    /// Root cause found live (docs/INTELLISENSE.md, "Diagnostics and troubleshooting"): the extension's MEF parts were
    /// loaded (so the "kbview" content type exists) but its pkgdef registrations were not - the package, its editor
    /// factories and the <c>.kbview</c> association to the core text editor. Visual Studio then opens the file in its XML
    /// editor: no language server (no completion), and every <c>x:</c> reported as an undeclared prefix. Two ways to get
    /// there were seen: a VSIX installed into a hive that had never run (its pkgdef cache is built without the
    /// extension and is not invalidated afterwards), and a hive whose cache still points at a deleted copy of the
    /// extension (an earlier F5 deployment). Both are repaired by rebuilding the configuration cache
    /// (<c>devenv /updateconfiguration</c>) with Visual Studio closed.
    /// </summary>
    public static class KbviewEditorHealth
    {
        /// <summary>Why the view is not in the Kubuno editor.</summary>
        public enum Cause
        {
            /// <summary>The Kubuno package could not be loaded: its registration is missing or stale.</summary>
            PackageNotLoaded,

            /// <summary>The package loads, but the file still went to another editor ("Open With…", an old default).</summary>
            OtherEditorChosen,
        }

        /// <summary>
        /// The command that rebuilds Visual Studio's configuration cache of this instance: <paramref name="devenvPath"/>
        /// quoted, <c>/updateconfiguration</c>, and <c>/rootsuffix</c> when the instance is not the regular one.
        /// </summary>
        /// <param name="devenvPath">The running <c>devenv.exe</c>.</param>
        /// <param name="registryRoot">The instance's registry root (<c>VSSPROPID_VirtualRegistryRoot</c>), e.g.
        /// <c>Software\Microsoft\VisualStudio\18.0_dc9e2338Exp</c>: its suffix after the version and instance id is the
        /// root suffix.</param>
        public static string RepairCommand(string devenvPath, string? registryRoot)
        {
            var suffix = RootSuffix(registryRoot);
            var command = $"\"{devenvPath}\" /updateconfiguration";
            return string.IsNullOrEmpty(suffix) ? command : command + " /rootsuffix " + suffix;
        }

        /// <summary>
        /// The root suffix of a registry root such as <c>Software\Microsoft\VisualStudio\18.0_dc9e2338Exp</c> (<c>Exp</c>),
        /// or an empty string for the regular instance (<c>18.0_dc9e2338</c>, <c>18.0</c>).
        /// </summary>
        public static string RootSuffix(string? registryRoot)
        {
            if (string.IsNullOrWhiteSpace(registryRoot))
            {
                return string.Empty;
            }

            var last = registryRoot!.TrimEnd('\\').Split('\\');
            var key = last[last.Length - 1];
            var underscore = key.IndexOf('_');
            if (underscore < 0)
            {
                // "18.0Exp" (an instance without an id): what follows the version digits and dots.
                var i = 0;
                while (i < key.Length && (char.IsDigit(key[i]) || key[i] == '.'))
                {
                    i++;
                }

                return key.Substring(i);
            }

            // "18.0_dc9e2338Exp": the instance id is 8 hexadecimal digits.
            var rest = key.Substring(underscore + 1);
            return rest.Length > 8 ? rest.Substring(8) : string.Empty;
        }

        /// <summary>The Output pane line describing the problem (English, like the rest of the log).</summary>
        public static string LogLine(string path, string contentType, Cause cause, string? packageError, string repairCommand)
        {
            var file = Path.GetFileName(path);
            var why = cause == Cause.PackageNotLoaded
                ? "the Kubuno package is not registered in this Visual Studio instance" + (string.IsNullOrEmpty(packageError) ? string.Empty : $" ({packageError})")
                : "another editor was chosen for it";
            return $"'{file}' opened in an editor of content type '{contentType}' instead of the Kubuno view editor: {why}. " +
                   "There is no Kubuno IntelliSense in it, and an XML editor reports every undeclared x: prefix as an error. " +
                   (cause == Cause.PackageNotLoaded
                       ? $"Repair: close Visual Studio, run {repairCommand}, then reopen the solution."
                       : "Reopen it with the Kubuno editor (Open With... > Kubuno View Designer, or the info bar's button).");
        }

        /// <summary>The info bar text, in Visual Studio's language.</summary>
        public static string InfoBarText(string path, Cause cause, bool french)
        {
            var file = Path.GetFileName(path);
            if (french)
            {
                return cause == Cause.PackageNotLoaded
                    ? $"Kubuno : « {file} » s'est ouvert dans l'éditeur XML, car l'extension Kubuno n'est pas enregistrée dans cette instance de Visual Studio (cache de configuration périmé). Pas d'IntelliSense Kubuno ici. Réparation : fermez Visual Studio et exécutez la commande copiée par le bouton, puis rouvrez la solution."
                    : $"Kubuno : « {file} » s'est ouvert dans un autre éditeur que celui des vues Kubuno : pas d'IntelliSense Kubuno ici.";
            }

            return cause == Cause.PackageNotLoaded
                ? $"Kubuno: '{file}' opened in the XML editor because the Kubuno extension is not registered in this Visual Studio instance (stale configuration cache). No Kubuno IntelliSense here. Repair: close Visual Studio and run the command the button copies, then reopen the solution."
                : $"Kubuno: '{file}' opened in another editor than the Kubuno view editor: no Kubuno IntelliSense here.";
        }

        /// <summary>Whether <paramref name="contentTypeName"/> is one whose buffer the Kubuno views language server serves.</summary>
        public static bool IsKbviewContentType(string? contentTypeName) =>
            string.Equals(contentTypeName, KbviewConstants.ContentType, StringComparison.OrdinalIgnoreCase);
    }
}

using System;
using System.IO;

namespace Kubuno.Desktop.Logic
{
    /// <summary>
    /// The two kinds of view file (docs/VIEWS-SPEC.md, "File kinds"): a form, window or dialog is a <c>.kbview</c>, a
    /// user control is a <c>.kbcontrol</c>. Both hold the same XML format and open in the same designer; only their
    /// role (and so their Solution Explorer icon and item templates) differs.
    /// </summary>
    public static class ViewFiles
    {
        /// <summary>The extension of a form, window or dialog view.</summary>
        public const string ViewExtension = ".kbview";

        /// <summary>The extension of a user control view.</summary>
        public const string ControlExtension = ".kbcontrol";

        /// <summary>Both extensions, the form one first.</summary>
        public static readonly string[] Extensions = { ViewExtension, ControlExtension };

        /// <summary>Whether <paramref name="path"/> is a view file of either kind.</summary>
        public static bool IsViewFile(string? path) => IsView(path) || IsControl(path);

        /// <summary>Whether <paramref name="path"/> is a form, window or dialog view (<c>.kbview</c>).</summary>
        public static bool IsView(string? path) => HasExtension(path, ViewExtension);

        /// <summary>Whether <paramref name="path"/> is a user control view (<c>.kbcontrol</c>).</summary>
        public static bool IsControl(string? path) => HasExtension(path, ControlExtension);

        /// <summary>
        /// The view of a code-behind file: the same-stem <c>.kbview</c> or <c>.kbcontrol</c> next to it, when one exists
        /// (the <c>.kbview</c> first); null otherwise.
        /// </summary>
        public static string? ViewOf(string? rsPath, Func<string, bool> fileExists)
        {
            if (string.IsNullOrEmpty(rsPath) || !rsPath!.EndsWith(".rs", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            foreach (var extension in Extensions)
            {
                var view = Path.ChangeExtension(rsPath, extension);
                if (fileExists(view))
                {
                    return view;
                }
            }

            return null;
        }

        private static bool HasExtension(string? path, string extension) =>
            !string.IsNullOrEmpty(path) && path!.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
    }
}

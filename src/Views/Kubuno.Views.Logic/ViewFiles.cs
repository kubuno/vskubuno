using System;
using System.IO;

namespace Kubuno.Views.Logic
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
        /// The code-behind languages, in the order a view's code-behind is looked for: Rust (desktop), TypeScript (web,
        /// docs/WEB-VIEWS.md section 2.2).
        /// </summary>
        public static readonly string[] CodeBehindExtensions = { ".rs", ".ts", ".tsx" };

        /// <summary>
        /// The view of a code-behind file: the same-stem <c>.kbview</c> or <c>.kbcontrol</c> next to it, when one exists
        /// (the <c>.kbview</c> first); for a web view's <c>X.kbcontrol.ts</c>, the <c>X.kbcontrol</c> itself. Null otherwise
        /// (a <c>.d.ts</c> declaration file is never a code-behind).
        /// </summary>
        public static string? ViewOf(string? codePath, Func<string, bool> fileExists)
        {
            if (string.IsNullOrEmpty(codePath) || codePath!.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase) ||
                !Array.Exists(CodeBehindExtensions, e => codePath.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            // `Menu.kbcontrol.ts`: the view is the file name without its last extension.
            var inner = codePath.Substring(0, codePath.Length - Path.GetExtension(codePath).Length);
            if (IsViewFile(inner) && fileExists(inner))
            {
                return inner;
            }

            foreach (var extension in Extensions)
            {
                var view = Path.ChangeExtension(codePath, extension);
                if (fileExists(view))
                {
                    return view;
                }
            }

            return null;
        }

        /// <summary>
        /// The code-behind of a view, when one exists: the same-stem <c>.rs</c> (desktop), else <c>.ts</c> / <c>.tsx</c> (web),
        /// else the view's name followed by <c>.ts</c> / <c>.tsx</c> (<c>Menu.kbcontrol.ts</c>); null otherwise.
        /// </summary>
        public static string? CodeBehindOf(string? viewPath, Func<string, bool> fileExists)
        {
            if (string.IsNullOrEmpty(viewPath))
            {
                return null;
            }

            foreach (var extension in CodeBehindExtensions)
            {
                var code = Path.ChangeExtension(viewPath, extension);
                if (fileExists(code))
                {
                    return code;
                }
            }

            foreach (var extension in new[] { ".ts", ".tsx" })
            {
                if (fileExists(viewPath + extension))
                {
                    return viewPath + extension;
                }
            }

            return null;
        }

        private static bool HasExtension(string? path, string extension) =>
            !string.IsNullOrEmpty(path) && path!.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
    }
}

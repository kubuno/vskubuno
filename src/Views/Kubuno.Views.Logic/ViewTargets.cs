using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kubuno.Views.Logic
{
    /// <summary>The target a view belongs to (docs/VIEWS-SPEC.md section 1): its code-behind is Rust or TypeScript.</summary>
    public enum ViewTarget
    {
        /// <summary>A desktop view: Rust code-behind, the compiled-in registry of kubuno-desktop-views.</summary>
        Desktop,

        /// <summary>A web view: TypeScript code-behind, the JSON registry of @kubuno/ui and the project's controls.</summary>
        Web,
    }

    /// <summary>
    /// The profile detection of kubuno-views-ls (docs/WEB-VIEWS.md section 5, WV-7), the same rule on the Visual Studio
    /// side so the editor parts of each target (the Rust and the TypeScript cross-language completions, F12 into Rust)
    /// stay out of the other target's views. Walking up from the view's folder, the first project marker decides:
    /// <c>kubuno.views.json</c> (its <c>target</c>; without one it is the web compiler's configuration, so web), a
    /// <c>package.json</c> depending on <c>@kubuno/views-compiler</c> or a JavaScript project (<c>*.esproj</c>) = web, a
    /// <c>Cargo.toml</c> naming <c>kubuno-desktop</c> = desktop. Nothing found = desktop (the existing behaviour).
    /// </summary>
    public static class ViewTargets
    {
        /// <summary>The configuration file of a web project's views (and the explicit target of any view).</summary>
        public const string ConfigFileName = "kubuno.views.json";

        /// <summary>
        /// The target of the view at <paramref name="viewPath"/> and, for the web, its project root (the folder of the
        /// marker); <paramref name="filesIn"/> lists the file names of a folder.
        /// </summary>
        public static (ViewTarget Target, string? Root) Detect(string? viewPath, Func<string, bool> fileExists, Func<string, string?> readText, Func<string, IEnumerable<string>> filesIn)
        {
            if (string.IsNullOrEmpty(viewPath))
            {
                return (ViewTarget.Desktop, null);
            }

            var dir = Path.GetDirectoryName(viewPath);
            while (!string.IsNullOrEmpty(dir))
            {
                var marker = Marker(dir!, fileExists, readText, filesIn);
                if (marker is { } target)
                {
                    return (target, target == ViewTarget.Web ? dir : null);
                }

                var parent = Path.GetDirectoryName(dir);
                if (string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                dir = parent;
            }

            return (ViewTarget.Desktop, null);
        }

        private static ViewTarget? Marker(string dir, Func<string, bool> fileExists, Func<string, string?> readText, Func<string, IEnumerable<string>> filesIn)
        {
            var config = Path.Combine(dir, ConfigFileName);
            if (fileExists(config))
            {
                var text = readText(config) ?? string.Empty;
                return DeclaresDesktop(text) ? ViewTarget.Desktop : ViewTarget.Web;
            }

            var package = Path.Combine(dir, "package.json");
            if (fileExists(package) && (readText(package) ?? string.Empty).Contains("\"@kubuno/views-compiler\""))
            {
                return ViewTarget.Web;
            }

            if (filesIn(dir).Any(f => f.EndsWith(".esproj", StringComparison.OrdinalIgnoreCase)))
            {
                return ViewTarget.Web;
            }

            var cargo = Path.Combine(dir, "Cargo.toml");
            if (fileExists(cargo))
            {
                var text = readText(cargo) ?? string.Empty;
                if (text.Contains("kubuno-desktop") || text.Contains("kubuno_desktop"))
                {
                    return ViewTarget.Desktop;
                }
            }

            return null;
        }

        /// <summary>Whether a <c>kubuno.views.json</c> text says <c>"target": "desktop"</c> (whitespace ignored).</summary>
        private static bool DeclaresDesktop(string json)
        {
            var compact = new string(json.Where(c => !char.IsWhiteSpace(c)).ToArray());
            return compact.Contains("\"target\":\"desktop\"");
        }
    }
}

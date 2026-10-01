using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kubuno.Desktop.Logic.DesignSurface
{
    /// <summary>
    /// When the project's controls changed since its last design build (docs/EVENTS.md, "User controls"): the
    /// designer then says that the views using them show their previous version until the project is built again -
    /// the Windows Forms designer's "rebuild to see the changes of a UserControl". The folders to watch (the project
    /// and its path dependencies, a library of controls included) and which saved files count (a Rust file declaring
    /// a control, a user control's view).
    /// </summary>
    public static class DesignSourceWatch
    {
        private static readonly Regex DeclaresControl = new Regex(@"#\s*\[\s*derive\s*\([^)]*\b(UserControl|Component|PropertyValue|EventArgs)\b", RegexOptions.Compiled);
        private static readonly Regex UserControlRoot = new Regex(@"^\s*(<\?xml[^>]*\?>\s*)?(<!--.*?-->\s*)*<\s*UserControl\b", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex PathDependency = new Regex(@"(?m)^\s*(?<name>[A-Za-z0-9_-]+)\s*=\s*\{[^}\n]*\bpath\s*=\s*""(?<path>[^""]+)""", RegexOptions.Compiled);

        /// <summary>
        /// The folders whose sources the project's design build compiles: the package's own, and those of its path
        /// dependencies that are not Kubuno's own crates (a control library of the solution).
        /// </summary>
        public static IReadOnlyList<string> WatchedDirectories(string manifestPath, string? manifestText)
        {
            var root = Path.GetDirectoryName(manifestPath) ?? string.Empty;
            var result = new List<string> { root };
            foreach (Match m in PathDependency.Matches(manifestText ?? string.Empty))
            {
                var name = m.Groups["name"].Value;
                if (name == "kubuno" || name.StartsWith("kubuno-", StringComparison.Ordinal) || name.StartsWith("kubuno_", StringComparison.Ordinal))
                {
                    continue;
                }

                var dir = Path.GetFullPath(Path.Combine(root, m.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar)));
                if (!result.Any(d => string.Equals(d, dir, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(dir);
                }
            }

            return result;
        }

        /// <summary>Whether a path is a build output or tool folder's (never a source).</summary>
        public static bool IsIgnored(string path)
        {
            var p = path.Replace('/', '\\');
            return p.IndexOf("\\target\\", StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf("\\.vs\\", StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf("\\.git\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Whether saving <paramref name="path"/> (with <paramref name="text"/>) changes what the design surface renders
        /// once rebuilt: a Rust file declaring a control, a property enum or an args type, or a user control's view.
        /// A form's own view or code-behind does not (the designer reads the view live).
        /// </summary>
        public static bool AffectsDesign(string path, string? text)
        {
            if (string.IsNullOrEmpty(path) || IsIgnored(path) || text is null)
            {
                return false;
            }

            if (path.EndsWith(".rs", StringComparison.OrdinalIgnoreCase))
            {
                return DeclaresControl.IsMatch(text);
            }

            return path.EndsWith(".kbview", StringComparison.OrdinalIgnoreCase) && UserControlRoot.IsMatch(text);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Kubuno.Desktop.TemplateWizard
{
    /// <summary>
    /// The pure logic of <see cref="InheritedViewWizard"/> (visual inheritance, docs/EVENTS.md "User controls"): the
    /// views of a project a new inherited form or inherited user control can derive from (Windows Forms' Inheritance
    /// Picker), the derived view's text - its root naming the base (<c>x:Inherits</c>) and an override element for every
    /// control of the base the derived view may change (<c>Modifiers</c> Protected or Public), nested like in the base -
    /// and the Rust type of the base view.
    /// </summary>
    public static class InheritedViewNames
    {
        private static readonly string[] Overridable = { "Protected", "Public", "Internal", "ProtectedInternal" };

        /// <summary>A view a new view may inherit from.</summary>
        public sealed class BaseView
        {
            public BaseView(string path, string rootName, bool isUserControl)
            {
                Path = path;
                RootName = rootName;
                IsUserControl = isUserControl;
            }

            /// <summary>The <c>.kbview</c> file (absolute).</summary>
            public string Path { get; }

            /// <summary>Its root element (<c>Panel</c>, <c>UserControl</c>…).</summary>
            public string RootName { get; }

            public bool IsUserControl { get; }
        }

        /// <summary>Reads a view leniently (<c>x:</c> prefixes without a namespace declaration); null when it is not XML.</summary>
        public static XmlDocument? Load(string text)
        {
            try
            {
                var doc = new XmlDocument();
                using var reader = new XmlTextReader(new StringReader(text ?? string.Empty)) { Namespaces = false, DtdProcessing = DtdProcessing.Prohibit };
                doc.Load(reader);
                return doc;
            }
            catch (XmlException)
            {
                return null;
            }
        }

        /// <summary>The views of <paramref name="files"/> (path, text) a form (<paramref name="userControl"/> false) or a user control can inherit from.</summary>
        public static IReadOnlyList<BaseView> Candidates(IEnumerable<(string Path, string Text)> files, bool userControl)
        {
            var result = new List<BaseView>();
            foreach (var (path, text) in files)
            {
                if (Load(text)?.DocumentElement is not { } root)
                {
                    continue;
                }

                var isUserControl = root.Name == "UserControl";
                if (isUserControl == userControl)
                {
                    result.Add(new BaseView(path, root.Name, isUserControl));
                }
            }

            return result.OrderBy(b => b.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The <c>.kbview</c> files under <paramref name="projectDirectory"/>, build output skipped.</summary>
        public static IEnumerable<string> ViewFiles(string projectDirectory)
        {
            if (!Directory.Exists(projectDirectory))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(projectDirectory, "*.kbview", SearchOption.AllDirectories)
                .Where(f => f.IndexOf("\\target\\", StringComparison.OrdinalIgnoreCase) < 0 && f.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) < 0);
        }

        /// <summary>The path of <paramref name="baseView"/> as the view in <paramref name="derivedDirectory"/> names it (<c>/</c> separators).</summary>
        public static string RelativePath(string derivedDirectory, string baseView)
        {
            var from = new Uri(derivedDirectory.TrimEnd('\\') + "\\");
            return Uri.UnescapeDataString(from.MakeRelativeUri(new Uri(baseView)).ToString()).Replace('\\', '/');
        }

        /// <summary>
        /// The override elements of the derived view (indented by <paramref name="indent"/>): one per control of the base whose
        /// <c>Modifiers</c> lets a derived view change it, inside the elements standing for its named containers; empty when
        /// none can be changed.
        /// </summary>
        public static string Overrides(string baseText, string indent = "  ")
        {
            if (Load(baseText)?.DocumentElement is not { } root)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (var child in root.ChildNodes.OfType<XmlElement>())
            {
                Write(child, indent, sb);
            }

            return sb.ToString();
        }

        private static bool CanOverride(XmlElement e) => Overridable.Contains(e.GetAttribute("Modifiers"));

        private static bool HasOverridable(XmlElement e) => e.ChildNodes.OfType<XmlElement>().Any(c => (CanOverride(c) && c.HasAttribute("x:Name")) || HasOverridable(c));

        private static void Write(XmlElement e, string indent, StringBuilder sb)
        {
            var name = e.GetAttribute("x:Name");
            if (name.Length == 0)
            {
                return; // An unnamed container cannot be overridden: what it holds stays the base's.
            }

            var inner = HasOverridable(e);
            if (!CanOverride(e) && !inner)
            {
                return;
            }

            sb.Append(indent).Append('<').Append(e.Name).Append(" x:Name=\"").Append(name).Append('"');
            if (!inner)
            {
                sb.Append("/>\n");
                return;
            }

            sb.Append(">\n");
            foreach (var child in e.ChildNodes.OfType<XmlElement>())
            {
                Write(child, indent + "  ", sb);
            }

            sb.Append(indent).Append("</").Append(e.Name).Append(">\n");
        }

        /// <summary>
        /// The Rust type of a base view from its code-behind (the same-stem <c>.rs</c>): the struct under
        /// <c>#[kubuno::view(…)]</c> (a form) or <c>#[derive(UserControl…)]</c>; null when there is none.
        /// </summary>
        public static string? BaseTypeName(string rsText, bool userControl)
        {
            var pattern = userControl
                ? @"#\s*\[\s*derive\s*\([^)]*\bUserControl\b[^)]*\)\s*\][\s\S]*?\bstruct\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)"
                : @"#\s*\[\s*kubuno\s*::\s*view\s*\([^\]]*\)\s*\][\s\S]*?\bstruct\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)";
            var match = Regex.Match(rsText ?? string.Empty, pattern);
            return match.Success ? match.Groups["name"].Value : null;
        }

        /// <summary>The path of the base type from the crate root (<c>crate::address_editor::AddressEditor</c>), its module named after its file.</summary>
        public static string BaseTypePath(string baseView, string typeName) =>
            "crate::" + ControlItemNames.ModuleName(Path.GetFileNameWithoutExtension(baseView)) + "::" + typeName;
    }
}

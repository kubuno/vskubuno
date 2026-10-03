using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Kubuno.Rust.Logic.ProjectGeneration
{
    /// <summary>
    /// Builds/updates the XML solution file (<c>.slnx</c>, Visual Studio 2026's default format) that lists every generated
    /// <c>.rsproj</c> - the <c>.slnx</c> counterpart of <see cref="RsprojSolutionGenerator"/>, with the same discipline: an
    /// existing file is merged into (only the missing <c>Project</c> elements are added, everything else - folders, other
    /// projects, comments, formatting - is kept as it is). Projects with an executable sit at the solution root, where
    /// "Set as Startup Project" is one click away; library-only members go to a <c>/Libraries/</c> solution folder.
    /// </summary>
    public static class RsprojSlnxGenerator
    {
        /// <summary>The solution folder library-only members are listed in.</summary>
        public const string LibrariesFolderName = "/Libraries/";

        /// <summary>The project type GUID as Visual Studio itself writes it in a <c>.slnx</c> (lower case, no braces).</summary>
        private static readonly string TypeAttributeValue = RsprojSolutionGenerator.RsprojTypeGuid.ToLowerInvariant();

        /// <param name="solutionDirectory">Directory the <c>.slnx</c> lives (or will be written) in - project paths are stored relative to it.</param>
        /// <param name="existingContent"><see langword="null"/> when no <c>.slnx</c> exists yet at the target path.</param>
        /// <param name="members">Every workspace member's plan item (created and already existing ones alike).</param>
        public static RsprojSolutionPlan Plan(string solutionDirectory, string? existingContent, IReadOnlyList<RsprojProjectPlanItem> members)
        {
            if (solutionDirectory is null)
            {
                throw new ArgumentNullException(nameof(solutionDirectory));
            }

            if (members is null)
            {
                throw new ArgumentNullException(nameof(members));
            }

            var entries = members
                .Select(member => (Name: member.PackageName, Path: MakeRelativePath(solutionDirectory, member.ProjectPath), member.IsLibraryOnly))
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (existingContent is null)
            {
                return new RsprojSolutionPlan(BuildFresh(entries), changed: true, entries.Select(entry => entry.Name).ToList());
            }

            return MergeIntoExisting(existingContent, entries);
        }

        /// <summary>
        /// The solution of a repository whose projects go to named solution folders (the desktop repository: Applications,
        /// Framework, ... - <see cref="DesktopRepositoryLayout"/>): a fresh <c>.slnx</c> lists the folders in
        /// <paramref name="folderOrder"/> (each folder's projects by name); an existing one only gets its missing projects,
        /// each in its folder (created at the end when absent). <paramref name="folderOf"/> returns a folder name such as
        /// <c>/Framework/</c>, or null for the solution root.
        /// </summary>
        public static RsprojSolutionPlan PlanWithFolders(
            string solutionDirectory,
            string? existingContent,
            IReadOnlyList<RsprojProjectPlanItem> members,
            Func<RsprojProjectPlanItem, string?> folderOf,
            IReadOnlyList<string> folderOrder)
        {
            if (solutionDirectory is null)
            {
                throw new ArgumentNullException(nameof(solutionDirectory));
            }

            if (members is null)
            {
                throw new ArgumentNullException(nameof(members));
            }

            var entries = members
                .Select(member => (Name: member.ProjectName, Path: MakeRelativePath(solutionDirectory, member.ProjectPath), Folder: folderOf(member)))
                .OrderBy(entry => entry.Folder is null ? -1 : IndexOf(folderOrder, entry.Folder))
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (existingContent is null)
            {
                var builder = new StringBuilder();
                builder.Append("<Solution>\n");
                builder.Append("  <Configurations>\n");
                builder.Append("    <Platform Name=\"x64\" />\n");
                builder.Append("  </Configurations>\n");
                foreach (var entry in entries.Where(entry => entry.Folder is null))
                {
                    builder.Append("  ").Append(ProjectElementText(entry.Path)).Append('\n');
                }

                foreach (var group in entries.Where(entry => entry.Folder is not null).GroupBy(entry => entry.Folder!))
                {
                    builder.Append("  <Folder Name=\"").Append(System.Security.SecurityElement.Escape(group.Key)).Append("\">\n");
                    foreach (var entry in group)
                    {
                        builder.Append("    ").Append(ProjectElementText(entry.Path)).Append('\n');
                    }

                    builder.Append("  </Folder>\n");
                }

                builder.Append("</Solution>\n");
                return new RsprojSolutionPlan(builder.ToString(), changed: true, entries.Select(entry => entry.Name).ToList());
            }

            XDocument document;
            try
            {
                document = XDocument.Parse(existingContent, LoadOptions.PreserveWhitespace);
            }
            catch (XmlException)
            {
                return new RsprojSolutionPlan(existingContent, changed: false, Array.Empty<string>());
            }

            var root = document.Root;
            if (root is null || root.Name.LocalName != "Solution")
            {
                return new RsprojSolutionPlan(existingContent, changed: false, Array.Empty<string>());
            }

            var existingPaths = new HashSet<string>(
                root.Descendants("Project").Select(project => Normalize((string?)project.Attribute("Path") ?? string.Empty)),
                StringComparer.OrdinalIgnoreCase);
            var missing = entries.Where(entry => !existingPaths.Contains(Normalize(entry.Path))).ToList();
            if (missing.Count == 0)
            {
                return new RsprojSolutionPlan(existingContent, changed: false, Array.Empty<string>());
            }

            var newline = existingContent.Contains("\r\n") ? "\r\n" : "\n";
            EnsureX64Platform(root, newline, hasOtherProjectTypes: root.Descendants("Project").Any(project => !((string?)project.Attribute("Path") ?? string.Empty).EndsWith(".rsproj", StringComparison.OrdinalIgnoreCase)));
            foreach (var entry in missing)
            {
                if (entry.Folder is null)
                {
                    var anchor = root.Elements("Project").LastOrDefault() ?? (XElement?)root.Element("Configurations");
                    AddChild(root, anchor, NewProjectElement(entry.Path), newline, "  ");
                    continue;
                }

                var folder = root.Elements("Folder").FirstOrDefault(element => string.Equals((string?)element.Attribute("Name"), entry.Folder, StringComparison.OrdinalIgnoreCase));
                if (folder is null)
                {
                    folder = new XElement("Folder", new XAttribute("Name", entry.Folder));
                    AddChild(root, root.Elements().LastOrDefault(), folder, newline, "  ");
                    folder.Add(new XText(newline + "  "));
                }

                AddChild(folder, folder.Elements("Project").LastOrDefault(), NewProjectElement(entry.Path), newline, "    ");
            }

            if (root.LastNode is XElement)
            {
                root.Add(new XText(newline));
            }

            var merged = document.Declaration is null
                ? root.ToString(SaveOptions.DisableFormatting)
                : document.Declaration + newline + root.ToString(SaveOptions.DisableFormatting);
            var trailing = existingContent.EndsWith("\n", StringComparison.Ordinal) ? newline : string.Empty;
            return new RsprojSolutionPlan(merged + trailing, changed: true, missing.Select(entry => entry.Name).ToList());
        }

        private static int IndexOf(IReadOnlyList<string> order, string folder)
        {
            for (var index = 0; index < order.Count; index++)
            {
                if (string.Equals(order[index], folder, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return order.Count;
        }

        private static string BuildFresh(List<(string Name, string Path, bool IsLibraryOnly)> entries)
        {
            var builder = new StringBuilder();
            builder.Append("<Solution>\n");
            builder.Append("  <Configurations>\n");
            builder.Append("    <Platform Name=\"x64\" />\n");
            builder.Append("  </Configurations>\n");
            foreach (var entry in entries.Where(entry => !entry.IsLibraryOnly))
            {
                builder.Append("  ").Append(ProjectElementText(entry.Path)).Append('\n');
            }

            var libraries = entries.Where(entry => entry.IsLibraryOnly).ToList();
            if (libraries.Count > 0)
            {
                builder.Append("  <Folder Name=\"").Append(LibrariesFolderName).Append("\">\n");
                foreach (var entry in libraries)
                {
                    builder.Append("    ").Append(ProjectElementText(entry.Path)).Append('\n');
                }

                builder.Append("  </Folder>\n");
            }

            builder.Append("</Solution>\n");
            return builder.ToString();
        }

        private static RsprojSolutionPlan MergeIntoExisting(string existingContent, List<(string Name, string Path, bool IsLibraryOnly)> entries)
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(existingContent, LoadOptions.PreserveWhitespace);
            }
            catch (XmlException)
            {
                // Not XML we can read: leave the developer's file alone rather than guess.
                return new RsprojSolutionPlan(existingContent, changed: false, Array.Empty<string>());
            }

            var root = document.Root;
            if (root is null || root.Name.LocalName != "Solution")
            {
                return new RsprojSolutionPlan(existingContent, changed: false, Array.Empty<string>());
            }

            var existingPaths = new HashSet<string>(
                root.Descendants("Project").Select(project => Normalize((string?)project.Attribute("Path") ?? string.Empty)),
                StringComparer.OrdinalIgnoreCase);

            var missing = entries.Where(entry => !existingPaths.Contains(Normalize(entry.Path))).ToList();
            if (missing.Count == 0)
            {
                return new RsprojSolutionPlan(existingContent, changed: false, Array.Empty<string>());
            }

            var newline = existingContent.Contains("\r\n") ? "\r\n" : "\n";
            var hasOtherProjectTypes = root.Descendants("Project")
                .Any(project => !((string?)project.Attribute("Path") ?? string.Empty).EndsWith(".rsproj", StringComparison.OrdinalIgnoreCase));
            EnsureX64Platform(root, newline, hasOtherProjectTypes);

            foreach (var entry in missing.Where(entry => !entry.IsLibraryOnly))
            {
                var anchor = root.Elements("Project").LastOrDefault() ?? (XElement?)root.Element("Configurations");
                AddChild(root, anchor, NewProjectElement(entry.Path), newline, "  ");
            }

            var libraries = missing.Where(entry => entry.IsLibraryOnly).ToList();
            if (libraries.Count > 0)
            {
                var folder = root.Elements("Folder").FirstOrDefault(element => string.Equals((string?)element.Attribute("Name"), LibrariesFolderName, StringComparison.OrdinalIgnoreCase));
                if (folder is null)
                {
                    folder = new XElement("Folder", new XAttribute("Name", LibrariesFolderName));
                    AddChild(root, root.Elements().LastOrDefault(), folder, newline, "  ");
                    folder.Add(new XText(newline + "  "));
                }

                foreach (var entry in libraries)
                {
                    AddChild(folder, folder.Elements("Project").LastOrDefault(), NewProjectElement(entry.Path), newline, "    ");
                }
            }

            if (root.LastNode is XElement)
            {
                // The closing tag of a solution that was empty goes on a line of its own.
                root.Add(new XText(newline));
            }

            var merged = document.Declaration is null
                ? root.ToString(SaveOptions.DisableFormatting)
                : document.Declaration + newline + root.ToString(SaveOptions.DisableFormatting);
            var trailing = existingContent.EndsWith("\n", StringComparison.Ordinal) ? newline : string.Empty;
            return new RsprojSolutionPlan(merged + trailing, changed: true, missing.Select(entry => entry.Name).ToList());
        }

        /// <summary>A <c>.rsproj</c> is x64 only: make sure the solution offers that platform.</summary>
        private static void EnsureX64Platform(XElement root, string newline, bool hasOtherProjectTypes)
        {
            var configurations = root.Element("Configurations");
            if (configurations is null)
            {
                configurations = new XElement("Configurations");
                if (hasOtherProjectTypes)
                {
                    // An existing solution without a Configurations element uses Any CPU: keep it for its other projects.
                    configurations.Add(new XText(newline + "    "), new XElement("Platform", new XAttribute("Name", "Any CPU")));
                }

                configurations.Add(new XText(newline + "    "), new XElement("Platform", new XAttribute("Name", "x64")), new XText(newline + "  "));
                root.AddFirst(new XText(newline + "  "), configurations);
                return;
            }

            if (configurations.Elements("Platform").Any(platform => string.Equals((string?)platform.Attribute("Name"), "x64", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            AddChild(configurations, configurations.Elements("Platform").LastOrDefault(), new XElement("Platform", new XAttribute("Name", "x64")), newline, "    ");
        }

        /// <summary>Inserts <paramref name="element"/> after <paramref name="anchor"/> (or as the first child), on its own indented line.</summary>
        private static void AddChild(XElement parent, XElement? anchor, XElement element, string newline, string indent)
        {
            if (anchor is not null)
            {
                anchor.AddAfterSelf(new XText(newline + indent), element);
                return;
            }

            if (parent.FirstNode is null)
            {
                // An empty <Solution/> or <Folder/>: the closing tag goes on a line of its own.
                parent.Add(new XText(newline + indent), element, new XText(newline + indent.Substring(0, Math.Max(0, indent.Length - 2))));
                return;
            }

            parent.AddFirst(new XText(newline + indent), element);
        }

        private static XElement NewProjectElement(string path) =>
            new("Project", new XAttribute("Path", path), new XAttribute("Type", TypeAttributeValue));

        private static string ProjectElementText(string path) =>
            $"<Project Path=\"{System.Security.SecurityElement.Escape(path)}\" Type=\"{TypeAttributeValue}\" />";

        /// <summary>Relative path with forward slashes, as Visual Studio writes it in a <c>.slnx</c>.</summary>
        private static string MakeRelativePath(string fromDirectory, string toFile)
        {
            var from = fromDirectory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? fromDirectory : fromDirectory + Path.DirectorySeparatorChar;
            var relative = new Uri(from).MakeRelativeUri(new Uri(toFile));
            return Uri.UnescapeDataString(relative.ToString()).Replace('\\', '/');
        }

        private static string Normalize(string path)
        {
            var normalized = path.Replace('\\', '/');
            return (normalized.StartsWith("./", StringComparison.Ordinal) ? normalized.Substring(2) : normalized).ToLowerInvariant();
        }
    }
}

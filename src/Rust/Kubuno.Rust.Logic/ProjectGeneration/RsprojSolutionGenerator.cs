using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Kubuno.VisualStudio.Core.ProjectGeneration
{
    /// <summary>Result of <see cref="RsprojSolutionGenerator.Plan"/>.</summary>
    public sealed class RsprojSolutionPlan
    {
        /// <summary>The full <c>.sln</c> text to write (identical to the input when nothing changed).</summary>
        public string Content { get; }

        /// <summary><see langword="true"/> when <see cref="Content"/> differs from what is already on disk (or there was nothing on disk yet).</summary>
        public bool Changed { get; }

        /// <summary>Package names newly added as a solution entry by this run.</summary>
        public IReadOnlyList<string> AddedProjectNames { get; }

        public RsprojSolutionPlan(string content, bool changed, IReadOnlyList<string> addedProjectNames)
        {
            Content = content;
            Changed = changed;
            AddedProjectNames = addedProjectNames;
        }
    }

    /// <summary>
    /// Builds/updates the <c>.sln</c> that lists every generated <c>.rsproj</c> (docs/RSPROJ.md
    /// work package 5). An existing solution is edited surgically - only the lines needed for the
    /// missing project entries are inserted, everything else (solution folders, other projects,
    /// existing formatting) is preserved byte-for-byte, the same "never regenerate what the
    /// developer owns" discipline <c>CLAUDE.md</c> already applies to Kubuno view XML. A brand new
    /// solution is written in the same shape as <c>samples/hello-rust.sln</c>.
    /// </summary>
    public static class RsprojSolutionGenerator
    {
        /// <summary>Project-type GUID for <c>.rsproj</c> - <c>Kubuno.Rust.Sdk/Sdk.props</c>'s <c>DefaultProjectTypeGuid</c>.</summary>
        public const string RsprojTypeGuid = "6C7C4CB5-6E36-4C6F-9C6F-9C6E9B4D4C13";

        private static readonly Regex ProjectLineRegex = new(
            "^Project\\(\"\\{[0-9A-Fa-f-]+\\}\"\\)\\s*=\\s*\"[^\"]*\"\\s*,\\s*\"(?<path>[^\"]*)\"\\s*,\\s*\"\\{(?<guid>[0-9A-Fa-f-]+)\\}\"\\s*$",
            RegexOptions.Compiled);

        /// <param name="solutionDirectory">Directory the <c>.sln</c> lives (or will be written) in - project paths are stored relative to it.</param>
        /// <param name="existingContent"><see langword="null"/> when no <c>.sln</c> exists yet at the target path.</param>
        /// <param name="members">Every workspace member's plan item (both newly created and already-existing <c>.rsproj</c> - work package 5: "a .sln ... with all of them").</param>
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
                .Select(member => (
                    Name: member.PackageName,
                    RelativePath: MakeRelativePath(solutionDirectory, member.ProjectPath),
                    Guid: DeterministicGuid.From("rsproj:" + NormalizeForSeed(member.ProjectPath)).ToString("D").ToUpperInvariant()))
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (existingContent is null)
            {
                var fresh = BuildFreshSolution(entries);
                return new RsprojSolutionPlan(fresh, changed: true, entries.Select(entry => entry.Name).ToList());
            }

            return MergeIntoExisting(existingContent, entries);
        }

        private static RsprojSolutionPlan MergeIntoExisting(
            string existingContent,
            List<(string Name, string RelativePath, string Guid)> entries)
        {
            var newline = existingContent.Contains("\r\n") ? "\r\n" : "\n";
            var lines = existingContent.Replace("\r\n", "\n").Split('\n').ToList();
            // Split on '\n' after normalizing drops a trailing empty element only when the file
            // ends with a newline - re-add it at the end so round-tripping an unmodified file is
            // byte-identical.
            var endedWithNewline = existingContent.EndsWith("\n", StringComparison.Ordinal);
            if (endedWithNewline && lines.Count > 0 && lines[lines.Count - 1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                var match = ProjectLineRegex.Match(line);
                if (match.Success)
                {
                    existingPaths.Add(NormalizeForSeed(match.Groups["path"].Value));
                }
            }

            var missing = entries.Where(entry => !existingPaths.Contains(NormalizeForSeed(entry.RelativePath))).ToList();
            if (missing.Count == 0)
            {
                return new RsprojSolutionPlan(existingContent, changed: false, Array.Empty<string>());
            }

            InsertProjectBlocks(lines, missing);
            InsertConfigurationPlatformLines(lines);
            InsertProjectConfigurationLines(lines, missing);

            var merged = string.Join(newline, lines) + (endedWithNewline ? newline : string.Empty);
            return new RsprojSolutionPlan(merged, changed: true, missing.Select(entry => entry.Name).ToList());
        }

        private static void InsertProjectBlocks(List<string> lines, List<(string Name, string RelativePath, string Guid)> missing)
        {
            var globalIndex = lines.FindIndex(line => line.Trim() == "Global");
            var insertAt = globalIndex >= 0 ? globalIndex : lines.Count;

            var block = new List<string>();
            foreach (var entry in missing)
            {
                block.Add($"Project(\"{{{RsprojTypeGuid}}}\") = \"{entry.Name}\", \"{entry.RelativePath}\", \"{{{entry.Guid}}}\"");
                block.Add("EndProject");
            }

            lines.InsertRange(insertAt, block);
        }

        private static void InsertConfigurationPlatformLines(List<string> lines)
        {
            InsertMissingSectionLines(
                lines,
                "GlobalSection(SolutionConfigurationPlatforms) = preSolution",
                new[] { "Debug|x64 = Debug|x64", "Release|x64 = Release|x64" },
                indent: "\t\t");
        }

        private static void InsertProjectConfigurationLines(List<string> lines, List<(string Name, string RelativePath, string Guid)> missing)
        {
            var newLines = new List<string>();
            foreach (var entry in missing)
            {
                newLines.Add($"{{{entry.Guid}}}.Debug|x64.ActiveCfg = Debug|x64");
                newLines.Add($"{{{entry.Guid}}}.Debug|x64.Build.0 = Debug|x64");
                newLines.Add($"{{{entry.Guid}}}.Release|x64.ActiveCfg = Release|x64");
                newLines.Add($"{{{entry.Guid}}}.Release|x64.Build.0 = Release|x64");
            }

            InsertMissingSectionLines(
                lines,
                "GlobalSection(ProjectConfigurationPlatforms) = postSolution",
                newLines,
                indent: "\t\t",
                onlyIfMissing: null); // every new project always needs its own 4 lines, never de-duplicated against existing ones
        }

        /// <summary>
        /// Finds the <c>GlobalSection(...)</c>/<c>EndGlobalSection</c> pair whose header trims to
        /// <paramref name="sectionHeader"/> and inserts any of <paramref name="candidateLines"/>
        /// not already present verbatim (trimmed) in that section, right before its
        /// <c>EndGlobalSection</c>. A no-op when the section itself is not found (best-effort:
        /// see this class's own remarks).
        /// </summary>
        private static void InsertMissingSectionLines(
            List<string> lines,
            string sectionHeader,
            IReadOnlyList<string> candidateLines,
            string indent,
            bool? onlyIfMissing = true)
        {
            var startIndex = lines.FindIndex(line => line.Trim() == sectionHeader);
            if (startIndex < 0)
            {
                return;
            }

            var endIndex = lines.FindIndex(startIndex + 1, line => line.Trim() == "EndGlobalSection");
            if (endIndex < 0)
            {
                return;
            }

            var toInsert = candidateLines.AsEnumerable();
            if (onlyIfMissing == true)
            {
                var existing = new HashSet<string>(lines.Skip(startIndex + 1).Take(endIndex - startIndex - 1).Select(line => line.Trim()));
                toInsert = candidateLines.Where(line => !existing.Contains(line));
            }

            lines.InsertRange(endIndex, toInsert.Select(line => indent + line));
        }

        private static string BuildFreshSolution(List<(string Name, string RelativePath, string Guid)> entries)
        {
            var builder = new StringBuilder();
            builder.Append('\n');
            builder.Append("Microsoft Visual Studio Solution File, Format Version 12.00\n");
            builder.Append("# Visual Studio Version 18\n");
            builder.Append("VisualStudioVersion = 18.0.00000.0\n");
            builder.Append("MinimumVisualStudioVersion = 10.0.40219.1\n");

            foreach (var entry in entries)
            {
                builder.Append($"Project(\"{{{RsprojTypeGuid}}}\") = \"{entry.Name}\", \"{entry.RelativePath}\", \"{{{entry.Guid}}}\"\n");
                builder.Append("EndProject\n");
            }

            builder.Append("Global\n");
            builder.Append("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\n");
            builder.Append("\t\tDebug|x64 = Debug|x64\n");
            builder.Append("\t\tRelease|x64 = Release|x64\n");
            builder.Append("\tEndGlobalSection\n");
            builder.Append("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\n");
            foreach (var entry in entries)
            {
                builder.Append($"\t\t{{{entry.Guid}}}.Debug|x64.ActiveCfg = Debug|x64\n");
                builder.Append($"\t\t{{{entry.Guid}}}.Debug|x64.Build.0 = Debug|x64\n");
                builder.Append($"\t\t{{{entry.Guid}}}.Release|x64.ActiveCfg = Release|x64\n");
                builder.Append($"\t\t{{{entry.Guid}}}.Release|x64.Build.0 = Release|x64\n");
            }

            builder.Append("\tEndGlobalSection\n");
            builder.Append("\tGlobalSection(SolutionProperties) = preSolution\n");
            builder.Append("\t\tHideSolutionNode = FALSE\n");
            builder.Append("\tEndGlobalSection\n");
            builder.Append("EndGlobal\n");
            return builder.ToString();
        }

        private static string MakeRelativePath(string fromDirectory, string toFile)
        {
            var fromUri = new Uri(AppendDirectorySeparator(fromDirectory));
            var toUri = new Uri(toFile);
            var relativeUri = fromUri.MakeRelativeUri(toUri);
            return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', '\\');
        }

        private static string AppendDirectorySeparator(string path) =>
            path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? path : path + Path.DirectorySeparatorChar;

        private static string NormalizeForSeed(string path) => path.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
    }
}

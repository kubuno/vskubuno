using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kubuno.Views.Designer.PropertyBrowser;
using Kubuno.Views.Designer.Registry;
using Kubuno.Views.Designer.Selection;

namespace Kubuno.Views.Designer.Ribbon
{
    /// <summary>
    /// A tab's <c>&lt;RibbonTab.ScalingPolicy&gt;</c> (docs/RIBBON.md section 5): the ordered <c>&lt;Scale Group=".." Size=".."/&gt;</c>
    /// steps the ribbon applies, one after the other, while the tab does not fit. What the Properties window's
    /// ScalingPolicy editor reads and writes (one undo unit). Pure, unit-tested.
    /// </summary>
    public static class RibbonScalingPolicy
    {
        /// <summary>The property element holding the steps.</summary>
        public const string PropertyElement = "RibbonTab.ScalingPolicy";

        /// <summary>The element of one step.</summary>
        public const string StepTag = "Scale";

        /// <summary>The sizes a step can bring a group to, largest first.</summary>
        public static readonly IReadOnlyList<string> Sizes = new[] { "Medium", "Small", "Collapsed" };

        /// <summary>The steps of tab <paramref name="tab"/>, in order.</summary>
        public static IReadOnlyList<ViewNode> Steps(ViewNode tab) =>
            tab.Children.FirstOrDefault(c => c.Name == PropertyElement)?.Children.Where(c => c.Name == StepTag).ToList() ?? new List<ViewNode>();

        /// <summary>The names a step can refer to: the tab's groups' <c>x:Name</c>s, in order.</summary>
        public static IReadOnlyList<string> GroupNames(ViewNode tab) =>
            tab.Children.Where(c => c.Name == "RibbonGroup").Select(c => c.Attribute("x:Name")).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList();

        /// <summary>
        /// The registry entry the collection editor edits a step with: <c>Group</c> (one of <paramref name="groups"/>) and
        /// <c>Size</c>.
        /// </summary>
        public static ComponentMeta StepMeta(IReadOnlyList<string> groups) => new ComponentMeta
        {
            Name = StepTag,
            Family = RibbonDesignerTasks.Family,
            Properties = new List<PropertyMeta>
            {
                new PropertyMeta
                {
                    Name = "Group",
                    Kind = groups.Count > 0 ? PropKind.CreateEnum(groups) : PropKind.String,
                    Doc = "The group this step shrinks (its x:Name).",
                    DocFr = "Le groupe que cette étape réduit (son x:Name).",
                },
                new PropertyMeta
                {
                    Name = "Size",
                    Kind = PropKind.CreateEnum(Sizes),
                    Default = "Collapsed",
                    Doc = "The size the group takes at this step; a step never makes a group larger.",
                    DocFr = "La taille que prend le groupe à cette étape ; une étape n'agrandit jamais un groupe.",
                },
            },
        };

        /// <summary>The steps an edited collection stands for: an existing step keeps its attributes but the changed ones.</summary>
        public static IReadOnlyList<(string Group, string Size)> Merge(IReadOnlyList<ViewNode> originals, IReadOnlyList<CollectionMember> members)
        {
            var steps = new List<(string, string)>();
            foreach (var member in members)
            {
                var values = new Dictionary<string, string?>(StringComparer.Ordinal);
                if (member.OriginalIndex is { } i && i >= 0 && i < originals.Count)
                {
                    foreach (var a in originals[i].Attributes)
                    {
                        values[a.Name] = a.Value;
                    }
                }

                foreach (var change in member.Attributes)
                {
                    values[change.Key] = change.Value;
                }

                var group = values.TryGetValue("Group", out var g) ? g : null;
                if (!string.IsNullOrEmpty(group))
                {
                    steps.Add((group!, values.TryGetValue("Size", out var s) && !string.IsNullOrEmpty(s) ? s! : "Collapsed"));
                }
            }

            return steps;
        }

        /// <summary>
        /// The problems of <paramref name="steps"/> (what the language server reports too): a step naming no group of the
        /// tab, and a step that makes a group larger again.
        /// </summary>
        public static IReadOnlyList<string> Diagnostics(IReadOnlyList<(string Group, string Size)> steps, IReadOnlyList<string> groups)
        {
            var problems = new List<string>();
            var reached = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (group, size) in steps)
            {
                if (!groups.Contains(group))
                {
                    problems.Add(T($"No group of this tab is named '{group}'.", $"Aucun groupe de cet onglet ne s'appelle « {group} »."));
                    continue;
                }

                var rank = Sizes.ToList().IndexOf(size);
                if (reached.TryGetValue(group, out var before) && rank < before)
                {
                    problems.Add(T($"'{group}' grows back to {size}: a step never makes a group larger.", $"« {group} » redevient {size} : une étape n'agrandit jamais un groupe."));
                }

                reached[group] = Math.Max(rank, before);
            }

            return problems;
        }

        /// <summary>
        /// The edits that give tab <paramref name="tabId"/> the policy <paramref name="steps"/>: the property element rewritten
        /// in place, added as the tab's first child, or removed when there is no step left. Empty when nothing changes.
        /// </summary>
        public static IReadOnlyList<TextReplacement> Plan(string text, string tabId, IReadOnlyList<(string Group, string Size)> steps)
        {
            var tab = ViewDocument.Find(ViewDocument.Parse(text), tabId);
            if (tab is null)
            {
                return Array.Empty<TextReplacement>();
            }

            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var block = tab.Children.FirstOrDefault(c => c.Name == PropertyElement);
            var tabIndent = LineIndent(text, tab.Start);
            var indent = tab.Children.Count > 0 ? LineIndent(text, tab.Children[0].Start) : tabIndent + "  ";
            var sb = new StringBuilder("<").Append(PropertyElement).Append('>');
            foreach (var (group, size) in steps)
            {
                sb.Append(newline).Append(indent).Append("  <").Append(StepTag)
                  .Append(" Group=\"").Append(ChildCollectionPlanner.Escape(group)).Append("\" Size=\"").Append(ChildCollectionPlanner.Escape(size)).Append("\"/>");
            }

            var written = sb.Append(newline).Append(indent).Append("</").Append(PropertyElement).Append('>').ToString();
            if (block is not null)
            {
                if (steps.Count == 0)
                {
                    var start = block.Start;
                    while (start > 0 && (text[start - 1] == ' ' || text[start - 1] == '\t'))
                    {
                        start--;
                    }

                    if (start > 0 && text[start - 1] == '\n')
                    {
                        start--;
                        if (start > 0 && text[start - 1] == '\r')
                        {
                            start--;
                        }
                    }

                    return new[] { new TextReplacement(start, block.End - start, string.Empty) };
                }

                return string.Equals(text.Substring(block.Start, block.End - block.Start), written, StringComparison.Ordinal)
                    ? Array.Empty<TextReplacement>()
                    : new[] { new TextReplacement(block.Start, block.End - block.Start, written) };
            }

            if (steps.Count == 0)
            {
                return Array.Empty<TextReplacement>();
            }

            if (tab.Children.Count > 0)
            {
                return new[] { new TextReplacement(tab.Children[0].Start, 0, written + newline + indent) };
            }

            if (tab.SelfClosing)
            {
                var at = tab.StartTagClose;
                while (at > tab.Start && char.IsWhiteSpace(text[at - 1]))
                {
                    at--;
                }

                return new[] { new TextReplacement(at, tab.End - at, ">" + newline + indent + written + newline + tabIndent + "</" + tab.Name + ">") };
            }

            return new[] { new TextReplacement(tab.StartTagEnd, tab.CloseTagStart - tab.StartTagEnd, newline + indent + written + newline + tabIndent) };
        }

        private static string T(string english, string french) => DesignerText.IsFrench ? french : english;

        private static string LineIndent(string text, int offset)
        {
            var lineStart = offset;
            while (lineStart > 0 && text[lineStart - 1] != '\n' && text[lineStart - 1] != '\r')
            {
                lineStart--;
            }

            var end = lineStart;
            while (end < text.Length && (text[end] == ' ' || text[end] == '\t'))
            {
                end++;
            }

            return text.Substring(lineStart, end - lineStart);
        }
    }
}

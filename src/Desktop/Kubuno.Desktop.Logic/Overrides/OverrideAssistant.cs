using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Kubuno.Desktop.Logic.Overrides
{
    /// <summary>One text edit (offsets in the original text).</summary>
    public sealed class RustTextEdit
    {
        public RustTextEdit(int start, int length, string newText)
        {
            Start = start;
            Length = length;
            NewText = newText;
        }

        public int Start { get; }

        public int Length { get; }

        public string NewText { get; }

        public override string ToString() => $"@{Start}+{Length} \"{NewText}\"";
    }

    /// <summary>What "Substituer des membres…" works on at a caret: the class, its chain and what it already overrides.</summary>
    public sealed class OverrideContext
    {
        internal OverrideContext(RustItemScanner scan, string typeName, IReadOnlyList<string> chain, RustStructItem? @struct, IReadOnlyList<RustImplItem> impls, IReadOnlyList<OverridableMember> available)
        {
            Scan = scan;
            TypeName = typeName;
            Chain = chain;
            Struct = @struct;
            Impls = impls;
            Available = available;
        }

        internal RustItemScanner Scan { get; }

        /// <summary>The text the context was computed from.</summary>
        public string Text => Scan.Text;

        /// <summary>The class (<c>RoundButton</c>).</summary>
        public string TypeName { get; }

        /// <summary>The class followed by its ancestors, <c>"Component"</c> last.</summary>
        public IReadOnlyList<string> Chain { get; }

        /// <summary>Its struct in this file, when it is declared here.</summary>
        public RustStructItem? Struct { get; }

        /// <summary>Its level-trait impls in this file.</summary>
        public IReadOnlyList<RustImplItem> Impls { get; }

        /// <summary>The members it can override and does not override yet, nearest level first.</summary>
        public IReadOnlyList<OverridableMember> Available { get; }

        /// <summary>Whether <paramref name="member"/> is already overridden.</summary>
        public bool IsOverridden(OverridableMember member) => Impls.Any(i => i.Trait == member.Level && i.Methods.Contains(member.Name));
    }

    /// <summary>
    /// The override assistance of Kubuno controls (docs/EVENTS.md EVT-7b, "Override assistance"): independent of
    /// rust-analyzer, it finds the control class at the caret (inside <c>impl Control for X</c>, or on a
    /// <c>#[derive(Component)]</c> struct), lists the overridable members of its chain it does not override yet, and
    /// writes the chosen ones - into the existing <c>impl &lt;Level&gt; for X</c>, or into a new one, adding the level
    /// to <c>#[kubuno(overrides(…))]</c> (without which the derive's empty impl would conflict) and the prelude import.
    /// </summary>
    public static class OverrideAssistant
    {
        /// <summary>The context at <paramref name="caret"/>, null when the caret is not on a control class.</summary>
        public static OverrideContext? Analyze(string text, int caret, OverrideCatalog catalog)
        {
            var scan = RustItemScanner.Scan(text);
            string? typeName = null;

            // Inside a level-trait impl (`impl Control for RoundButton { | }`), or its header.
            var impl = scan.Impls.Where(i => i.Trait is not null && OverrideCatalog.IsLevel(i.Trait) && caret >= i.ItemStart && caret <= i.CloseBrace + 1).OrderByDescending(i => i.OpenBrace).FirstOrDefault();
            if (impl is not null)
            {
                typeName = impl.Type;
            }

            // On a control's struct (its attributes, declaration or fields).
            typeName ??= scan.Structs.FirstOrDefault(s => s.IsComponent && caret >= s.ItemStart && caret <= s.ItemEnd)?.Name;

            // Inside another impl of a control declared in this file (`impl RoundButton { … }`).
            typeName ??= scan.Impls.Where(i => caret >= i.ItemStart && caret <= i.CloseBrace + 1 && scan.Structs.Any(s => s.IsComponent && s.Name == i.Type)).Select(i => i.Type).FirstOrDefault();
            if (typeName is null)
            {
                return null;
            }

            var @struct = scan.Structs.FirstOrDefault(s => s.Name == typeName && s.IsComponent);
            var chain = ChainOf(typeName, scan, catalog, impl?.Trait, 0);
            var impls = scan.Impls.Where(i => i.Type == typeName && i.Trait is not null && OverrideCatalog.IsLevel(i.Trait)).ToList();
            var available = catalog.ForChain(chain).Where(m => !impls.Any(i => i.Trait == m.Level && i.Methods.Contains(m.Name))).ToList();
            return new OverrideContext(scan, typeName, chain, @struct, impls, available);
        }

        /// <summary>The chain of <paramref name="typeName"/>: from its struct's <c>extends</c> (a built-in class, a level, or a class of the same file), else from the impl's level.</summary>
        private static IReadOnlyList<string> ChainOf(string typeName, RustItemScanner scan, OverrideCatalog catalog, string? implLevel, int depth)
        {
            var result = new List<string> { typeName };
            var @struct = scan.Structs.FirstOrDefault(s => s.Name == typeName && s.IsComponent);
            var extends = @struct?.Extends ?? (@struct?.IsUserControl == true ? "UserControl" : null);
            if (extends is not null && catalog.Chains.TryGetValue(extends, out var builtin))
            {
                result.AddRange(builtin);
            }
            else if (extends is not null && depth < 16 && scan.Structs.Any(s => s.Name == extends && s.IsComponent))
            {
                result.AddRange(ChainOf(extends, scan, catalog, null, depth + 1));
            }
            else
            {
                if (extends is not null)
                {
                    result.Add(extends);
                }

                var levels = @struct?.NamedLevels("levels") ?? Array.Empty<string>();
                foreach (var level in levels.Concat(implLevel is null ? Array.Empty<string>() : new[] { implLevel }))
                {
                    if (catalog.Chains.TryGetValue(level, out var levelChain))
                    {
                        result.AddRange(levelChain.Where(l => !result.Contains(l)));
                    }
                }

                if (!result.Contains("Component"))
                {
                    result.Add("Component");
                }
            }

            return result;
        }

        /// <summary>
        /// The edits writing <paramref name="members"/> (overriding their base) into <paramref name="context"/>'s class;
        /// <paramref name="caret"/> is where to place the caret afterwards (inside the first new method), in the
        /// EDITED text.
        /// </summary>
        public static IReadOnlyList<RustTextEdit> Plan(OverrideContext context, IEnumerable<OverridableMember> members, string newline, out int caret)
        {
            var text = context.Scan.Text;
            var edits = new List<RustTextEdit>();
            var chosen = members.Where(m => !context.IsOverridden(m)).GroupBy(m => m.Level).ToList();
            caret = -1;
            var insertAfter = new List<string>();
            var newImpls = new StringBuilder();

            foreach (var group in chosen)
            {
                var stubs = string.Join(newline, group.Select(m => m.Stub("    ", newline)));
                var impl = context.Impls.FirstOrDefault(i => i.Trait == group.Key);
                if (impl is not null)
                {
                    // Into the existing impl, before its closing brace, after a blank line when it has content.
                    var body = text.Substring(impl.OpenBrace + 1, impl.CloseBrace - impl.OpenBrace - 1);
                    var closeLineStart = text.LastIndexOf('\n', Math.Max(0, impl.CloseBrace - 1)) + 1;
                    var onOwnLine = text.Substring(closeLineStart, impl.CloseBrace - closeLineStart).Trim().Length == 0;
                    var prefix = body.Trim().Length == 0 ? (onOwnLine ? string.Empty : newline) : (onOwnLine ? newline : newline + newline);
                    var at = onOwnLine ? closeLineStart : impl.CloseBrace;
                    edits.Add(new RustTextEdit(at, 0, prefix + stubs));
                }
                else
                {
                    newImpls.Append(newline).Append("impl ").Append(group.Key).Append(" for ").Append(context.TypeName).Append(" {").Append(newline).Append(stubs).Append('}').Append(newline);
                    insertAfter.Add(group.Key);
                }
            }

            if (newImpls.Length > 0)
            {
                // After the class's last level impl, else after its struct, else at the end.
                var anchor = context.Impls.Count > 0 ? context.Impls.Max(i => i.CloseBrace) + 1 : context.Struct?.ItemEnd ?? text.Length;
                var lineEnd = text.IndexOf('\n', anchor);
                var at = lineEnd < 0 ? text.Length : lineEnd + 1;
                var lead = lineEnd < 0 ? newline : string.Empty;
                edits.Add(new RustTextEdit(at, 0, lead + newImpls));

                // The levels the class now implements itself go into `overrides(…)`.
                if (context.Struct is { } s)
                {
                    edits.AddRange(AddOverrides(s, insertAfter, newline));
                }
            }

            if (chosen.Count > 0 && !context.Scan.HasPrelude)
            {
                edits.Add(PreludeImport(context.Scan, newline));
            }

            edits.Sort((a, b) => b.Start.CompareTo(a.Start));

            // The caret: inside the first new method (on its base call), in the edited text.
            var first = chosen.SelectMany(g => g).FirstOrDefault();
            if (first is not null)
            {
                var edited = Apply(text, edits);
                var at = edited.IndexOf(first.Signature, StringComparison.Ordinal);
                caret = at < 0 ? -1 : edited.IndexOf(first.BaseCall, at, StringComparison.Ordinal);
            }

            return edits;
        }

        /// <summary><c>overrides(Level)</c> added to the struct's <c>#[kubuno(…)]</c> (created for a user control without one).</summary>
        private static IEnumerable<RustTextEdit> AddOverrides(RustStructItem s, IReadOnlyList<string> levels, string newline)
        {
            var attribute = s.KubunoAttribute;
            if (attribute is null)
            {
                yield return new RustTextEdit(s.DeclarationLineStart, 0, "#[kubuno(overrides(" + string.Join(", ", levels) + "))]" + newline);
                yield break;
            }

            var (start, _, attrText) = attribute.Value;
            var existing = Regex.Match(attrText, @"overrides\s*\(([^)]*)\)");
            if (existing.Success)
            {
                var already = existing.Groups[1].Value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                var add = levels.Where(l => !already.Contains(l)).ToList();
                if (add.Count > 0)
                {
                    var closeParen = start + existing.Index + existing.Length - 1;
                    yield return new RustTextEdit(closeParen, 0, (already.Count > 0 ? ", " : string.Empty) + string.Join(", ", add));
                }

                yield break;
            }

            // `#[kubuno(extends = Button)]` -> `#[kubuno(extends = Button, overrides(Control))]`.
            var lastParen = attrText.LastIndexOf(')');
            if (lastParen > 0)
            {
                var inner = attrText.Substring(attrText.IndexOf('(') + 1, lastParen - attrText.IndexOf('(') - 1).Trim();
                yield return new RustTextEdit(start + lastParen, 0, (inner.Length > 0 ? ", " : string.Empty) + "overrides(" + string.Join(", ", levels) + ")");
            }
        }

        /// <summary><c>use kubuno_views::prelude::*;</c> after the file's last <c>use</c> (else at the top, after the inner doc).</summary>
        private static RustTextEdit PreludeImport(RustItemScanner scan, string newline)
        {
            var uses = Regex.Matches(scan.Masked, @"(?m)^\s*(pub\s+)?use\s+[^;]*;");
            if (uses.Count > 0)
            {
                var last = uses[uses.Count - 1];
                var lineEnd = scan.Text.IndexOf('\n', last.Index + last.Length);
                var at = lineEnd < 0 ? scan.Text.Length : lineEnd + 1;
                return new RustTextEdit(at, 0, "use kubuno_views::prelude::*;" + newline);
            }

            // After the leading `//!` lines.
            var offset = 0;
            foreach (var line in scan.Text.Split('\n'))
            {
                if (!line.TrimStart().StartsWith("//!", StringComparison.Ordinal) && !line.TrimStart().StartsWith("#![", StringComparison.Ordinal))
                {
                    break;
                }

                offset += line.Length + 1;
            }

            return new RustTextEdit(Math.Min(offset, scan.Text.Length), 0, "use kubuno_views::prelude::*;" + newline + newline);
        }

        /// <summary>
        /// Where completion can offer Kubuno items at <paramref name="caret"/> (docs/EVENTS.md EVT-7b): an item position -
        /// the line holds only the identifier being typed, possibly after <c>fn </c> - directly inside an <c>impl</c>
        /// block (overridable members of a level impl, the <c>onpaint</c>/<c>handler</c> snippets) or inside the body of
        /// a control's struct (the <c>event</c>/<c>prop</c> snippets). Null anywhere else.
        /// </summary>
        public static RustCompletionSite? CompletionSiteAt(string text, int caret, OverrideCatalog catalog)
        {
            if (caret < 0 || caret > text.Length)
            {
                return null;
            }

            var lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
            if (caret == 0)
            {
                lineStart = 0;
            }

            var before = text.Substring(lineStart, caret - lineStart);
            var shape = Regex.Match(before, @"^([ \t]*)((fn[ \t]+)?[A-Za-z_]*)$");
            if (!shape.Success)
            {
                return null;
            }

            var scan = RustItemScanner.Scan(text);
            int Depth(int from)
            {
                var depth = 0;
                for (var i = from; i < caret; i++)
                {
                    depth += scan.Masked[i] == '{' ? 1 : scan.Masked[i] == '}' ? -1 : 0;
                }

                return depth;
            }

            var site = new RustCompletionSite(lineStart + shape.Groups[1].Length, shape.Groups[1].Value);
            var impl = scan.Impls.Where(i => caret > i.OpenBrace && caret <= i.CloseBrace && Depth(i.OpenBrace) == 1).OrderByDescending(i => i.OpenBrace).FirstOrDefault();
            if (impl is not null)
            {
                site.InImpl = true;
                site.ImplTrait = impl.Trait;
                if (impl.Trait is not null && OverrideCatalog.IsLevel(impl.Trait))
                {
                    site.Context = Analyze(text, caret, catalog);
                }

                return site;
            }

            var @struct = scan.Structs.FirstOrDefault(s => s.IsComponent && caret > s.DeclarationLineStart && caret < s.ItemEnd);
            if (@struct is not null)
            {
                var open = scan.Masked.IndexOf('{', @struct.DeclarationLineStart);
                if (open >= 0 && open < caret && Depth(open) == 1)
                {
                    return site;
                }
            }

            return null;
        }

        /// <summary>Applies <paramref name="edits"/> (non-overlapping, any order) to <paramref name="text"/>.</summary>
        public static string Apply(string text, IEnumerable<RustTextEdit> edits)
        {
            var result = text;
            foreach (var edit in edits.OrderByDescending(e => e.Start).ThenByDescending(e => e.Length))
            {
                result = result.Substring(0, edit.Start) + edit.NewText + result.Substring(edit.Start + edit.Length);
            }

            return result;
        }
    }
}

namespace Kubuno.Desktop.Logic.Overrides
{
    /// <summary>A completion position of <see cref="OverrideAssistant.CompletionSiteAt"/>.</summary>
    public sealed class RustCompletionSite
    {
        public RustCompletionSite(int replaceStart, string indent)
        {
            ReplaceStart = replaceStart;
            Indent = indent;
        }

        /// <summary>Where an accepted item's text starts (the word being typed, or its <c>fn</c>).</summary>
        public int ReplaceStart { get; }

        /// <summary>The line's indentation (each inserted line after the first gets it).</summary>
        public string Indent { get; }

        /// <summary>Directly inside an <c>impl</c> block (else inside a control's struct).</summary>
        public bool InImpl { get; set; }

        /// <summary>The impl's trait (last segment), null for an inherent impl.</summary>
        public string? ImplTrait { get; set; }

        /// <summary>For a level impl (<c>impl Control for X</c>): the members still to override.</summary>
        public OverrideContext? Context { get; set; }
    }
}

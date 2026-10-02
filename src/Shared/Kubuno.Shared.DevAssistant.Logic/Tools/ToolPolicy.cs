using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kubuno.Core.DevAssistant.Logic.Protocol;

namespace Kubuno.Core.DevAssistant.Logic.Tools
{
    /// <summary>
    /// What the assistant may expose to a model at all (docs/AI-ASSISTANT.md section 7.4), enforced in code on both
    /// sides of the channel - never by the prompt. DA-1 allows only <see cref="ApprovalClass.Read"/> tools and
    /// <see cref="ApprovalClass.Write"/> tools that PROPOSE a change set (applied after the developer's review): no
    /// execute-class tool exists yet (no shell, no build, no run), and nothing that could express a push, a tag, a
    /// publication, a release, a commit or a database access can be registered, whatever layer contributes it.
    /// </summary>
    public static class ToolPolicy
    {
        /// <summary>Tool names follow <c>family_action</c>: lower-case snake case, 3 to 64 characters.</summary>
        private static readonly Regex NamePattern = new Regex("^[a-z][a-z0-9_]{2,63}$", RegexOptions.CultureInvariant);

        /// <summary>Words no tool name may contain (outbound actions and execution are not expressible).</summary>
        private static readonly string[] ForbiddenWords =
        {
            "push", "publish", "release", "tag", "commit", "remote", "deploy", "upload", "shell", "bash", "cmd", "powershell",
            "exec", "execute", "run", "spawn", "process", "sql", "database", "db", "query", "install", "delete", "rm", "http",
            "fetch", "curl", "web", "npm", "cargo", "git_reset", "git_clean", "credential", "secret", "token",
        };

        /// <summary>Families DA-1 knows; a tool must belong to one of them.</summary>
        private static readonly string[] AllowedFamilies = { "vs", "fs", "edit", "kbview", "kbres", "kubuno" };

        /// <summary>Why <paramref name="descriptor"/> may not be exposed in this lot, or null when it may.</summary>
        public static string? WhyRejected(ToolDescriptor descriptor)
        {
            if (descriptor is null)
            {
                return "null descriptor";
            }

            if (!NamePattern.IsMatch(descriptor.Name ?? string.Empty))
            {
                return $"'{descriptor.Name}': not a snake_case tool name";
            }

            var words = descriptor.Name!.Split('_');
            if (!AllowedFamilies.Contains(words[0]))
            {
                return $"'{descriptor.Name}': unknown tool family '{words[0]}'";
            }

            var forbidden = words.Skip(1).Concat(new[] { descriptor.Name }).FirstOrDefault(w => ForbiddenWords.Contains(w));
            if (forbidden is not null)
            {
                return $"'{descriptor.Name}': '{forbidden}' actions are not expressible by any tool";
            }

            switch (descriptor.ApprovalClass)
            {
                case ApprovalClass.Read:
                    return null;
                case ApprovalClass.Write:
                    // Writes only propose: every write tool must say so in its name (edit_propose, kbview_apply_ops...).
                    return descriptor.Name.StartsWith("edit_", StringComparison.Ordinal) || descriptor.Name.EndsWith("_apply_ops", StringComparison.Ordinal) || descriptor.Name.EndsWith("_set", StringComparison.Ordinal)
                        ? null
                        : $"'{descriptor.Name}': write tools must propose a reviewed change set (edit_*, *_apply_ops, *_set)";
                case ApprovalClass.Execute:
                    return $"'{descriptor.Name}': execute-class tools do not exist before lot DA-3";
                default:
                    return $"'{descriptor.Name}': forbidden";
            }
        }

        /// <summary>The descriptors that pass <see cref="WhyRejected"/>, sorted by name (a deterministic, cache-friendly tool list).</summary>
        public static IReadOnlyList<ToolDescriptor> Filter(IEnumerable<ToolDescriptor> descriptors, ICollection<string>? rejected = null)
        {
            var kept = new List<ToolDescriptor>();
            foreach (var descriptor in descriptors)
            {
                var why = WhyRejected(descriptor);
                if (why is null)
                {
                    if (kept.All(d => d.Name != descriptor.Name))
                    {
                        kept.Add(descriptor);
                    }
                }
                else
                {
                    rejected?.Add(why);
                }
            }

            return kept.OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
        }
    }
}

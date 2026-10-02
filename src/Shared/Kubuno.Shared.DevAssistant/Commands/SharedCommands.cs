using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Kubuno.Shared.DevAssistant.Extensibility;
using Kubuno.Shared.DevAssistant.Logic.Prompts;

namespace Kubuno.Shared.DevAssistant.Commands
{
    /// <summary>
    /// The Shared layer's commands in DA-1: <c>/expliquer</c> (explain the current error or selection and propose a fix -
    /// the Rust layer adds <c>rustc --explain</c> with the execution tools of DA-3) and <c>/aide</c>.
    /// </summary>
    internal sealed class SharedCommands : IDevAssistantCommandProvider
    {
        public IEnumerable<DevAssistantCommand> GetCommands() => new[]
        {
            new DevAssistantCommand
            {
                Name = "expliquer",
                Aliases = new[] { "explain" },
                DescriptionFr = "Expliquer l'erreur courante ou la sélection, et proposer une correction",
                DescriptionEn = "Explain the current error or the selection, and propose a fix",
                Effort = "medium",
                DefaultReferences = new[] { ReferenceKinds.Selection },
                Digest =
                    "Command /expliquer: explain an error or the selected code. Start from the attached selection, the Error " +
                    "List (vs_error_list) and the source around each error (fs_read with a line range). Explain the cause in a " +
                    "few sentences, then propose a minimal fix with edit_propose (anchored old_text/new_text). If the cause " +
                    "is outside the files you can read, say so instead of guessing.",
            },
            new DevAssistantCommand
            {
                Name = "aide",
                Aliases = new[] { "help" },
                DescriptionFr = "Lister les commandes et les références",
                DescriptionEn = "List the commands and references",
                Effort = "low",
                Digest = "Command /aide: list the available / commands and # references (they are described in the user message) in a short table.",
            },
        };

        /// <summary>The rules digest (section 6.1, tier 1), embedded in this assembly.</summary>
        public static string RulesDigest()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Kubuno.Shared.DevAssistant.Knowledge.rules.md");
            if (stream is null)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}

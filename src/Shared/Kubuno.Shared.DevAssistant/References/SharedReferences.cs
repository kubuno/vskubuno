using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Core.DevAssistant.Extensibility;
using Kubuno.Core.DevAssistant.Logic.Changes;
using Kubuno.Core.DevAssistant.Logic.Prompts;
using Kubuno.Core.DevAssistant.Tools;
using Kubuno.Core.Mcp.Bridge;
using Kubuno.Core.Mcp.Bridge.Contracts;

namespace Kubuno.Core.DevAssistant.References
{
    /// <summary>
    /// <c>#fichier[:chemin]</c> and <c>#sélection</c> (docs/AI-ASSISTANT.md section 5.2), resolved from the in-proc
    /// <see cref="IVsContextProvider"/> when the message is sent. The content is masked by the caller.
    /// </summary>
    internal sealed class CoreReferences : IDevAssistantReferenceProvider
    {
        private const int MaxChars = 60_000;
        private const int SelectionContextLines = 5;

        private readonly IVsContextProvider _context;

        public CoreReferences(IVsContextProvider context)
        {
            _context = context;
        }

        public IEnumerable<string> Kinds => new[] { ReferenceKinds.File, ReferenceKinds.Selection };

        public async Task<ResolvedReference?> ResolveAsync(PromptReference reference, DevAssistantToolContext context, CancellationToken cancellationToken)
        {
            if (reference.Kind == ReferenceKinds.File)
            {
                string? path = reference.Argument is { Length: > 0 } argument ? CoreTools.Resolve(argument, context) : null;
                if (path is null)
                {
                    var active = await _context.GetActiveDocumentAsync(new ActiveDocumentParams(), cancellationToken).ConfigureAwait(false);
                    path = active.HasActiveDocument ? active.Path : null;
                }

                if (path is null)
                {
                    return null;
                }

                if (context.WhyDenied(path) is { } why)
                {
                    return new ResolvedReference(reference.Kind, Path.GetFileName(path), $"(not attached: {why})");
                }

                var text = context.ReadDocument(path);
                if (text is null)
                {
                    return null;
                }

                var body = text.Length > MaxChars ? text.Substring(0, MaxChars) + "\n… (truncated; use fs_read with a line range for the rest)" : text;
                return new ResolvedReference(reference.Kind, Path.GetFileName(path), "path: " + path + "\n\n" + body);
            }

            if (reference.Kind == ReferenceKinds.Selection)
            {
                var selection = await _context.GetSelectionAsync(cancellationToken).ConfigureAwait(false);
                if (!selection.HasSelection || selection.FilePath is null)
                {
                    return null;
                }

                if (context.WhyDenied(selection.FilePath) is { } why)
                {
                    return new ResolvedReference(reference.Kind, Path.GetFileName(selection.FilePath), $"(not attached: {why})");
                }

                var builder = new StringBuilder();
                builder.Append("file: ").Append(selection.FilePath).Append('\n');
                builder.Append("selection: lines ").Append(selection.StartLine).Append(':').Append(selection.StartColumn).Append(" to ").Append(selection.EndLine).Append(':').Append(selection.EndColumn).Append("\n\n");
                builder.Append("selected text:\n").Append(selection.Text).Append("\n\n");
                if (context.ReadDocument(selection.FilePath) is { } text)
                {
                    var lines = LineDiff.SplitLines(text);
                    int from = Math.Max(1, selection.StartLine - SelectionContextLines);
                    int to = Math.Min(lines.Count, selection.EndLine + SelectionContextLines);
                    builder.Append("surrounding lines ").Append(from).Append('-').Append(to).Append(":\n");
                    for (int i = from - 1; i < to; i++)
                    {
                        builder.Append(i + 1).Append(": ").Append(lines[i].TrimEnd('\r', '\n')).Append('\n');
                    }
                }

                var label = $"{Path.GetFileName(selection.FilePath)} ({selection.StartLine}-{selection.EndLine})";
                return new ResolvedReference(reference.Kind, label, builder.ToString());
            }

            return null;
        }
    }
}

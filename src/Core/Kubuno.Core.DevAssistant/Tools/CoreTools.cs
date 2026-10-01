using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Core.DevAssistant.Extensibility;
using Kubuno.Core.DevAssistant.Logic.Changes;
using Kubuno.Core.DevAssistant.Logic.Protocol;
using Kubuno.Core.Mcp.Bridge;
using Kubuno.Core.Mcp.Bridge.Contracts;

namespace Kubuno.Core.DevAssistant.Tools
{
    /// <summary>
    /// The Core layer's tools (docs/AI-ASSISTANT.md section 6.3): Visual Studio context through the MCP bridge's
    /// <see cref="IVsContextProvider"/> (in-proc, not the pipe), file reads inside the solution roots with the denied-file
    /// rules, and <c>edit_propose</c> - anchored replacements and new files that only build the change set reviewed by
    /// the developer. All read-only except <c>edit_propose</c>, which writes nothing itself.
    /// </summary>
    internal static class CoreTools
    {
        private const int MaxReadChars = 60_000;
        private const int MaxGrepMatches = 200;
        private const int MaxListEntries = 500;

        public static IReadOnlyList<IDevAssistantTool> Create(IVsContextProvider context) => new IDevAssistantTool[]
        {
            new DelegateTool(
                "vs_active_document",
                "The document active in Visual Studio's editor: path, language, unsaved flag and text (optionally a 1-based line range).",
                Schema(@"{""type"":""object"",""properties"":{""startLine"":{""type"":""integer""},""endLine"":{""type"":""integer""}},""additionalProperties"":false}"),
                ApprovalClass.Read,
                async (input, _, token) =>
                {
                    var parameters = new ActiveDocumentParams { StartLine = Int(input, "startLine"), EndLine = Int(input, "endLine") };
                    var info = await context.GetActiveDocumentAsync(parameters, token).ConfigureAwait(false);
                    if (info.Text is { Length: > MaxReadChars })
                    {
                        info.Text = info.Text.Substring(0, MaxReadChars) + "\n… (truncated: ask a line range)";
                    }

                    return DevAssistantToolResult.Json(info);
                }),
            new DelegateTool(
                "vs_selection",
                "The current editor selection: file, 1-based range and selected text.",
                Schema(@"{""type"":""object"",""properties"":{},""additionalProperties"":false}"),
                ApprovalClass.Read,
                async (_, _, token) => DevAssistantToolResult.Json(await context.GetSelectionAsync(token).ConfigureAwait(false))),
            new DelegateTool(
                "vs_error_list",
                "Items of Visual Studio's Error List: severity, file, line, column, message, project.",
                Schema(@"{""type"":""object"",""properties"":{""maxItems"":{""type"":""integer""}},""additionalProperties"":false}"),
                ApprovalClass.Read,
                async (input, _, token) => DevAssistantToolResult.Json(await context.GetErrorListAsync(new ErrorListParams { MaxItems = Int(input, "maxItems") ?? 50 }, token).ConfigureAwait(false))),
            new DelegateTool(
                "vs_open_documents",
                "Every document open in Visual Studio, with the active and unsaved flags.",
                Schema(@"{""type"":""object"",""properties"":{},""additionalProperties"":false}"),
                ApprovalClass.Read,
                async (_, _, token) => DevAssistantToolResult.Json(await context.GetOpenDocumentsAsync(token).ConfigureAwait(false))),
            new DelegateTool(
                "fs_read",
                "Reads a file of the solution (its unsaved editor text when open). Optional 1-based line range. Secret files are denied.",
                Schema(@"{""type"":""object"",""properties"":{""path"":{""type"":""string"",""description"":""Absolute path, or relative to the solution root.""},""startLine"":{""type"":""integer""},""endLine"":{""type"":""integer""}},""required"":[""path""],""additionalProperties"":false}"),
                ApprovalClass.Read,
                (input, toolContext, _) => Task.FromResult(Read(input, toolContext))),
            new DelegateTool(
                "fs_list",
                "Lists files of the solution matching a glob (e.g. src/**/*.kbview). Protected folders (.git, .vs, target, node_modules) are skipped.",
                Schema(@"{""type"":""object"",""properties"":{""glob"":{""type"":""string""}},""required"":[""glob""],""additionalProperties"":false}"),
                ApprovalClass.Read,
                (input, toolContext, token) => Task.FromResult(List(input, toolContext, token))),
            new DelegateTool(
                "fs_grep",
                "Searches the solution's files for a regular expression (optionally restricted by a glob). Returns path:line: text.",
                Schema(@"{""type"":""object"",""properties"":{""pattern"":{""type"":""string""},""glob"":{""type"":""string""}},""required"":[""pattern""],""additionalProperties"":false}"),
                ApprovalClass.Read,
                (input, toolContext, token) => Task.FromResult(Grep(input, toolContext, token))),
            new DelegateTool(
                "edit_propose",
                "Proposes file changes for the developer to review (nothing is written until they accept). Each change is either {path, edits:[{old_text, new_text}]} - old_text must occur exactly once in the current file, copied exactly, with enough surrounding lines to be unique - or {path, new_file_content} for a new file. Never use line numbers. Keep «secret:…» markers exactly as you received them.",
                Schema(@"{""type"":""object"",""properties"":{""changes"":{""type"":""array"",""items"":{""type"":""object"",""properties"":{""path"":{""type"":""string""},""edits"":{""type"":""array"",""items"":{""type"":""object"",""properties"":{""old_text"":{""type"":""string""},""new_text"":{""type"":""string""}},""required"":[""old_text"",""new_text""],""additionalProperties"":false}},""new_file_content"":{""type"":""string""}},""required"":[""path""],""additionalProperties"":false}},""summary"":{""type"":""string""}},""required"":[""changes""],""additionalProperties"":false}"),
                ApprovalClass.Write,
                (input, toolContext, _) => Task.FromResult(Propose(input, toolContext))),
        };

        internal static JsonElement Schema(string json) => RpcCodec.ParseElement(json);

        internal static int? Int(JsonElement input, string name) =>
            input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

        internal static string? Str(JsonElement input, string name) =>
            input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        /// <summary>An absolute path for <paramref name="path"/> (relative paths are taken from the first solution root).</summary>
        internal static string Resolve(string path, DevAssistantToolContext context)
        {
            var trimmed = path.Trim().Trim('"');
            if (!Path.IsPathRooted(trimmed) && context.SolutionRoots.Count > 0)
            {
                trimmed = Path.Combine(context.SolutionRoots[0], trimmed);
            }

            return Path.GetFullPath(trimmed);
        }

        private static DevAssistantToolResult Read(JsonElement input, DevAssistantToolContext context)
        {
            var path = Resolve(Str(input, "path") ?? string.Empty, context);
            if (context.WhyDenied(path) is { } why)
            {
                return DevAssistantToolResult.Error($"Access denied to {path} ({why}).");
            }

            var text = context.ReadDocument(path);
            if (text is null)
            {
                return DevAssistantToolResult.Error($"{path} does not exist.");
            }

            var lines = LineDiff.SplitLines(text);
            int start = Math.Max(1, Int(input, "startLine") ?? 1);
            int end = Math.Min(lines.Count, Int(input, "endLine") ?? lines.Count);
            var builder = new StringBuilder();
            builder.Append("path: ").Append(path).Append('\n').Append("lines: ").Append(start).Append('-').Append(end).Append(" of ").Append(lines.Count).Append("\n\n");
            for (int i = start - 1; i < end && builder.Length < MaxReadChars; i++)
            {
                builder.Append(lines[i]);
            }

            if (builder.Length >= MaxReadChars)
            {
                builder.Append("\n… (truncated: read a line range)");
            }

            return DevAssistantToolResult.Text(builder.ToString());
        }

        private static IEnumerable<string> EnumerateFiles(DevAssistantToolContext context, string? glob, CancellationToken token)
        {
            var regex = glob is null ? null : GlobToRegex(glob);
            foreach (var root in context.SolutionRoots)
            {
                var stack = new Stack<string>();
                stack.Push(root);
                while (stack.Count > 0)
                {
                    token.ThrowIfCancellationRequested();
                    var folder = stack.Pop();
                    IEnumerable<string> subfolders;
                    IEnumerable<string> files;
                    try
                    {
                        subfolders = Directory.EnumerateDirectories(folder).ToList();
                        files = Directory.EnumerateFiles(folder).ToList();
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        continue;
                    }

                    foreach (var subfolder in subfolders)
                    {
                        var name = Path.GetFileName(subfolder).ToLowerInvariant();
                        if (name is ".git" or ".vs" or "target" or "node_modules" or "bin" or "obj" or ".ssh")
                        {
                            continue;
                        }

                        stack.Push(subfolder);
                    }

                    foreach (var file in files)
                    {
                        var relative = file.Substring(root.TrimEnd('\\').Length).TrimStart('\\').Replace('\\', '/');
                        if ((regex is null || regex.IsMatch(relative)) && context.WhyDenied(file) is null)
                        {
                            yield return file;
                        }
                    }
                }
            }
        }

        private static DevAssistantToolResult List(JsonElement input, DevAssistantToolContext context, CancellationToken token)
        {
            var files = EnumerateFiles(context, Str(input, "glob"), token).Take(MaxListEntries + 1).ToList();
            var text = string.Join("\n", files.Take(MaxListEntries));
            if (files.Count > MaxListEntries)
            {
                text += "\n… (more files: narrow the glob)";
            }

            return DevAssistantToolResult.Text(files.Count == 0 ? "No file matches." : text);
        }

        private static DevAssistantToolResult Grep(JsonElement input, DevAssistantToolContext context, CancellationToken token)
        {
            Regex pattern;
            try
            {
                pattern = new Regex(Str(input, "pattern") ?? string.Empty, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException exception)
            {
                return DevAssistantToolResult.Error("Invalid regular expression: " + exception.Message);
            }

            var results = new StringBuilder();
            int count = 0;
            foreach (var file in EnumerateFiles(context, Str(input, "glob"), token))
            {
                if (new FileInfo(file).Length > 2_000_000)
                {
                    continue;
                }

                var text = context.ReadDocument(file);
                if (text is null || text.IndexOf('\0') >= 0)
                {
                    continue;
                }

                var lines = text.Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    try
                    {
                        if (!pattern.IsMatch(lines[i]))
                        {
                            continue;
                        }
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        continue;
                    }

                    results.Append(file).Append(':').Append(i + 1).Append(": ").Append(lines[i].Length > 300 ? lines[i].Substring(0, 300) + "…" : lines[i]).Append('\n');
                    if (++count >= MaxGrepMatches)
                    {
                        results.Append("… (more matches: refine the pattern or the glob)");
                        return DevAssistantToolResult.Text(results.ToString());
                    }
                }
            }

            return DevAssistantToolResult.Text(count == 0 ? "No match." : results.ToString());
        }

        private static DevAssistantToolResult Propose(JsonElement input, DevAssistantToolContext context)
        {
            var summary = new StringBuilder();
            if (!input.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
            {
                return DevAssistantToolResult.Error("changes is required.");
            }

            foreach (var change in changes.EnumerateArray())
            {
                var path = Resolve(Str(change, "path") ?? string.Empty, context);
                if (context.WhyDenied(path) is { } why)
                {
                    return DevAssistantToolResult.Error($"{path}: writes are not allowed there ({why}).");
                }

                var current = context.ReadDocument(path);
                if (Str(change, "new_file_content") is { } content)
                {
                    if (current is not null)
                    {
                        return DevAssistantToolResult.Error($"{path} already exists: use edits with old_text/new_text.");
                    }

                    var restored = AnchoredEditApplier.RestoreNewFile(content, context.Masker);
                    if (!restored.Succeeded)
                    {
                        return DevAssistantToolResult.Error(path + ": " + restored.Error);
                    }

                    context.ProposeChange(path, null, restored.Text!);
                    summary.Append("new file ").Append(path).Append('\n');
                    continue;
                }

                if (current is null)
                {
                    return DevAssistantToolResult.Error($"{path} does not exist: give new_file_content to create it.");
                }

                var edits = new List<AnchoredEdit>();
                if (change.TryGetProperty("edits", out var editArray) && editArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var edit in editArray.EnumerateArray())
                    {
                        edits.Add(new AnchoredEdit { OldText = Str(edit, "old_text") ?? string.Empty, NewText = Str(edit, "new_text") ?? string.Empty });
                    }
                }

                var proposal = AnchoredEditApplier.Apply(current, edits, context.Masker);
                if (!proposal.Succeeded)
                {
                    return DevAssistantToolResult.Error(path + ": " + proposal.Error);
                }

                context.ProposeChange(path, current, proposal.Text!);
                var hunks = LineDiff.Compute(current, proposal.Text!);
                summary.Append(path).Append(": ").Append(hunks.Count).Append(" hunk(s), +").Append(hunks.Sum(h => h.Added)).Append(" -").Append(hunks.Sum(h => h.Removed)).Append('\n');
            }

            summary.Append("Proposed to the developer for review; nothing is written until they accept.");
            return DevAssistantToolResult.Text(summary.ToString());
        }

        private static Regex GlobToRegex(string glob)
        {
            var normalized = glob.Replace('\\', '/').TrimStart('/');
            var builder = new StringBuilder("^");
            for (int i = 0; i < normalized.Length; i++)
            {
                var c = normalized[i];
                if (c == '*' && i + 1 < normalized.Length && normalized[i + 1] == '*')
                {
                    builder.Append(".*");
                    i++;
                    if (i + 1 < normalized.Length && normalized[i + 1] == '/')
                    {
                        i++;
                        builder.Append("/?");
                    }
                }
                else if (c == '*')
                {
                    builder.Append("[^/]*");
                }
                else if (c == '?')
                {
                    builder.Append("[^/]");
                }
                else
                {
                    builder.Append(Regex.Escape(c.ToString()));
                }
            }

            builder.Append('$');
            return new Regex(builder.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }

    /// <summary>A tool built from a descriptor and a delegate.</summary>
    internal sealed class DelegateTool : IDevAssistantTool
    {
        private readonly Func<JsonElement, DevAssistantToolContext, CancellationToken, Task<DevAssistantToolResult>> _invoke;

        public DelegateTool(string name, string description, JsonElement schema, ApprovalClass approvalClass, Func<JsonElement, DevAssistantToolContext, CancellationToken, Task<DevAssistantToolResult>> invoke)
        {
            Descriptor = new ToolDescriptor { Name = name, Description = description, InputSchema = schema, ApprovalClass = approvalClass };
            _invoke = invoke;
        }

        public ToolDescriptor Descriptor { get; }

        public Task<DevAssistantToolResult> InvokeAsync(JsonElement input, DevAssistantToolContext context, CancellationToken cancellationToken) =>
            _invoke(input, context, cancellationToken);
    }
}

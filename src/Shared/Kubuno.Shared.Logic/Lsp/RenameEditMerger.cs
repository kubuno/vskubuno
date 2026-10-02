using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kubuno.Shared.Logic.Lsp
{
    /// <summary>
    /// Merges two LSP <c>WorkspaceEdit</c>s into one, so Visual Studio applies them as one rename - e.g. a rename started in
    /// the Rust editor (F2 / Ctrl+R, R on a handler method - docs/EVENTS.md §5.5, EVT-5): rust-analyzer's edit renames the
    /// Rust side, and the desktop layer adds the edits <c>kubuno/renameHandler</c> (with <c>rustRenamed</c>) computes for
    /// the <c>.kbview</c> attributes and the <c>handlers!</c> strings. Pure JSON in, JSON out (the middle layer converts from/to its
    /// <c>JToken</c>s).
    /// </summary>
    public static class RenameEditMerger
    {
        /// <summary>
        /// <paramref name="rustAnalyzerEdit"/> (a <c>WorkspaceEdit</c> with <c>documentChanges</c> or <c>changes</c>)
        /// with every file of <paramref name="kubunoEdit"/>'s <c>changes</c> added: appended to that file's edits when
        /// rust-analyzer already edits it, else as a new entry. Returns <paramref name="rustAnalyzerEdit"/> unchanged
        /// when either side is not a usable edit.
        /// </summary>
        public static string Merge(string rustAnalyzerEdit, string? kubunoEdit)
        {
            if (string.IsNullOrWhiteSpace(kubunoEdit))
            {
                return rustAnalyzerEdit;
            }

            try
            {
                if (JsonNode.Parse(rustAnalyzerEdit) is not JsonObject target ||
                    JsonNode.Parse(kubunoEdit!) is not JsonObject extra ||
                    extra["changes"] is not JsonObject extraChanges)
                {
                    return rustAnalyzerEdit;
                }

                if (target["documentChanges"] is JsonArray documentChanges)
                {
                    foreach (var file in extraChanges)
                    {
                        if (file.Value is not JsonArray edits || edits.Count == 0)
                        {
                            continue;
                        }

                        var existing = documentChanges.OfType<JsonObject>().FirstOrDefault(d =>
                            d["edits"] is JsonArray && d["textDocument"]?["uri"] is JsonValue uri && SameFile(uri.GetValue<string>(), file.Key));
                        if (existing?["edits"] is JsonArray existingEdits)
                        {
                            foreach (var edit in edits)
                            {
                                existingEdits.Add(edit?.DeepClone());
                            }
                        }
                        else
                        {
                            documentChanges.Add(new JsonObject
                            {
                                ["textDocument"] = new JsonObject { ["uri"] = file.Key, ["version"] = null },
                                ["edits"] = edits.DeepClone(),
                            });
                        }
                    }
                }
                else
                {
                    var changes = target["changes"] as JsonObject ?? new JsonObject();
                    target["changes"] = changes;
                    foreach (var file in extraChanges)
                    {
                        if (file.Value is not JsonArray edits || edits.Count == 0)
                        {
                            continue;
                        }

                        var key = changes.Select(c => c.Key).FirstOrDefault(k => SameFile(k, file.Key)) ?? file.Key;
                        if (changes[key] is JsonArray existingEdits)
                        {
                            foreach (var edit in edits)
                            {
                                existingEdits.Add(edit?.DeepClone());
                            }
                        }
                        else
                        {
                            changes[key] = edits.DeepClone();
                        }
                    }
                }

                return target.ToJsonString();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                return rustAnalyzerEdit;
            }
        }

        /// <summary>Whether two <c>file://</c> URIs name the same file (drive letter case and <c>%3A</c> vary between servers).</summary>
        public static bool SameFile(string a, string b)
        {
            try
            {
                return string.Equals(new Uri(Uri.UnescapeDataString(a)).LocalPath, new Uri(Uri.UnescapeDataString(b)).LocalPath, StringComparison.OrdinalIgnoreCase);
            }
            catch (UriFormatException)
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}

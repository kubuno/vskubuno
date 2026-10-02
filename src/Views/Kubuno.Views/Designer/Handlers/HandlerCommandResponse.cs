using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Kubuno.Desktop.Designer.Handlers
{
    /// <summary>
    /// The answer of <c>kubuno/renameHandler</c>, <c>kubuno/removeHandler</c> and <c>kubuno/convertHandlers</c>
    /// (docs/EVENTS.md §5.4/§5.5, EVT-4/EVT-5): an optional <c>WorkspaceEdit</c> (one edit list per file) and what
    /// the server says about it.
    /// </summary>
    public sealed class HandlerCommandResponse
    {
        public HandlerCommandResponse(HandlerWorkspaceEdit? edit, string? handlerName, string? reason, bool removedStub, IReadOnlyList<string> converted)
        {
            Edit = edit;
            HandlerName = handlerName;
            Reason = reason;
            RemovedStub = removedStub;
            Converted = converted ?? throw new ArgumentNullException(nameof(converted));
        }

        public HandlerWorkspaceEdit? Edit { get; }

        /// <summary>The handler concerned: <c>oldName</c> of a rename, <c>handlerName</c> of a removal.</summary>
        public string? HandlerName { get; }

        /// <summary>Why nothing was done (a name already taken, nothing to convert...).</summary>
        public string? Reason { get; }

        /// <summary>A removal also deleted the untouched handler stub.</summary>
        public bool RemovedStub { get; }

        /// <summary>The handlers a conversion turned into typed methods.</summary>
        public IReadOnlyList<string> Converted { get; }
    }

    /// <summary>Pure JSON parsing of the EVT-5 handler commands' answers, like <see cref="CreateHandlerResponseParser"/>.</summary>
    public static class HandlerCommandResponseParser
    {
        /// <summary>A rename/remove/convert answer; null for an empty or malformed payload.</summary>
        public static HandlerCommandResponse? ParseEditResponse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(json!);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var edit = CreateHandlerResponseParser.TryGetNonNull(root, "edit", out var editElement) ? CreateHandlerResponseParser.ParseEdit(editElement) : null;
                var name = String(root, "oldName") ?? String(root, "handlerName");
                var removedStub = root.TryGetProperty("removedStub", out var removed) && removed.ValueKind == JsonValueKind.True;
                var converted = new List<string>();
                if (CreateHandlerResponseParser.TryGetNonNull(root, "converted", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in list.EnumerateArray())
                    {
                        if (item.GetString() is { } handler)
                        {
                            converted.Add(handler);
                        }
                    }
                }

                return new HandlerCommandResponse(edit, name, String(root, "reason"), removedStub, converted);
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                return null;
            }
        }

        /// <summary>The <c>handlers</c> of a <c>kubuno/compatibleHandlers</c> answer; empty for anything else.</summary>
        public static IReadOnlyList<string> ParseHandlerNames(string? json)
        {
            var names = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return names;
            }

            try
            {
                using var document = JsonDocument.Parse(json!);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("handlers", out var handlers) &&
                    handlers.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in handlers.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } name)
                        {
                            names.Add(name);
                        }
                    }
                }
            }
            catch (JsonException)
            {
            }

            return names;
        }

        private static string? String(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}

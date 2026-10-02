using System;
using System.Collections.Generic;
using System.Text.Json;
using Kubuno.Desktop.Designer.Editing;

namespace Kubuno.Desktop.Designer.Handlers
{
    /// <summary>
    /// Pure JSON -&gt; <see cref="CreateHandlerResponse"/> parsing, deliberately decoupled from the RPC
    /// transport that produced the string - the same "parse a JSON string, do not care where it came
    /// from" shape <see cref="Registry.ComponentRegistry.FromJson"/> already uses for
    /// <c>kubuno/registry</c>. <see cref="Infrastructure.JsonRpcKubunoViewsLanguageServerClient"/> is the
    /// one (untested, StreamJsonRpc-dependent) caller.
    ///
    /// Walks the parsed <see cref="JsonDocument"/> field by field rather than calling
    /// <c>JsonSerializer.Deserialize&lt;T&gt;</c> against <see cref="LspPosition"/>/<see cref="LspRange"/>/
    /// <see cref="Editing.TextEditDto"/> directly - those are `readonly struct`s with a single custom
    /// constructor and get-only properties, and for a STRUCT (unlike a class) `System.Text.Json` prefers
    /// the compiler-synthesized public PARAMETERLESS constructor over the custom one whenever both exist
    /// (every struct always has an implicit one), silently producing an all-default `{Line = 0, Character
    /// = 0}` instead of binding through the constructor or throwing - caught empirically by this class's
    /// own unit tests (`Parse_LocationOnlyResponse_ReadsUriAndRange`/`Parse_EditResponse_...` both failed
    /// with `0` instead of the JSON's real numbers before this fix). No other code in this codebase
    /// deserializes JSON into these two struct types either (`Editing/*Tests.cs` only ever construct them
    /// with `new LspPosition(...)`/`new LspRange(...)` from already-parsed values) - this class is the
    /// first, and needed to discover this the hard way rather than assume it away.
    /// </summary>
    public static class CreateHandlerResponseParser
    {
        /// <summary>
        /// <see langword="null"/> for an empty/malformed payload rather than throwing - the same
        /// "degrade to nothing usable" contract every other bridge in this codebase gives a caller that
        /// cannot use its result (docs/DESIGNER.md's own "an id that does not resolve is never an
        /// error... degrades to no-op" rule, mirrored here one layer up).
        /// </summary>
        public static CreateHandlerResponse? Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(json!);
                return ParseResponse(document.RootElement);
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                return null;
            }
        }

        private static CreateHandlerResponse ParseResponse(JsonElement root)
        {
            var handlerName = root.GetProperty("handlerName").GetString() ?? string.Empty;
            var location = TryGetNonNull(root, "location", out var locationElement) ? ParseLocation(locationElement) : null;
            var edit = TryGetNonNull(root, "edit", out var editElement) ? ParseEdit(editElement) : null;
            return new CreateHandlerResponse(handlerName, location, edit);
        }

        private static HandlerLocation ParseLocation(JsonElement element)
        {
            var uri = element.GetProperty("uri").GetString() ?? string.Empty;
            return new HandlerLocation(uri, ParseRange(element.GetProperty("range")));
        }

        internal static HandlerWorkspaceEdit ParseEdit(JsonElement element)
        {
            Dictionary<string, IReadOnlyList<TextEditDto>>? changes = null;
            if (TryGetNonNull(element, "changes", out var changesElement))
            {
                changes = new Dictionary<string, IReadOnlyList<TextEditDto>>();
                foreach (var fileProperty in changesElement.EnumerateObject())
                {
                    var edits = new List<TextEditDto>();
                    foreach (var editElement in fileProperty.Value.EnumerateArray())
                    {
                        edits.Add(ParseTextEdit(editElement));
                    }

                    changes[fileProperty.Name] = edits;
                }
            }

            return new HandlerWorkspaceEdit(changes);
        }

        private static TextEditDto ParseTextEdit(JsonElement element)
        {
            var range = ParseRange(element.GetProperty("range"));
            var newText = element.GetProperty("newText").GetString() ?? string.Empty;
            return new TextEditDto(range, newText);
        }

        private static LspRange ParseRange(JsonElement element)
        {
            var start = ParsePosition(element.GetProperty("start"));
            var end = ParsePosition(element.GetProperty("end"));
            return new LspRange(start, end);
        }

        private static LspPosition ParsePosition(JsonElement element)
        {
            var line = element.GetProperty("line").GetInt32();
            var character = element.GetProperty("character").GetInt32();
            return new LspPosition(line, character);
        }

        internal static bool TryGetNonNull(JsonElement element, string propertyName, out JsonElement value)
        {
            if (element.TryGetProperty(propertyName, out value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                return true;
            }

            value = default;
            return false;
        }
    }
}

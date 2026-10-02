using System;
using System.Collections.Generic;
using System.Text.Json;
using Kubuno.Desktop.Designer.Editing;
using Kubuno.Desktop.Designer.Outline;

namespace Kubuno.Desktop.Designer.Selection
{
    /// <summary>
    /// Pure JSON -&gt; DTO parsing for the three methods <see cref="IViewsSelectionLanguageServerClient"/>
    /// exposes, deliberately decoupled from the RPC transport that produced the string - the same shape
    /// <see cref="Handlers.CreateHandlerResponseParser"/> already uses (that class's own doc comment
    /// explains why: <c>Infrastructure.JsonRpcViewsSelectionLanguageServerClient</c> is the one
    /// (untested, StreamJsonRpc-dependent) caller).
    ///
    /// Walks each parsed <see cref="JsonElement"/> field by field rather than
    /// <c>JsonSerializer.Deserialize&lt;LspPosition&gt;</c>/<c>&lt;LspRange&gt;</c> - see
    /// <see cref="Handlers.CreateHandlerResponseParser"/>'s own doc comment for exactly why (those two
    /// types are `readonly struct`s with a custom constructor `System.Text.Json` silently bypasses in
    /// favor of the implicit parameterless one, discovered empirically by that class's own unit tests).
    /// </summary>
    public static class SelectionResponseParser
    {
        /// <summary><see langword="null"/> for an empty/malformed payload OR a JSON <c>null</c> literal (the server's own "nothing to select" result) - the same "degrade to nothing usable" rule <see cref="Handlers.CreateHandlerResponseParser.Parse"/> already documents.</summary>
        public static ElementAtOffsetResponse? ParseElementAtOffset(string? json)
        {
            return ParseOrNullClass<ElementAtOffsetResponse>(json, root =>
            {
                var elementId = root.GetProperty("elementId").GetString();
                if (string.IsNullOrEmpty(elementId))
                {
                    return null;
                }

                return new ElementAtOffsetResponse(elementId!, ParseRange(root.GetProperty("range")));
            });
        }

        /// <summary>See <see cref="ParseElementAtOffset"/>'s own doc for the null-handling contract.</summary>
        public static LspRange? ParseRangeOfElement(string? json)
        {
            return ParseOrNullStruct<LspRange>(json, root => ParseRange(root.GetProperty("range")));
        }

        /// <summary>
        /// The full <c>textDocument/documentSymbol</c> result - a JSON array at the top level (the
        /// standard LSP shape), not a single object. <see langword="null"/> for the same reasons
        /// <see cref="ParseElementAtOffset"/> documents; an EMPTY (but non-null) list is a valid, distinct
        /// result (a document with no root element - <c>symbols.rs</c>'s own doc: "None when the document
        /// has no root element at all").
        /// </summary>
        public static IReadOnlyList<DocumentSymbolDto>? ParseDocumentSymbols(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(json!);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var result = new List<DocumentSymbolDto>();
                foreach (var element in root.EnumerateArray())
                {
                    result.Add(ParseDocumentSymbol(element));
                }

                return result;
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                return null;
            }
        }

        private static DocumentSymbolDto ParseDocumentSymbol(JsonElement element)
        {
            var name = element.GetProperty("name").GetString() ?? string.Empty;
            var detail = element.TryGetProperty("detail", out var detailProp) && detailProp.ValueKind == JsonValueKind.String
                ? detailProp.GetString()
                : null;
            var range = ParseRange(element.GetProperty("range"));

            var children = new List<DocumentSymbolDto>();
            if (element.TryGetProperty("children", out var childrenProp) && childrenProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in childrenProp.EnumerateArray())
                {
                    children.Add(ParseDocumentSymbol(child));
                }
            }

            return new DocumentSymbolDto(name, detail, range, children);
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

        private static T? ParseOrNullClass<T>(string? json, Func<JsonElement, T?> parse) where T : class
        {
            if (string.IsNullOrWhiteSpace(json) || json == "null")
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(json!);
                if (document.RootElement.ValueKind == JsonValueKind.Null)
                {
                    return null;
                }

                return parse(document.RootElement);
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                return null;
            }
        }

        private static T? ParseOrNullStruct<T>(string? json, Func<JsonElement, T?> parse) where T : struct
        {
            if (string.IsNullOrWhiteSpace(json) || json == "null")
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(json!);
                if (document.RootElement.ValueKind == JsonValueKind.Null)
                {
                    return null;
                }

                return parse(document.RootElement);
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                return null;
            }
        }
    }
}

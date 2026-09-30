using System;
using System.Linq;
using System.Text.Json.Nodes;
using Kubuno.Core.Logic.Lsp;

namespace Kubuno.Rust.Logic.IntelliSense
{
    /// <summary>
    /// What Kubuno changes in the <c>initialize</c> handshake between Visual Studio and rust-analyzer
    /// (applied by <see cref="LspHandshakeStreams"/>), and the settings it starts rust-analyzer with.
    /// </summary>
    public static class RustAnalyzerHandshake
    {
        /// <summary>The client command rust-analyzer attaches to a completed function call to reopen Parameter Info.</summary>
        public const string TriggerParameterHintsCommand = "rust-analyzer.triggerParameterHints";

        /// <summary>The client command rust-analyzer puts on its "N references" code lenses (arguments: uri, position, locations).</summary>
        public const string ShowReferencesCommand = "rust-analyzer.showReferences";

        /// <summary>
        /// Client capabilities Visual Studio does not announce but Kubuno's own editor features handle: completion
        /// snippets and label details (Kubuno's completion source replaces Visual Studio's, which only expands
        /// snippets for an allow-list of Microsoft clients), and the client-side commands rust-analyzer may attach
        /// to a completion (reopen Parameter Info) or a code lens (show references).
        /// </summary>
        public static string? RewriteInitializeRequest(string json)
        {
            if (LspJson.Parse(json) is not JsonObject message || message["params"] is not JsonObject parameters)
            {
                return null;
            }

            var capabilities = LspJson.Ensure(parameters, "capabilities");
            var completionItem = LspJson.Ensure(capabilities, "textDocument", "completion", "completionItem");
            completionItem["snippetSupport"] = true;
            completionItem["labelDetailsSupport"] = true;

            // Only the import is worth resolving lazily: Visual Studio also lists textEdit, insertText and command, which
            // rust-analyzer would then leave out of every item until each one is resolved.
            completionItem["resolveSupport"] = new JsonObject { ["properties"] = new JsonArray("additionalTextEdits") };
            LspJson.Ensure(capabilities, "textDocument", "completion")["contextSupport"] = true;

            // Kubuno's completion reads each item's own textEdit; rust-analyzer would otherwise move the edit range to
            // the list's itemDefaults (Visual Studio announces that support).
            if (capabilities["textDocument"]?["completion"]?["completionList"] is JsonObject completionList)
            {
                completionList.Remove("itemDefaults");
            }

            var experimental = LspJson.Ensure(capabilities, "experimental");
            experimental["commands"] = new JsonObject
            {
                ["commands"] = new JsonArray(TriggerParameterHintsCommand, ShowReferencesCommand),
            };

            return message.ToJsonString();
        }

        /// <summary>
        /// Replaces rust-analyzer's semantic token legend by <see cref="RustSemanticTokenMap"/>'s (reported through
        /// <paramref name="onMap"/>, for the middle layer that remaps every token response), and hides its completion
        /// capability from Visual Studio.
        /// </summary>
        public static string? RewriteInitializeResponse(string json, Action<RustSemanticTokenMap> onMap)
        {
            if (LspJson.Parse(json) is not JsonObject message || message["result"]?["capabilities"] is not JsonObject capabilities)
            {
                return null;
            }

            // Kubuno's completion source asks rust-analyzer itself; without the capability, Visual Studio's generic LSP
            // completion source stays out of Rust sessions (it would otherwise also define the text being completed).
            capabilities.Remove("completionProvider");

            if (capabilities["semanticTokensProvider"] is not JsonObject provider
                || provider["legend"] is not JsonObject legend
                || legend["tokenTypes"] is not JsonArray types)
            {
                return message.ToJsonString();
            }

            var modifiers = legend["tokenModifiers"] as JsonArray;
            var map = new RustSemanticTokenMap(
                types.Select(t => (string?)t ?? string.Empty).ToList(),
                modifiers?.Select(m => (string?)m ?? string.Empty).ToList() ?? new System.Collections.Generic.List<string>());
            legend["tokenTypes"] = new JsonArray(map.Legend.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
            legend["tokenModifiers"] = new JsonArray(RustSemanticTokenMap.Modifiers.Select(m => (JsonNode?)JsonValue.Create(m)).ToArray());
            onMap(map);
            return message.ToJsonString();
        }

        /// <summary>
        /// rust-analyzer's settings (its <c>initializationOptions</c>, the same keys as the <c>rust-analyzer.*</c>
        /// settings of other editors): reference and implementation counts on the code lenses Kubuno draws, no
        /// Run/Debug lenses (Visual Studio has its own test and launch commands), argument placeholders when a
        /// function is completed, symbol search over functions and methods too, and the inlay hints chosen in the options
        /// (<see cref="RustInlayHintSettings"/>).
        /// </summary>
        public static JsonObject InitializationOptions(RustInlayHintSettings? inlayHints = null) => new JsonObject
        {
            ["lens"] = new JsonObject
            {
                ["enable"] = true,
                ["run"] = new JsonObject { ["enable"] = false },
                ["debug"] = new JsonObject { ["enable"] = false },
                ["updateTest"] = new JsonObject { ["enable"] = false },
                ["implementations"] = new JsonObject { ["enable"] = true },
                ["references"] = new JsonObject
                {
                    ["adt"] = new JsonObject { ["enable"] = true },
                    ["enumVariant"] = new JsonObject { ["enable"] = true },
                    ["method"] = new JsonObject { ["enable"] = true },
                    ["trait"] = new JsonObject { ["enable"] = true },
                },
            },
            ["completion"] = new JsonObject
            {
                ["autoimport"] = new JsonObject { ["enable"] = true },
                ["callable"] = new JsonObject { ["snippets"] = "fill_arguments" },
                ["postfix"] = new JsonObject { ["enable"] = true },
            },
            // Go To All (Ctrl+T) finds functions and methods too, like C#'s (rust-analyzer's default is types only).
            ["workspace"] = new JsonObject
            {
                ["symbol"] = new JsonObject { ["search"] = new JsonObject { ["kind"] = "all_symbols", ["limit"] = 256 } },
            },
            ["inlayHints"] = (inlayHints ?? RustInlayHintSettings.Current).ToRustAnalyzerSettings(),
        };
    }
}

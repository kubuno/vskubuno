using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServer.Client;
using Newtonsoft.Json.Linq;

namespace Kubuno.Desktop.Views.LanguageService
{
    /// <summary>
    /// <see cref="KubunoViewsLanguageClient.MiddleLayer"/>: answers Visual Studio's own
    /// <c>textDocument/hover</c> with nothing, so the LSP client does not draw kubuno-views-ls's markdown
    /// tooltip. The VSIX's QuickInfo source asks the server itself (over <see cref="KubunoViewsLanguageClient.Rpc"/>,
    /// which does not go through this layer) and shows a C#-style tooltip with the registry's localized
    /// documentation instead. When kubuno-views-ls has no definition for a position (a control's tag, a binding
    /// path), <see cref="DefinitionFallback"/> - set by the VSIX, which can reach rust-analyzer - gets a chance to
    /// answer it with the Rust side. Everything else passes through untouched.
    /// </summary>
    public sealed class HoverSuppressionMiddleLayer : ILanguageClientMiddleLayer2<JToken>
    {
        private const string HoverMethod = "textDocument/hover";
        private const string DefinitionMethod = "textDocument/definition";

        /// <summary>
        /// Called with a <c>textDocument/definition</c> request kubuno-views-ls answered with nothing; returns the
        /// locations to use instead (or null to keep the empty answer).
        /// </summary>
        public static Func<JToken, Task<JToken?>>? DefinitionFallback { get; set; }

        public bool CanHandle(string methodName) => methodName == HoverMethod || methodName == DefinitionMethod;

        public Task HandleNotificationAsync(string methodName, JToken methodParam, Func<JToken, Task> sendNotification) =>
            sendNotification(methodParam);

        public async Task<JToken?> HandleRequestAsync(string methodName, JToken methodParam, Func<JToken, Task<JToken?>> sendRequest)
        {
            if (methodName == HoverMethod)
            {
                return null;
            }

            var response = await sendRequest(methodParam).ConfigureAwait(false);
            if (methodName == DefinitionMethod && IsEmpty(response) && DefinitionFallback is { } fallback)
            {
                return await fallback(methodParam).ConfigureAwait(false) ?? response;
            }

            return response;
        }

        private static bool IsEmpty(JToken? response) =>
            response is null || response.Type == JTokenType.Null || (response is JArray array && array.Count == 0);
    }
}

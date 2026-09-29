using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServer.Client;
using Newtonsoft.Json.Linq;

namespace Kubuno.VisualStudio.Views.LanguageService
{
    /// <summary>
    /// <see cref="KubunoViewsLanguageClient.MiddleLayer"/>: answers Visual Studio's own
    /// <c>textDocument/hover</c> with nothing, so the LSP client does not draw kubuno-views-ls's markdown
    /// tooltip. The VSIX's QuickInfo source asks the server itself (over <see cref="KubunoViewsLanguageClient.Rpc"/>,
    /// which does not go through this layer) and shows a C#-style tooltip with the registry's localized
    /// documentation instead. Everything else passes through untouched.
    /// </summary>
    public sealed class HoverSuppressionMiddleLayer : ILanguageClientMiddleLayer2<JToken>
    {
        private const string HoverMethod = "textDocument/hover";

        public bool CanHandle(string methodName) => methodName == HoverMethod;

        public Task HandleNotificationAsync(string methodName, JToken methodParam, Func<JToken, Task> sendNotification) =>
            sendNotification(methodParam);

        public Task<JToken?> HandleRequestAsync(string methodName, JToken methodParam, Func<JToken, Task<JToken?>> sendRequest) =>
            methodName == HoverMethod ? Task.FromResult<JToken?>(null) : sendRequest(methodParam);
    }
}

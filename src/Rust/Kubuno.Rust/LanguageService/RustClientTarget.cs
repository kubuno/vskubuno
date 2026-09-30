using System.Linq;
using Kubuno.Rust.Logic.IntelliSense;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.Rust.LanguageService
{
    /// <summary>
    /// The server-to-client requests rust-analyzer sends that Visual Studio's LSP client does not answer itself. After a
    /// <c>workspace/didChangeConfiguration</c> rust-analyzer ignores the notification's payload and asks for its settings
    /// with <c>workspace/configuration</c> (section "rust-analyzer"); without an answer the change is dropped.
    /// </summary>
    internal sealed class RustClientTarget
    {
        /// <summary>rust-analyzer's current settings (the same object as its <c>initializationOptions</c>).</summary>
        [JsonRpcMethod("workspace/configuration", UseSingleObjectParameterDeserialization = true)]
        public JToken[] Configuration(JToken parameters)
        {
            var items = parameters["items"] as JArray ?? new JArray();
            return items.Select<JToken, JToken>(item =>
            {
                string? section = (string?)item["section"];
                return section is null || section == "rust-analyzer"
                    ? (JToken)JObject.Parse(RustAnalyzerHandshake.InitializationOptions().ToJsonString())
                    : JValue.CreateNull();
            }).ToArray();
        }
    }
}

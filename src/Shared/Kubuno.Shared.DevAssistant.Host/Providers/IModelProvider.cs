using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Shared.DevAssistant.Logic.Protocol;

namespace Kubuno.Shared.DevAssistant.Host.Providers
{
    /// <summary>
    /// A model provider (docs/AI-ASSISTANT.md section 9.3). Implementations: <see cref="AnthropicProvider"/> (the
    /// official SDK) and <see cref="FakeProvider"/> (recorded responses, offline). Provider-specific features (caching
    /// breakpoints, thinking, effort) stay inside the provider; the loop only sees normalised events.
    /// </summary>
    public interface IModelProvider
    {
        /// <summary>The provider id (<see cref="ProviderIds"/>).</summary>
        string Id { get; }

        /// <summary>The endpoint host shown in the destination badge.</summary>
        string Endpoint { get; }

        /// <summary>True when nothing leaves the machine.</summary>
        bool IsLocal { get; }

        Task<ModelsListResult> ListModelsAsync(CancellationToken cancellationToken);

        /// <summary>Streams one model call. Ends with a <see cref="ProviderEventKind.Stop"/> event.</summary>
        IAsyncEnumerable<ProviderEvent> StreamAsync(ProviderRequest request, CancellationToken cancellationToken);
    }

    /// <summary>One model call, provider-neutral.</summary>
    public sealed class ProviderRequest
    {
        public string Model { get; set; } = string.Empty;

        public string Effort { get; set; } = "medium";

        public int MaxTokens { get; set; } = 32000;

        public IReadOnlyList<SystemPart> System { get; set; } = new List<SystemPart>();

        public IReadOnlyList<ToolDescriptor> Tools { get; set; } = new List<ToolDescriptor>();

        public IReadOnlyList<ChatTurn> Messages { get; set; } = new List<ChatTurn>();
    }

    public enum ProviderEventKind
    {
        /// <summary>The exact request body about to be sent (<see cref="ProviderEvent.Text"/>).</summary>
        RequestBody,
        TextDelta,
        ThinkingDelta,

        /// <summary>A complete content block (<see cref="ProviderEvent.Block"/>), in response order.</summary>
        BlockCompleted,

        /// <summary>The model started a tool use (name known, input still streaming).</summary>
        ToolUseStarted,
        Usage,

        /// <summary>The call ended (<see cref="ProviderEvent.Text"/> = the stop reason: end_turn, tool_use, max_tokens, refusal...).</summary>
        Stop,
    }

    public sealed class ProviderEvent
    {
        private ProviderEvent(ProviderEventKind kind)
        {
            Kind = kind;
        }

        public ProviderEventKind Kind { get; }

        public string Text { get; private set; } = string.Empty;

        public ChatBlock? Block { get; private set; }

        public UsageInfo? Usage { get; private set; }

        public static ProviderEvent RequestBody(string body) => new ProviderEvent(ProviderEventKind.RequestBody) { Text = body };

        public static ProviderEvent TextDelta(string text) => new ProviderEvent(ProviderEventKind.TextDelta) { Text = text };

        public static ProviderEvent ThinkingDelta(string text) => new ProviderEvent(ProviderEventKind.ThinkingDelta) { Text = text };

        public static ProviderEvent Completed(ChatBlock block) => new ProviderEvent(ProviderEventKind.BlockCompleted) { Block = block };

        public static ProviderEvent ToolUseStarted(ChatBlock block) => new ProviderEvent(ProviderEventKind.ToolUseStarted) { Block = block };

        public static ProviderEvent UsageReport(UsageInfo usage) => new ProviderEvent(ProviderEventKind.Usage) { Usage = usage };

        public static ProviderEvent Stop(string reason) => new ProviderEvent(ProviderEventKind.Stop) { Text = reason };
    }

    /// <summary>A provider failure the loop reports to the developer as is (no key, HTTP error...).</summary>
    public sealed class ProviderException : System.Exception
    {
        public ProviderException(string message, System.Exception? inner = null)
            : base(message, inner)
        {
        }
    }
}

using System.Collections.Generic;
using System.Text.Json;

namespace Kubuno.Shared.DevAssistant.Logic.Protocol
{
    /// <summary>
    /// The method names of the channel (docs/AI-ASSISTANT.md section 9.1). VSIX → host requests: <see cref="Initialize"/>,
    /// <see cref="ModelsList"/>, <see cref="SessionSend"/>, <see cref="SessionCancel"/>, <see cref="Shutdown"/>.
    /// Host → VSIX: the <see cref="ToolInvoke"/> request and the stream notifications.
    /// </summary>
    public static class DevAssistantMethods
    {
        /// <summary>Version of this contract; the VSIX refuses a host answering another one.</summary>
        public const int ProtocolVersion = 1;

        public const string Initialize = "initialize";
        public const string ModelsList = "models/list";
        public const string SessionSend = "session/send";
        public const string SessionCancel = "session/cancel";
        public const string Shutdown = "shutdown";

        public const string ToolInvoke = "tool/invoke";

        public const string StreamDelta = "stream/delta";
        public const string StreamToolCall = "stream/toolCall";
        public const string Usage = "usage";
        public const string RequestSent = "request/sent";

        /// <summary>Either side: cancels one of its own pending requests (sent by <see cref="RpcConnection"/>).</summary>
        public const string CancelRequest = "$/cancelRequest";
    }

    /// <summary>The providers the host knows. The VSIX never names an endpoint: the host maps the id.</summary>
    public static class ProviderIds
    {
        /// <summary>Anthropic Claude (api.anthropic.com), the official C# SDK.</summary>
        public const string Anthropic = "anthropic";

        /// <summary>The offline test provider replaying recorded streaming responses (no network, no key).</summary>
        public const string Fake = "fake";

        /// <summary>The Credential Manager target holding a provider's API key (CRED_PERSIST_LOCAL_MACHINE).</summary>
        public static string CredentialTarget(string providerId) => "Kubuno:DevAssistant:Provider:" + providerId;
    }

    public sealed class CancelRequestParams
    {
        public long Id { get; set; }
    }

    public sealed class InitializeParams
    {
        public int ProtocolVersion { get; set; } = DevAssistantMethods.ProtocolVersion;

        /// <summary>Folder holding the fake provider's recorded responses (tests and offline checks); null for the bundled one.</summary>
        public string? FixturesDirectory { get; set; }
    }

    public sealed class InitializeResult
    {
        public int ProtocolVersion { get; set; }

        public string HostVersion { get; set; } = string.Empty;
    }

    public sealed class ModelsListParams
    {
        public string Provider { get; set; } = ProviderIds.Anthropic;
    }

    public sealed class ModelInfo
    {
        public string Id { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public long? MaxInputTokens { get; set; }

        public long? MaxOutputTokens { get; set; }
    }

    public sealed class ModelsListResult
    {
        public List<ModelInfo> Models { get; set; } = new List<ModelInfo>();

        /// <summary>True when the list came from the provider's Models API, false for the offline fallback.</summary>
        public bool FromProvider { get; set; }

        /// <summary>Why the provider's list could not be read (no key, offline...), or null.</summary>
        public string? Error { get; set; }

        /// <summary>The endpoint host shown in the destination badge (« Cloud : api.anthropic.com »).</summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>True when nothing leaves the machine (fake or local provider).</summary>
        public bool IsLocal { get; set; }
    }

    /// <summary>The approval class of a tool (docs/AI-ASSISTANT.md section 5.6).</summary>
    public enum ApprovalClass
    {
        Read,
        Write,
        Execute,
        Forbidden,
    }

    /// <summary>An MCP-shaped tool descriptor the VSIX sends with each request.</summary>
    public sealed class ToolDescriptor
    {
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        /// <summary>JSON Schema of the input (an object schema).</summary>
        public JsonElement InputSchema { get; set; }

        public ApprovalClass ApprovalClass { get; set; } = ApprovalClass.Read;
    }

    /// <summary>A part of the system prompt; <see cref="CacheBreakpoint"/> marks the end of a cached prefix.</summary>
    public sealed class SystemPart
    {
        public string Text { get; set; } = string.Empty;

        public bool CacheBreakpoint { get; set; }
    }

    /// <summary>Kinds of <see cref="ChatBlock"/>.</summary>
    public static class ChatBlockTypes
    {
        public const string Text = "text";
        public const string Thinking = "thinking";
        public const string RedactedThinking = "redacted_thinking";
        public const string ToolUse = "tool_use";
        public const string ToolResult = "tool_result";
    }

    /// <summary>
    /// One content block of a turn, provider-neutral but lossless for the provider-native parts: thinking blocks keep
    /// their signature and tool uses their ids as received, so the history replays exactly (append-only, section 2.1).
    /// </summary>
    public sealed class ChatBlock
    {
        public string Type { get; set; } = ChatBlockTypes.Text;

        public string? Text { get; set; }

        /// <summary>Thinking signature, or the opaque data of a redacted thinking block.</summary>
        public string? Signature { get; set; }

        public string? ToolUseId { get; set; }

        public string? ToolName { get; set; }

        /// <summary>A tool use's input object.</summary>
        public JsonElement? Input { get; set; }

        public bool IsError { get; set; }

        public static ChatBlock FromText(string text) => new ChatBlock { Type = ChatBlockTypes.Text, Text = text };

        public static ChatBlock ToolResultFor(string toolUseId, string content, bool isError) =>
            new ChatBlock { Type = ChatBlockTypes.ToolResult, ToolUseId = toolUseId, Text = content, IsError = isError };
    }

    public static class ChatRoles
    {
        public const string User = "user";
        public const string Assistant = "assistant";
    }

    /// <summary>One message of the conversation.</summary>
    public sealed class ChatTurn
    {
        public string Role { get; set; } = ChatRoles.User;

        public List<ChatBlock> Blocks { get; set; } = new List<ChatBlock>();
    }

    /// <summary>Token usage of one model call (or a sum of them).</summary>
    public sealed class UsageInfo
    {
        public long InputTokens { get; set; }

        public long OutputTokens { get; set; }

        public long CacheReadInputTokens { get; set; }

        public long CacheCreationInputTokens { get; set; }

        public void Add(UsageInfo other)
        {
            InputTokens += other.InputTokens;
            OutputTokens += other.OutputTokens;
            CacheReadInputTokens += other.CacheReadInputTokens;
            CacheCreationInputTokens += other.CacheCreationInputTokens;
        }
    }

    /// <summary>
    /// <see cref="DevAssistantMethods.SessionSend"/>: runs one user turn to completion (model calls and tool rounds).
    /// The VSIX owns the conversation and sends the whole (masked) history; the host returns the turns it added.
    /// </summary>
    public sealed class SessionSendParams
    {
        public string SessionId { get; set; } = string.Empty;

        public string Provider { get; set; } = ProviderIds.Anthropic;

        public string Model { get; set; } = string.Empty;

        /// <summary>low, medium or high.</summary>
        public string Effort { get; set; } = "medium";

        public int MaxTokens { get; set; } = 32000;

        public List<SystemPart> System { get; set; } = new List<SystemPart>();

        public List<ToolDescriptor> Tools { get; set; } = new List<ToolDescriptor>();

        /// <summary>The conversation so far, its last turn being the new user message.</summary>
        public List<ChatTurn> History { get; set; } = new List<ChatTurn>();

        /// <summary>Hard cost cap of the session in US dollars (section 7.3).</summary>
        public decimal CostCapUsd { get; set; } = 5m;

        /// <summary>What the session already spent before this turn.</summary>
        public decimal CostSoFarUsd { get; set; }

        /// <summary>Upper bound on model calls in this turn.</summary>
        public int MaxRounds { get; set; } = 12;
    }

    /// <summary>Why a <see cref="DevAssistantMethods.SessionSend"/> stopped.</summary>
    public static class StopReasons
    {
        public const string EndTurn = "end_turn";
        public const string Cancelled = "cancelled";
        public const string CostCap = "cost_cap";
        public const string MaxRounds = "max_rounds";
        public const string MaxTokens = "max_tokens";
        public const string Refusal = "refusal";
        public const string Error = "error";
    }

    public sealed class SessionSendResult
    {
        /// <summary>The turns the host appended after the user's message (assistant turns and tool results), in order.</summary>
        public List<ChatTurn> NewTurns { get; set; } = new List<ChatTurn>();

        public string StopReason { get; set; } = StopReasons.EndTurn;

        public string? Error { get; set; }

        public UsageInfo Usage { get; set; } = new UsageInfo();

        public decimal CostUsd { get; set; }
    }

    public sealed class SessionCancelParams
    {
        public string SessionId { get; set; } = string.Empty;
    }

    /// <summary>Host → VSIX: streamed text or reasoning summary.</summary>
    public sealed class StreamDeltaParams
    {
        public string SessionId { get; set; } = string.Empty;

        /// <summary><see cref="ChatBlockTypes.Text"/> or <see cref="ChatBlockTypes.Thinking"/>.</summary>
        public string Kind { get; set; } = ChatBlockTypes.Text;

        public string Text { get; set; } = string.Empty;

        /// <summary>Model call index within the turn (a new call starts a new assistant message).</summary>
        public int Round { get; set; }
    }

    /// <summary>Host → VSIX: the model asked for a tool (shown as a card before the tool runs).</summary>
    public sealed class StreamToolCallParams
    {
        public string SessionId { get; set; } = string.Empty;

        public string ToolUseId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public JsonElement? Input { get; set; }

        public int Round { get; set; }
    }

    /// <summary>Host → VSIX: usage and cost after each model call.</summary>
    public sealed class UsageParams
    {
        public string SessionId { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;

        public UsageInfo Usage { get; set; } = new UsageInfo();

        public decimal CostUsd { get; set; }

        public decimal SessionCostUsd { get; set; }
    }

    /// <summary>Host → VSIX: the exact request body sent to the provider (masked text only), for « Voir la requête ».</summary>
    public sealed class RequestSentParams
    {
        public string SessionId { get; set; } = string.Empty;

        public int Round { get; set; }

        public string Endpoint { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;
    }

    /// <summary>Host → VSIX request: run a tool the model asked for.</summary>
    public sealed class ToolInvokeParams
    {
        public string SessionId { get; set; } = string.Empty;

        public string ToolUseId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public JsonElement Input { get; set; }
    }

    public sealed class ToolInvokeResult
    {
        /// <summary>The (masked) result text sent back to the model.</summary>
        public string Content { get; set; } = string.Empty;

        public bool IsError { get; set; }
    }
}

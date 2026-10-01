using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Kubuno.Core.DevAssistant.Logic.Credentials;
using Kubuno.Core.DevAssistant.Logic.Protocol;
using ChatBlock = Kubuno.Core.DevAssistant.Logic.Protocol.ChatBlock;
using ModelInfo = Kubuno.Core.DevAssistant.Logic.Protocol.ModelInfo;

namespace Kubuno.Core.DevAssistant.Host.Providers
{
    /// <summary>
    /// Anthropic Claude through the official C# SDK (docs/AI-ASSISTANT.md section 2.1): streaming Messages API with
    /// client tools, adaptive thinking with a summarized display, explicit effort, prompt caching (breakpoints on the
    /// system parts that ask for one, automatic caching of the conversation), and the Models API for the picker. The API
    /// key is read here, from the Windows Credential Manager, each time a client is built: it never crosses the channel.
    /// The exact request body is captured from the HTTP layer for « Voir la requête » (the key travels in a header,
    /// never in the body).
    /// </summary>
    public sealed class AnthropicProvider : IModelProvider
    {
        /// <summary>Offline fallback of the model picker (section 2.1): only used when the Models API cannot be read.</summary>
        internal static readonly IReadOnlyList<ModelInfo> FallbackModels = new[]
        {
            new ModelInfo { Id = "claude-opus-5-5", DisplayName = "Claude Opus 5.5", MaxInputTokens = 1_000_000, MaxOutputTokens = 128_000 },
            new ModelInfo { Id = "claude-sonnet-5-5", DisplayName = "Claude Sonnet 5.5", MaxInputTokens = 1_000_000, MaxOutputTokens = 128_000 },
            new ModelInfo { Id = "claude-haiku-4-5", DisplayName = "Claude Haiku 4.5", MaxInputTokens = 200_000, MaxOutputTokens = 64_000 },
        };

        private static readonly AsyncLocal<StringBuilder?> CapturedBody = new AsyncLocal<StringBuilder?>();

        private readonly Func<string?> _readKey;
        private readonly string? _baseUrl;

        /// <param name="readKey">Reads the API key (default: the Credential Manager entry of this provider).</param>
        /// <param name="baseUrl">Overrides the endpoint (tests against a local recorded-SSE server); null for api.anthropic.com.</param>
        public AnthropicProvider(Func<string?>? readKey = null, string? baseUrl = null)
        {
            _readKey = readKey ?? (() => WindowsCredentialStore.Read(ProviderIds.CredentialTarget(ProviderIds.Anthropic)));
            _baseUrl = baseUrl;
        }

        public string Id => ProviderIds.Anthropic;

        public string Endpoint => _baseUrl is null ? "api.anthropic.com" : new Uri(_baseUrl).Authority;

        public bool IsLocal => false;

        public async Task<ModelsListResult> ListModelsAsync(CancellationToken cancellationToken)
        {
            var result = new ModelsListResult { Endpoint = Endpoint, IsLocal = false };
            try
            {
                var client = CreateClient();
                var page = await client.Models.List(cancellationToken: cancellationToken).ConfigureAwait(false);
                foreach (var model in page.Items)
                {
                    result.Models.Add(new ModelInfo
                    {
                        Id = model.ID,
                        DisplayName = string.IsNullOrEmpty(model.DisplayName) ? model.ID : model.DisplayName,
                        MaxInputTokens = model.MaxInputTokens,
                        MaxOutputTokens = model.MaxTokens,
                    });
                }

                result.FromProvider = result.Models.Count > 0;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result.Error = Describe(exception);
            }

            if (result.Models.Count == 0)
            {
                result.Models.AddRange(FallbackModels);
            }

            return result;
        }

        public async IAsyncEnumerable<ProviderEvent> StreamAsync(ProviderRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var client = CreateClient();
            var parameters = BuildParameters(request);
            var body = new StringBuilder();
            CapturedBody.Value = body;

            var blocks = new Dictionary<long, BlockBuilder>();
            var usage = new UsageInfo();
            string stopReason = StopReasons.EndTurn;
            bool bodyReported = false;

            await using var stream = client.Messages.CreateStreaming(parameters, cancellationToken).GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                RawMessageStreamEvent streamEvent;
                try
                {
                    if (!await stream.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    streamEvent = stream.Current;
                }
                catch (Exception exception) when (exception is not OperationCanceledException && exception is not ProviderException)
                {
                    throw new ProviderException(Describe(exception), exception);
                }

                if (!bodyReported)
                {
                    bodyReported = true;
                    yield return ProviderEvent.RequestBody(body.ToString());
                }

                if (streamEvent.TryPickStart(out var start))
                {
                    var startUsage = start.Message.Usage;
                    usage.InputTokens = startUsage.InputTokens;
                    usage.CacheReadInputTokens = startUsage.CacheReadInputTokens ?? 0;
                    usage.CacheCreationInputTokens = startUsage.CacheCreationInputTokens ?? 0;
                    usage.OutputTokens = startUsage.OutputTokens;
                }
                else if (streamEvent.TryPickContentBlockStart(out var blockStart))
                {
                    var builder = new BlockBuilder();
                    var content = blockStart.ContentBlock;
                    if (content.TryPickText(out var text))
                    {
                        builder.Type = ChatBlockTypes.Text;
                        builder.Text.Append(text.Text);
                    }
                    else if (content.TryPickThinking(out var thinking))
                    {
                        builder.Type = ChatBlockTypes.Thinking;
                        builder.Text.Append(thinking.Thinking);
                        builder.Signature = thinking.Signature;
                    }
                    else if (content.TryPickRedactedThinking(out var redacted))
                    {
                        builder.Type = ChatBlockTypes.RedactedThinking;
                        builder.Signature = redacted.Data;
                    }
                    else if (content.TryPickToolUse(out var toolUse))
                    {
                        builder.Type = ChatBlockTypes.ToolUse;
                        builder.ToolUseId = toolUse.ID;
                        builder.ToolName = toolUse.Name;
                        yield return ProviderEvent.ToolUseStarted(new ChatBlock { Type = ChatBlockTypes.ToolUse, ToolUseId = toolUse.ID, ToolName = toolUse.Name });
                    }
                    else
                    {
                        builder.Type = "unsupported";
                    }

                    blocks[blockStart.Index] = builder;
                }
                else if (streamEvent.TryPickContentBlockDelta(out var blockDelta))
                {
                    if (!blocks.TryGetValue(blockDelta.Index, out var builder))
                    {
                        continue;
                    }

                    var delta = blockDelta.Delta;
                    if (delta.TryPickText(out var text))
                    {
                        builder.Text.Append(text.Text);
                        yield return ProviderEvent.TextDelta(text.Text);
                    }
                    else if (delta.TryPickThinking(out var thinking))
                    {
                        builder.Text.Append(thinking.Thinking);
                        yield return ProviderEvent.ThinkingDelta(thinking.Thinking);
                    }
                    else if (delta.TryPickSignature(out var signature))
                    {
                        builder.Signature = signature.Signature;
                    }
                    else if (delta.TryPickInputJson(out var json))
                    {
                        builder.InputJson.Append(json.PartialJson);
                    }
                }
                else if (streamEvent.TryPickContentBlockStop(out var blockStop))
                {
                    if (blocks.TryGetValue(blockStop.Index, out var builder) && builder.ToBlock() is { } block)
                    {
                        yield return ProviderEvent.Completed(block);
                    }
                }
                else if (streamEvent.TryPickDelta(out var messageDelta))
                {
                    if (messageDelta.Delta.StopReason is { } reason)
                    {
                        stopReason = StopReasonText(reason);
                    }

                    var deltaUsage = messageDelta.Usage;
                    usage.OutputTokens = deltaUsage.OutputTokens;
                    if (deltaUsage.InputTokens is { } input)
                    {
                        usage.InputTokens = input;
                    }

                    if (deltaUsage.CacheReadInputTokens is { } cacheRead)
                    {
                        usage.CacheReadInputTokens = cacheRead;
                    }

                    if (deltaUsage.CacheCreationInputTokens is { } cacheCreation)
                    {
                        usage.CacheCreationInputTokens = cacheCreation;
                    }
                }
            }

            if (!bodyReported)
            {
                yield return ProviderEvent.RequestBody(body.ToString());
            }

            yield return ProviderEvent.UsageReport(usage);
            yield return ProviderEvent.Stop(stopReason);
        }

        /// <summary>The SDK parameters of one call (internal for the tests).</summary>
        internal static MessageCreateParams BuildParameters(ProviderRequest request)
        {
            var system = request.System
                .Where(part => part.Text.Length > 0)
                .Select(part => new TextBlockParam { Text = part.Text, CacheControl = part.CacheBreakpoint ? new CacheControlEphemeral() : null })
                .ToList();

            var tools = request.Tools
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => (ToolUnion)new Tool
                {
                    Name = t.Name,
                    Description = t.Description,
                    InputSchema = ToInputSchema(t.InputSchema),
                    EagerInputStreaming = true,
                })
                .ToList();

            var messages = request.Messages.Select(ToMessageParam).ToList();
            var isHaiku = request.Model.StartsWith("claude-haiku", StringComparison.Ordinal);
            var parameters = new MessageCreateParams
            {
                Model = request.Model,
                MaxTokens = request.MaxTokens,
                Messages = messages,
                System = system,
                Tools = tools.Count > 0 ? tools : null,
                // Automatic caching on the last message: the conversation prefix is reused turn after turn (section 9.4).
                CacheControl = new CacheControlEphemeral(),
            };

            if (!isHaiku)
            {
                // Thinking cannot be disabled on Opus 5.5; depth is set with effort, always explicitly (default medium).
                parameters = parameters with
                {
                    Thinking = new ThinkingConfigAdaptive { Display = Display.Summarized },
                    OutputConfig = new OutputConfig { Effort = ToEffort(request.Effort) },
                };
            }

            return parameters;
        }

        private static Effort ToEffort(string effort) => effort switch
        {
            "low" => Effort.Low,
            "high" => Effort.High,
            _ => Effort.Medium,
        };

        private static InputSchema ToInputSchema(JsonElement schema)
        {
            var properties = new Dictionary<string, JsonElement>();
            var required = new List<string>();
            if (schema.ValueKind == JsonValueKind.Object)
            {
                if (schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in props.EnumerateObject())
                    {
                        properties[property.Name] = property.Value.Clone();
                    }
                }

                if (schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
                {
                    required.AddRange(req.EnumerateArray().Select(r => r.GetString() ?? string.Empty).Where(r => r.Length > 0));
                }
            }

            return new InputSchema { Properties = properties, Required = required };
        }

        private static MessageParam ToMessageParam(ChatTurn turn)
        {
            var content = new List<ContentBlockParam>();
            foreach (var block in turn.Blocks)
            {
                switch (block.Type)
                {
                    case ChatBlockTypes.Text when !string.IsNullOrEmpty(block.Text):
                        content.Add(new TextBlockParam { Text = block.Text! });
                        break;
                    case ChatBlockTypes.Thinking:
                        content.Add(new ThinkingBlockParam { Thinking = block.Text ?? string.Empty, Signature = block.Signature ?? string.Empty });
                        break;
                    case ChatBlockTypes.RedactedThinking:
                        content.Add(new RedactedThinkingBlockParam { Data = block.Signature ?? string.Empty });
                        break;
                    case ChatBlockTypes.ToolUse:
                        content.Add(new ToolUseBlockParam { ID = block.ToolUseId ?? string.Empty, Name = block.ToolName ?? string.Empty, Input = ToDictionary(block.Input) });
                        break;
                    case ChatBlockTypes.ToolResult:
                        content.Add(new ToolResultBlockParam { ToolUseID = block.ToolUseId ?? string.Empty, Content = block.Text ?? string.Empty, IsError = block.IsError ? true : null });
                        break;
                }
            }

            return new MessageParam { Role = turn.Role == ChatRoles.Assistant ? Role.Assistant : Role.User, Content = content };
        }

        private static Dictionary<string, JsonElement> ToDictionary(JsonElement? input)
        {
            var dictionary = new Dictionary<string, JsonElement>();
            if (input is { ValueKind: JsonValueKind.Object } value)
            {
                foreach (var property in value.EnumerateObject())
                {
                    dictionary[property.Name] = property.Value.Clone();
                }
            }

            return dictionary;
        }

        private static string StopReasonText(object reason)
        {
            var text = (reason.ToString() ?? StopReasons.EndTurn).Trim('"');
            // ApiEnum renders either the wire value or the enum member name; normalise to the wire value.
            return text switch
            {
                "EndTurn" => "end_turn",
                "ToolUse" => "tool_use",
                "MaxTokens" => "max_tokens",
                "StopSequence" => "stop_sequence",
                "PauseTurn" => "pause_turn",
                "Refusal" => "refusal",
                _ => text,
            };
        }

        private AnthropicClient CreateClient()
        {
            var key = _readKey();
            if (string.IsNullOrEmpty(key))
            {
                throw new ProviderException("no-api-key: no Anthropic API key is stored in the Windows Credential Manager (" + ProviderIds.CredentialTarget(ProviderIds.Anthropic) + ").");
            }

            var httpClient = new HttpClient(new BodyCaptureHandler(new HttpClientHandler())) { Timeout = TimeSpan.FromMinutes(15) };
            return _baseUrl is null
                ? new AnthropicClient { ApiKey = key, HttpClient = httpClient, MaxRetries = 2 }
                : new AnthropicClient { ApiKey = key, HttpClient = httpClient, MaxRetries = 2, BaseUrl = _baseUrl };
        }

        private static string Describe(Exception exception) => exception switch
        {
            ProviderException provider => provider.Message,
            AnthropicApiException api => "http-error: " + api.Message,
            HttpRequestException http => "network-error: " + http.Message,
            _ => "error: " + exception.Message,
        };

        private sealed class BlockBuilder
        {
            public string Type { get; set; } = ChatBlockTypes.Text;

            public StringBuilder Text { get; } = new StringBuilder();

            public StringBuilder InputJson { get; } = new StringBuilder();

            public string? Signature { get; set; }

            public string? ToolUseId { get; set; }

            public string? ToolName { get; set; }

            public ChatBlock? ToBlock()
            {
                switch (Type)
                {
                    case ChatBlockTypes.Text:
                        return ChatBlock.FromText(Text.ToString());
                    case ChatBlockTypes.Thinking:
                        return new ChatBlock { Type = Type, Text = Text.ToString(), Signature = Signature };
                    case ChatBlockTypes.RedactedThinking:
                        return new ChatBlock { Type = Type, Signature = Signature };
                    case ChatBlockTypes.ToolUse:
                        JsonElement? input = null;
                        try
                        {
                            input = RpcCodec.ParseElement(InputJson.Length == 0 ? "{}" : InputJson.ToString());
                        }
                        catch (JsonException)
                        {
                            // Truncated input (max_tokens): the loop sends back an INVALID_JSON tool result.
                        }

                        return new ChatBlock { Type = Type, ToolUseId = ToolUseId, ToolName = ToolName, Input = input };
                    default:
                        return null;
                }
            }
        }

        /// <summary>Records the request body of the current call (the API key is a header, never part of it).</summary>
        private sealed class BodyCaptureHandler : DelegatingHandler
        {
            public BodyCaptureHandler(HttpMessageHandler inner)
                : base(inner)
            {
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Content is not null && CapturedBody.Value is { } sink)
                {
                    var body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    lock (sink)
                    {
                        sink.Clear();
                        sink.Append(body);
                    }

                    // Re-buffer the content so the real send gets the same bytes.
                    var mediaType = request.Content.Headers.ContentType;
                    request.Content = new StringContent(body, Encoding.UTF8);
                    if (mediaType is not null)
                    {
                        request.Content.Headers.ContentType = mediaType;
                    }
                }

                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

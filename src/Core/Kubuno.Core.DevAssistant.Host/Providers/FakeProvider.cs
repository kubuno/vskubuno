using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Core.DevAssistant.Logic.Protocol;

namespace Kubuno.Core.DevAssistant.Host.Providers
{
    /// <summary>
    /// The offline test provider (docs/AI-ASSISTANT.md section 10: "no automated test calls a real API"): replays recorded
    /// streaming responses so the whole flow - streaming, tool rounds, change sets, cost - runs without a key or a
    /// network. A scenario is chosen by matching the user's message against <c>fixtures\index.json</c>; its rounds are
    /// played in order, one per model call after that message. Recorded tool inputs may reference earlier tool results
    /// (<c>{{toolu_id.path}}</c>: a JSON property of that tool's result) so a scenario adapts to the open file.
    /// </summary>
    public sealed class FakeProvider : IModelProvider
    {
        private static readonly Regex Template = new Regex(@"\{\{(?<id>[A-Za-z0-9_]+)\.(?<path>[A-Za-z0-9_.]+)\}\}", RegexOptions.CultureInvariant);
        private static readonly Regex ReferenceLabel = new Regex("<reference kind=\"(?<kind>[^\"]+)\" label=\"(?<label>[^\"]*)\"", RegexOptions.CultureInvariant);

        private readonly string _fixturesDirectory;

        public FakeProvider(string fixturesDirectory)
        {
            _fixturesDirectory = fixturesDirectory;
        }

        public string Id => ProviderIds.Fake;

        public string Endpoint => "localhost (réponses enregistrées)";

        public bool IsLocal => true;

        public Task<ModelsListResult> ListModelsAsync(CancellationToken cancellationToken) => Task.FromResult(new ModelsListResult
        {
            Models =
            {
                new ModelInfo { Id = "kubuno-fake", DisplayName = "Fake (recorded responses)", MaxInputTokens = 1_000_000, MaxOutputTokens = 128_000 },
            },
            FromProvider = true,
            Endpoint = Endpoint,
            IsLocal = true,
        });

        public async IAsyncEnumerable<ProviderEvent> StreamAsync(ProviderRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var (userIndex, userText) = LastUserMessage(request.Messages);
            var scenario = LoadScenario(userText);
            int round = request.Messages.Skip(userIndex + 1).Count(t => t.Role == ChatRoles.Assistant);

            yield return ProviderEvent.RequestBody(JsonSerializer.Serialize(new
            {
                provider = "fake",
                model = request.Model,
                effort = request.Effort,
                max_tokens = request.MaxTokens,
                system = request.System,
                tools = request.Tools.Select(t => new { t.Name, t.Description, input_schema = t.InputSchema }),
                messages = request.Messages,
            }, RpcCodec.IndentedOptions));

            if (round >= scenario.Rounds.Count)
            {
                yield return ProviderEvent.TextDelta("(fin du scénario enregistré)");
                yield return ProviderEvent.Completed(ChatBlock.FromText("(fin du scénario enregistré)"));
                yield return ProviderEvent.UsageReport(new UsageInfo { InputTokens = 100, OutputTokens = 5 });
                yield return ProviderEvent.Stop(StopReasons.EndTurn);
                yield break;
            }

            var recorded = scenario.Rounds[round];
            foreach (var item in recorded.Events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (item.Type)
                {
                    case "text":
                    case "thinking":
                        var text = Expand(item.Text ?? string.Empty, request.Messages, userText);
                        int chunk = Math.Max(1, item.Chunk ?? 8);
                        for (int i = 0; i < text.Length; i += chunk)
                        {
                            var piece = text.Substring(i, Math.Min(chunk, text.Length - i));
                            yield return item.Type == "text" ? ProviderEvent.TextDelta(piece) : ProviderEvent.ThinkingDelta(piece);
                            await Task.Delay(item.DelayMs ?? 12, cancellationToken).ConfigureAwait(false);
                        }

                        yield return ProviderEvent.Completed(item.Type == "text"
                            ? ChatBlock.FromText(text)
                            : new ChatBlock { Type = ChatBlockTypes.Thinking, Text = text, Signature = "fake-signature" });
                        break;
                    case "tool_use":
                        var input = item.Input is { } raw ? RpcCodec.ParseElement(Expand(raw.GetRawText(), request.Messages, userText, json: true)) : RpcCodec.ParseElement("{}");
                        var block = new ChatBlock { Type = ChatBlockTypes.ToolUse, ToolUseId = item.Id ?? "toolu_fake_" + Guid.NewGuid().ToString("N").Substring(0, 8), ToolName = item.Name, Input = input };
                        yield return ProviderEvent.ToolUseStarted(new ChatBlock { Type = ChatBlockTypes.ToolUse, ToolUseId = block.ToolUseId, ToolName = block.ToolName });
                        await Task.Delay(item.DelayMs ?? 30, cancellationToken).ConfigureAwait(false);
                        yield return ProviderEvent.Completed(block);
                        break;
                }
            }

            yield return ProviderEvent.UsageReport(recorded.Usage ?? new UsageInfo { InputTokens = 1200, OutputTokens = 150 });
            yield return ProviderEvent.Stop(recorded.Stop ?? StopReasons.EndTurn);
        }

        private Scenario LoadScenario(string userText)
        {
            var index = Path.Combine(_fixturesDirectory, "index.json");
            if (File.Exists(index))
            {
                var entries = JsonSerializer.Deserialize<List<IndexEntry>>(File.ReadAllText(index, Encoding.UTF8), RpcCodec.Options) ?? new List<IndexEntry>();
                foreach (var entry in entries)
                {
                    if (Regex.IsMatch(userText, entry.Pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
                    {
                        var path = Path.Combine(_fixturesDirectory, entry.File);
                        return JsonSerializer.Deserialize<Scenario>(File.ReadAllText(path, Encoding.UTF8), RpcCodec.Options) ?? new Scenario();
                    }
                }
            }

            return new Scenario
            {
                Rounds =
                {
                    new Round { Events = { new RecordedEvent { Type = "text", Text = "Aucun scénario enregistré ne correspond à ce message." } } },
                },
            };
        }

        /// <summary>The index of the last user turn holding text (not tool results) and that text.</summary>
        private static (int Index, string Text) LastUserMessage(IReadOnlyList<ChatTurn> messages)
        {
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                var turn = messages[i];
                if (turn.Role == ChatRoles.User && turn.Blocks.Any(b => b.Type == ChatBlockTypes.Text))
                {
                    return (i, string.Join("\n", turn.Blocks.Where(b => b.Type == ChatBlockTypes.Text).Select(b => b.Text)));
                }
            }

            return (-1, string.Empty);
        }

        private static string Expand(string text, IReadOnlyList<ChatTurn> messages, string userText, bool json = false)
        {
            if (text.Contains("{{user.references}}", StringComparison.Ordinal))
            {
                var labels = ReferenceLabel.Matches(userText).Select(m => m.Groups["kind"].Value + " « " + m.Groups["label"].Value + " »").ToList();
                text = text.Replace("{{user.references}}", labels.Count == 0 ? "aucune" : string.Join(", ", labels), StringComparison.Ordinal);
            }

            return Template.Replace(text, match =>
            {
                var result = messages.SelectMany(t => t.Blocks).FirstOrDefault(b => b.Type == ChatBlockTypes.ToolResult && b.ToolUseId == match.Groups["id"].Value);
                if (result?.Text is null)
                {
                    return match.Value;
                }

                try
                {
                    using var document = JsonDocument.Parse(result.Text);
                    var element = document.RootElement;
                    foreach (var segment in match.Groups["path"].Value.Split('.'))
                    {
                        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
                        {
                            return match.Value;
                        }
                    }

                    var value = element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.GetRawText();
                    return json ? JsonEncodedText.Encode(value).ToString() : value;
                }
                catch (JsonException)
                {
                    return match.Value;
                }
            });
        }

        private sealed class IndexEntry
        {
            public string Pattern { get; set; } = ".*";

            public string File { get; set; } = string.Empty;
        }

        private sealed class Scenario
        {
            public List<Round> Rounds { get; set; } = new List<Round>();
        }

        private sealed class Round
        {
            public List<RecordedEvent> Events { get; set; } = new List<RecordedEvent>();

            public string? Stop { get; set; }

            public UsageInfo? Usage { get; set; }
        }

        private sealed class RecordedEvent
        {
            public string Type { get; set; } = "text";

            public string? Text { get; set; }

            public int? Chunk { get; set; }

            public int? DelayMs { get; set; }

            public string? Id { get; set; }

            public string? Name { get; set; }

            public JsonElement? Input { get; set; }
        }
    }
}

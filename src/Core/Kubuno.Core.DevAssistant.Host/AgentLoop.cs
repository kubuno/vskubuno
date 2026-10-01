using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Core.DevAssistant.Host.Providers;
using Kubuno.Core.DevAssistant.Logic.Cost;
using Kubuno.Core.DevAssistant.Logic.Protocol;
using Kubuno.Core.DevAssistant.Logic.Tools;

namespace Kubuno.Core.DevAssistant.Host
{
    /// <summary>
    /// Runs one user turn (docs/AI-ASSISTANT.md sections 7.1 and 7.3): model call → tool calls answered by the VSIX →
    /// next model call, until the model ends its turn, the cost cap or the round limit is reached, or the developer
    /// presses Stop. The history is append-only: turns are only ever added, and a turn that ends with tool uses always
    /// gets its tool results (an error result when the loop stops early), so the next request stays valid.
    /// </summary>
    public sealed class AgentLoop
    {
        private readonly IModelProvider _provider;
        private readonly Func<ToolInvokeParams, CancellationToken, Task<ToolInvokeResult>> _invokeTool;
        private readonly Func<string, object, Task> _notify;

        public AgentLoop(IModelProvider provider, Func<ToolInvokeParams, CancellationToken, Task<ToolInvokeResult>> invokeTool, Func<string, object, Task> notify)
        {
            _provider = provider;
            _invokeTool = invokeTool;
            _notify = notify;
        }

        public async Task<SessionSendResult> RunAsync(SessionSendParams parameters, CancellationToken cancellationToken)
        {
            var result = new SessionSendResult();
            var ledger = new CostLedger(parameters.CostCapUsd, parameters.CostSoFarUsd);
            var rejected = new List<string>();
            var tools = ToolPolicy.Filter(parameters.Tools, rejected);
            var toolNames = new HashSet<string>(tools.Select(t => t.Name), StringComparer.Ordinal);
            var history = new List<ChatTurn>(parameters.History);

            try
            {
                for (int round = 0; ; round++)
                {
                    if (ledger.IsExhausted)
                    {
                        result.StopReason = StopReasons.CostCap;
                        break;
                    }

                    if (round >= Math.Max(1, parameters.MaxRounds))
                    {
                        result.StopReason = StopReasons.MaxRounds;
                        break;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    var request = new ProviderRequest
                    {
                        Model = parameters.Model,
                        Effort = parameters.Effort,
                        MaxTokens = parameters.MaxTokens,
                        System = parameters.System,
                        Tools = tools,
                        Messages = history,
                    };

                    var assistant = new ChatTurn { Role = ChatRoles.Assistant };
                    string stopReason = StopReasons.EndTurn;
                    await foreach (var item in _provider.StreamAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        switch (item.Kind)
                        {
                            case ProviderEventKind.RequestBody:
                                await _notify(DevAssistantMethods.RequestSent, new RequestSentParams { SessionId = parameters.SessionId, Round = round, Endpoint = _provider.Endpoint, Body = item.Text }).ConfigureAwait(false);
                                break;
                            case ProviderEventKind.TextDelta:
                                await _notify(DevAssistantMethods.StreamDelta, new StreamDeltaParams { SessionId = parameters.SessionId, Kind = ChatBlockTypes.Text, Text = item.Text, Round = round }).ConfigureAwait(false);
                                break;
                            case ProviderEventKind.ThinkingDelta:
                                await _notify(DevAssistantMethods.StreamDelta, new StreamDeltaParams { SessionId = parameters.SessionId, Kind = ChatBlockTypes.Thinking, Text = item.Text, Round = round }).ConfigureAwait(false);
                                break;
                            case ProviderEventKind.BlockCompleted:
                                assistant.Blocks.Add(item.Block!);
                                if (item.Block!.Type == ChatBlockTypes.ToolUse)
                                {
                                    await _notify(DevAssistantMethods.StreamToolCall, new StreamToolCallParams { SessionId = parameters.SessionId, ToolUseId = item.Block.ToolUseId ?? string.Empty, Name = item.Block.ToolName ?? string.Empty, Input = item.Block.Input, Round = round }).ConfigureAwait(false);
                                }

                                break;
                            case ProviderEventKind.Usage:
                                var cost = ledger.Record(parameters.Model, item.Usage!);
                                await _notify(DevAssistantMethods.Usage, new UsageParams { SessionId = parameters.SessionId, Model = parameters.Model, Usage = item.Usage!, CostUsd = cost, SessionCostUsd = ledger.SpentUsd }).ConfigureAwait(false);
                                break;
                            case ProviderEventKind.Stop:
                                stopReason = item.Text;
                                break;
                        }
                    }

                    if (assistant.Blocks.Count == 0)
                    {
                        // A provider that answered nothing (should not happen): avoid an empty assistant turn.
                        assistant.Blocks.Add(ChatBlock.FromText(string.Empty));
                    }

                    history.Add(assistant);
                    result.NewTurns.Add(assistant);

                    var toolUses = assistant.Blocks.Where(b => b.Type == ChatBlockTypes.ToolUse).ToList();
                    if (stopReason == StopReasons.Refusal)
                    {
                        AddToolResults(toolUses, "Not run: the model declined the request.", history, result);
                        result.StopReason = StopReasons.Refusal;
                        break;
                    }

                    if (toolUses.Count == 0)
                    {
                        result.StopReason = stopReason == StopReasons.MaxTokens ? StopReasons.MaxTokens : StopReasons.EndTurn;
                        break;
                    }

                    if (ledger.IsExhausted)
                    {
                        // No further call may follow: running the tools would only spend the developer's time.
                        AddToolResults(toolUses, "Not run: the session's cost cap is reached.", history, result);
                        result.StopReason = StopReasons.CostCap;
                        break;
                    }

                    if (stopReason == StopReasons.MaxTokens)
                    {
                        // Tool inputs may be truncated: never run them.
                        AddToolResults(toolUses, "INVALID_JSON: the tool input was cut by max_tokens; nothing was run.", history, result);
                        result.StopReason = StopReasons.MaxTokens;
                        break;
                    }

                    var toolResults = new ChatTurn { Role = ChatRoles.User };
                    foreach (var toolUse in toolUses)
                    {
                        toolResults.Blocks.Add(await RunToolAsync(parameters.SessionId, toolUse, toolNames, cancellationToken).ConfigureAwait(false));
                    }

                    history.Add(toolResults);
                    result.NewTurns.Add(toolResults);
                }
            }
            catch (OperationCanceledException)
            {
                result.StopReason = StopReasons.Cancelled;
                CloseDanglingToolUses(history, result, "Not run: stopped by the developer.");
            }
            catch (ProviderException exception)
            {
                result.StopReason = StopReasons.Error;
                result.Error = exception.Message;
                CloseDanglingToolUses(history, result, "Not run: the provider failed.");
            }

            if (result.StopReason is StopReasons.CostCap or StopReasons.MaxRounds)
            {
                CloseDanglingToolUses(history, result, "Not run: the turn stopped (" + result.StopReason + ").");
            }

            if (rejected.Count > 0 && result.Error is null)
            {
                result.Error = "Tools refused by the policy: " + string.Join("; ", rejected);
            }

            result.Usage = ledger.TurnUsage;
            result.CostUsd = ledger.TurnUsd;
            return result;
        }

        private async Task<ChatBlock> RunToolAsync(string sessionId, ChatBlock toolUse, HashSet<string> toolNames, CancellationToken cancellationToken)
        {
            var id = toolUse.ToolUseId ?? string.Empty;
            if (!toolNames.Contains(toolUse.ToolName ?? string.Empty))
            {
                return ChatBlock.ToolResultFor(id, $"Unknown tool '{toolUse.ToolName}'. Only the declared tools exist.", isError: true);
            }

            if (toolUse.Input is not { ValueKind: JsonValueKind.Object } input)
            {
                return ChatBlock.ToolResultFor(id, "INVALID_JSON: the tool input is not a JSON object; send it again.", isError: true);
            }

            try
            {
                var answer = await _invokeTool(new ToolInvokeParams { SessionId = sessionId, ToolUseId = id, Name = toolUse.ToolName!, Input = input }, cancellationToken).ConfigureAwait(false);
                return ChatBlock.ToolResultFor(id, answer.Content, answer.IsError);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return ChatBlock.ToolResultFor(id, "Tool failed: " + exception.Message, isError: true);
            }
        }

        private static void AddToolResults(List<ChatBlock> toolUses, string message, List<ChatTurn> history, SessionSendResult result)
        {
            if (toolUses.Count == 0)
            {
                return;
            }

            var turn = new ChatTurn { Role = ChatRoles.User };
            turn.Blocks.AddRange(toolUses.Select(t => ChatBlock.ToolResultFor(t.ToolUseId ?? string.Empty, message, isError: true)));
            history.Add(turn);
            result.NewTurns.Add(turn);
        }

        /// <summary>When the last added turn is an assistant turn with tool uses, answers them with an error result.</summary>
        private static void CloseDanglingToolUses(List<ChatTurn> history, SessionSendResult result, string message)
        {
            if (result.NewTurns.Count == 0)
            {
                return;
            }

            var last = result.NewTurns[result.NewTurns.Count - 1];
            if (last.Role == ChatRoles.Assistant)
            {
                AddToolResults(last.Blocks.Where(b => b.Type == ChatBlockTypes.ToolUse).ToList(), message, history, result);
            }
        }
    }
}

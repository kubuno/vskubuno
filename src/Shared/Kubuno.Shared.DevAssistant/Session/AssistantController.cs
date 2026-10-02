using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE80;
using Kubuno.Shared.DevAssistant.Changes;
using Kubuno.Shared.DevAssistant.Commands;
using Kubuno.Shared.DevAssistant.Extensibility;
using Kubuno.Shared.DevAssistant.Host;
using Kubuno.Shared.DevAssistant.Logic.Changes;
using Kubuno.Shared.DevAssistant.Logic.Conversations;
using Kubuno.Shared.DevAssistant.Logic.Prompts;
using Kubuno.Shared.DevAssistant.Logic.Protocol;
using Kubuno.Shared.DevAssistant.Logic.Secrets;
using Kubuno.Shared.DevAssistant.Logic.Tools;
using Kubuno.Shared.DevAssistant.References;
using Kubuno.Shared.DevAssistant.Settings;
using Kubuno.Shared.DevAssistant.Tools;
using Kubuno.Shared.Logging;
using Kubuno.Shared.Mcp.Bridge;
using Kubuno.Shared.Mcp.Bridge.Contracts;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Shared.DevAssistant.Session
{
    /// <summary>What the controller tells the tool window (always on the UI thread).</summary>
    internal interface IAssistantView
    {
        void Clear();

        void AddUserMessage(string text, IReadOnlyList<string> references);

        void BeginAnswer();

        void AppendDelta(string kind, string text, int round);

        void AddToolCall(string toolUseId, string name, string summary);

        void CompleteToolCall(string toolUseId, bool isError, string summary);

        void UpdateCost(decimal sessionCostUsd, UsageInfo lastUsage);

        void EndAnswer(string stopReason, string? error, ChangeSet? changes, IReadOnlyList<string> requestBodies);

        void ShowNotice(string text);

        void SetBusy(bool busy);

        void ShowModels(ModelsListResult models, string selected);
    }

    /// <summary>
    /// The assistant's brain on the Visual Studio side (docs/AI-ASSISTANT.md sections 5, 8 and 9): parses the prompt,
    /// resolves the references, MASKS everything before it reaches the host process, keeps the conversation
    /// (append-only, persisted masked in <c>.vs</c>), answers the host's tool calls through the registered tools (inputs
    /// validated against their schemas, results masked), collects the proposed changes of an answer into a change set,
    /// and applies the reviewed hunks to the buffers as one undo unit.
    /// </summary>
    internal sealed class AssistantController : IDisposable
    {
        private readonly IAssistantView _view;
        private readonly JoinableTaskFactory _jtf;
        private readonly DevAssistantHostClient _host;
        private readonly ConcurrentDictionary<string, IDevAssistantTool> _activeTools = new ConcurrentDictionary<string, IDevAssistantTool>(StringComparer.Ordinal);
        private readonly List<string> _requestBodies = new List<string>();
        private IVsContextProvider? _context;
        private List<IDevAssistantTool> _tools = new List<IDevAssistantTool>();
        private List<IDevAssistantReferenceProvider> _references = new List<IDevAssistantReferenceProvider>();
        private List<DevAssistantCommand> _commands = new List<DevAssistantCommand>();
        private ConversationStore? _store;
        private SecretMasker _masker;
        private ChangeSet _changes = new ChangeSet();
        private DevAssistantToolContext? _toolContext;
        private CancellationTokenSource? _turn;
        private IReadOnlyList<string> _roots = Array.Empty<string>();

        public AssistantController(IAssistantView view, JoinableTaskFactory jtf)
        {
            _view = view;
            _jtf = jtf;
            _host = new DevAssistantHostClient(HandleHostRequestAsync, HandleHostNotification);
            Settings = new DevAssistantSettings();
            Conversation = NewConversation();
            _masker = new SecretMasker(Conversation.Salt);
        }

        public DevAssistantSettings Settings { get; private set; }

        public Conversation Conversation { get; private set; }

        /// <summary>The model chosen in the picker (may differ from the settings' default).</summary>
        public string Model { get; set; } = "claude-opus-5-5";

        public string Effort { get; set; } = "medium";

        public bool IsBusy => _turn is not null;

        public IReadOnlyList<DevAssistantCommand> Commands => _commands;

        /// <summary>The request bodies of the last answer (« Voir la requête »).</summary>
        public IReadOnlyList<string> LastRequestBodies => _requestBodies;

        public ConversationStore? Store => _store;

        /// <summary>UI thread: loads the settings, the layers' contributions (MEF) and the model list.</summary>
        public async Task InitializeAsync()
        {
            await _jtf.SwitchToMainThreadAsync();
            Settings = DevAssistantSettings.Load();
            Model = Settings.Model;
            Effort = Settings.Effort;
            var dte = (DTE2?)await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(SDTE));
            if (dte is not null)
            {
                _context = new Kubuno.Shared.Mcp.Bridge.Dte.DteVsContextProvider(dte);
            }

            _tools = new List<IDevAssistantTool>();
            _references = new List<IDevAssistantReferenceProvider>();
            _commands = new List<DevAssistantCommand>(new SharedCommands().GetCommands());
            if (_context is not null)
            {
                _tools.AddRange(SharedTools.Create(_context));
                _references.Add(new SharedReferences(_context));
            }

            if (await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(SComponentModel)) is IComponentModel componentModel)
            {
                foreach (var provider in SafeExtensions<IDevAssistantToolProvider>(componentModel))
                {
                    _tools.AddRange(provider.GetTools());
                }

                _references.AddRange(SafeExtensions<IDevAssistantReferenceProvider>(componentModel));
                foreach (var provider in SafeExtensions<IDevAssistantCommandProvider>(componentModel))
                {
                    _commands.AddRange(provider.GetCommands());
                }
            }

            await RefreshRootsAsync();
            await RefreshModelsAsync();
        }

        private static IEnumerable<T> SafeExtensions<T>(IComponentModel componentModel)
            where T : class
        {
            try
            {
                return componentModel.GetExtensions<T>().ToList();
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno Dev Assistant: loading the " + typeof(T).Name + " parts failed", exception);
                return Array.Empty<T>();
            }
        }

        /// <summary>The solution (or opened folder) root and the matching conversation store.</summary>
        private async Task RefreshRootsAsync()
        {
            if (_context is null)
            {
                return;
            }

            var solution = await _context.GetSolutionOrFolderAsync(CancellationToken.None);
            var roots = new List<string>();
            if (!string.IsNullOrEmpty(solution.RootPath) && Directory.Exists(solution.RootPath))
            {
                roots.Add(Path.GetFullPath(solution.RootPath!));
            }

            _roots = roots;
            _store = roots.Count > 0 ? new ConversationStore(roots[0]) : null;
        }

        public async Task RefreshModelsAsync()
        {
            ModelsListResult models;
            try
            {
                models = (await _host.InvokeAsync(DevAssistantMethods.ModelsList, new ModelsListParams { Provider = Settings.Provider }, CancellationToken.None).ConfigureAwait(false)).ResultAs<ModelsListResult>();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                models = new ModelsListResult { Error = exception.Message, Endpoint = Settings.Provider == ProviderIds.Fake ? "localhost" : "api.anthropic.com", IsLocal = Settings.Provider == ProviderIds.Fake };
            }

            await _jtf.SwitchToMainThreadAsync();
            if (models.Models.Count > 0 && !models.Models.Any(m => m.Id == Model))
            {
                Model = models.Models.Any(m => m.Id == Settings.Model) ? Settings.Model : models.Models[0].Id;
            }

            _view.ShowModels(models, Model);
        }

        public void ApplySettings(DevAssistantSettings settings)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var providerChanged = settings.Provider != Settings.Provider;
            Settings = settings;
            Settings.Save();
            Effort = settings.Effort;
            if (providerChanged || !string.Equals(Model, settings.Model, StringComparison.Ordinal))
            {
                Model = settings.Model;
            }

            _ = _jtf.RunAsync(RefreshModelsAsync);
        }

        private Conversation NewConversation() => new Conversation { Provider = Settings.Provider, Model = Model };

        public void StartNewConversation()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Conversation = NewConversation();
            _masker = new SecretMasker(Conversation.Salt);
            _changes = new ChangeSet();
            _requestBodies.Clear();
            _view.Clear();
        }

        public void LoadConversation(string id)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var loaded = _store?.Load(id);
            if (loaded is null)
            {
                return;
            }

            Conversation = loaded;
            _masker = new SecretMasker(loaded.Salt);
            _changes = new ChangeSet();
            _requestBodies.Clear();
            _view.Clear();
            foreach (var turn in loaded.Turns)
            {
                if (turn.Role == ChatRoles.User && turn.Blocks.Any(b => b.Type == ChatBlockTypes.Text))
                {
                    _view.AddUserMessage(UserTextOnly(turn.Blocks.First(b => b.Type == ChatBlockTypes.Text).Text ?? string.Empty), Array.Empty<string>());
                }
                else if (turn.Role == ChatRoles.Assistant)
                {
                    _view.BeginAnswer();
                    foreach (var block in turn.Blocks)
                    {
                        if (block.Type == ChatBlockTypes.Text)
                        {
                            _view.AppendDelta(ChatBlockTypes.Text, block.Text ?? string.Empty, 0);
                        }
                        else if (block.Type == ChatBlockTypes.ToolUse)
                        {
                            _view.AddToolCall(block.ToolUseId ?? string.Empty, block.ToolName ?? string.Empty, Summarize(block.Input));
                        }
                    }

                    _view.EndAnswer(StopReasons.EndTurn, null, null, Array.Empty<string>());
                }
            }

            _view.UpdateCost(loaded.CostUsd, loaded.Usage);
        }

        /// <summary>The text the developer typed, without the attached references.</summary>
        private static string UserTextOnly(string text)
        {
            var index = text.IndexOf("\n\n<reference ", StringComparison.Ordinal);
            return index < 0 ? text : text.Substring(0, index);
        }

        /// <summary>Command aliases → canonical names, for the parser.</summary>
        public IReadOnlyDictionary<string, string> CommandAliases()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var command in _commands)
            {
                map[PromptParser.Fold(command.Name)] = command.Name;
                foreach (var alias in command.Aliases)
                {
                    map[PromptParser.Fold(alias)] = command.Name;
                }
            }

            return map;
        }

        /// <summary>Sends a message (UI thread). Returns when the answer is complete.</summary>
        public async Task SendAsync(string input)
        {
            await _jtf.SwitchToMainThreadAsync();
            if (IsBusy || string.IsNullOrWhiteSpace(input))
            {
                return;
            }

            if (Settings.Provider == ProviderIds.Anthropic && !Settings.AnthropicConsent)
            {
                if (new UI.ConsentDialog().ShowModal() != true)
                {
                    return;
                }

                Settings.AnthropicConsent = true;
                Settings.Save();
            }

            var parsed = PromptParser.Parse(input, CommandAliases());
            var command = parsed.Command is null ? null : _commands.FirstOrDefault(c => c.Name == parsed.Command);
            if (command?.Name == "aide")
            {
                _view.AddUserMessage(input, Array.Empty<string>());
                _view.ShowNotice(HelpText());
                return;
            }

            // Hard stop on a secret in the developer's own text (section 8.3).
            bool maskUserText = true;
            if (SecretScanner.ContainsSecret(parsed.Text))
            {
                var choice = VsShellUtilities.ShowMessageBox(
                    ServiceProvider.GlobalProvider,
                    UI.AssistantText.S("Ce message contient un secret probable.\n\nOui : le masquer avant l'envoi\nNon : l'envoyer quand même\nAnnuler : ne rien envoyer", "This message contains a probable secret.\n\nYes: mask it before sending\nNo: send it anyway\nCancel: send nothing"),
                    UI.AssistantText.WindowTitle,
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_YESNOCANCEL,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                if (choice == 2 /* IDCANCEL */)
                {
                    return;
                }

                maskUserText = choice != 7 /* IDNO */;
            }

            _turn = new CancellationTokenSource();
            _view.SetBusy(true);
            _requestBodies.Clear();
            _changes = new ChangeSet();
            try
            {
                await RefreshRootsAsync();
                _toolContext = new DevAssistantToolContext(_roots, _masker, _changes, ReadDocument, ProposeChange);

                // References: those written, plus the command's defaults when none was given.
                var references = parsed.References.ToList();
                if (command is not null && references.Count == 0)
                {
                    references.AddRange(command.DefaultReferences.Select(kind => new PromptReference(kind, null)));
                }

                var resolved = new List<ResolvedReference>();
                foreach (var reference in references)
                {
                    var provider = _references.FirstOrDefault(p => p.Kinds.Contains(reference.Kind));
                    if (provider is null)
                    {
                        continue;
                    }

                    try
                    {
                        if (await provider.ResolveAsync(reference, _toolContext, _turn.Token) is { } value)
                        {
                            resolved.Add(value);
                        }
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        KubunoLog.WriteException("Kubuno Dev Assistant: resolving " + reference + " failed", exception);
                    }
                }

                await _jtf.SwitchToMainThreadAsync();
                _view.AddUserMessage(input, resolved.Select(r => "#" + r.Kind + " " + r.Label).ToList());

                var message = new StringBuilder(maskUserText ? _masker.Mask(parsed.Text) : parsed.Text);
                if (command is not null && !parsed.Text.StartsWith("/", StringComparison.Ordinal))
                {
                    message.Insert(0, "/" + command.Name + " ");
                }

                foreach (var reference in resolved)
                {
                    message.Append("\n\n<reference kind=\"").Append(reference.Kind).Append("\" label=\"").Append(reference.Label.Replace("\"", "'")).Append("\">\n");
                    message.Append(_masker.Mask(reference.Content));
                    message.Append("\n</reference>");
                }

                var userTurn = new ChatTurn { Role = ChatRoles.User, Blocks = { ChatBlock.FromText(message.ToString()) } };
                if (string.IsNullOrEmpty(Conversation.Title))
                {
                    var title = parsed.Text.Replace('\n', ' ').Trim();
                    Conversation.Title = title.Length > 60 ? title.Substring(0, 60) + "…" : title;
                }

                var history = new List<ChatTurn>(Conversation.Turns) { userTurn };
                var tools = _tools.Where(t => ToolPolicy.WhyRejected(t.Descriptor) is null).ToList();
                _activeTools.Clear();
                foreach (var tool in tools)
                {
                    _activeTools[tool.Descriptor.Name] = tool;
                }

                var system = new List<SystemPart>
                {
                    new SystemPart { Text = SharedCommands.RulesDigest(), CacheBreakpoint = true },
                };
                if (command is not null && command.Digest.Length > 0)
                {
                    system.Add(new SystemPart { Text = command.Digest, CacheBreakpoint = true });
                }

                _view.BeginAnswer();
                var parameters = new SessionSendParams
                {
                    SessionId = Conversation.Id,
                    Provider = Settings.Provider,
                    Model = Model,
                    Effort = command?.Effort is { } commandEffort && Effort == Settings.Effort ? commandEffort : Effort,
                    System = system,
                    Tools = tools.Select(t => t.Descriptor).ToList(),
                    History = history,
                    CostCapUsd = Settings.CostCapUsd,
                    CostSoFarUsd = Conversation.CostUsd,
                    MaxRounds = Settings.MaxRounds,
                };

                SessionSendResult result;
                try
                {
                    result = (await _host.InvokeAsync(DevAssistantMethods.SessionSend, parameters, _turn.Token)).ResultAs<SessionSendResult>();
                }
                catch (OperationCanceledException)
                {
                    result = new SessionSendResult { StopReason = StopReasons.Cancelled };
                }
                catch (Exception exception)
                {
                    result = new SessionSendResult { StopReason = StopReasons.Error, Error = exception.Message };
                }

                await _jtf.SwitchToMainThreadAsync();
                if (result.NewTurns.Count > 0 || result.StopReason != StopReasons.Cancelled)
                {
                    // Append-only: the user turn and whatever the host added. A failed send adds nothing the provider never saw.
                    if (result.NewTurns.Count > 0)
                    {
                        Conversation.Turns.Add(userTurn);
                        Conversation.Turns.AddRange(result.NewTurns);
                    }

                    Conversation.CostUsd += result.CostUsd;
                    Conversation.Usage.Add(result.Usage);
                    Conversation.Model = Model;
                    if (result.NewTurns.Count > 0)
                    {
                        try
                        {
                            _store?.Append(Conversation, new[] { userTurn }.Concat(result.NewTurns));
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                        {
                            KubunoLog.WriteException("Kubuno Dev Assistant: saving the conversation failed", exception);
                        }
                    }
                }

                _view.UpdateCost(Conversation.CostUsd, result.Usage);
                _view.EndAnswer(result.StopReason, DescribeError(result.Error), _changes.IsEmpty ? null : _changes, _requestBodies.ToList());
            }
            finally
            {
                _turn?.Dispose();
                _turn = null;
                _view.SetBusy(false);
            }
        }

        /// <summary>Stop: cancels the stream and any tool in progress.</summary>
        public void Cancel()
        {
            var turn = _turn;
            if (turn is null)
            {
                return;
            }

            _ = _jtf.RunAsync(async () =>
            {
                try
                {
                    await _host.InvokeAsync(DevAssistantMethods.SessionCancel, new SessionCancelParams { SessionId = Conversation.Id }, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    try
                    {
                        turn.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
            });
        }

        /// <summary>Applies the accepted hunks of <paramref name="changes"/> (UI thread) as one undo unit.</summary>
        public ApplyReport Apply(ChangeSet changes)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var report = ChangeSetApplier.Apply(new VsChangeSetBufferHost(), changes, UI.AssistantText.S("Assistant Kubuno : modifications acceptées", "Kubuno assistant: accepted changes"));
            return report;
        }

        private string? DescribeError(string? error)
        {
            if (error is null)
            {
                return null;
            }

            if (error.StartsWith("no-api-key", StringComparison.Ordinal))
            {
                return UI.AssistantText.S(
                    "Aucune clé API Anthropic n'est enregistrée. Ouvrez les paramètres (⚙), collez votre clé et choisissez « Enregistrer la clé ».",
                    "No Anthropic API key is stored. Open the settings (⚙), paste your key and choose “Save the key”.");
            }

            return error;
        }

        private string HelpText()
        {
            var builder = new StringBuilder();
            builder.Append(UI.AssistantText.S("**Commandes**\n", "**Commands**\n"));
            foreach (var command in _commands)
            {
                builder.Append("- `/").Append(command.Name).Append('`');
                if (command.Aliases.Count > 0)
                {
                    builder.Append(" (").Append(string.Join(", ", command.Aliases.Select(a => "`/" + a + "`"))).Append(')');
                }

                builder.Append(" — ").Append(UI.AssistantText.IsFrench ? command.DescriptionFr : command.DescriptionEn).Append('\n');
            }

            builder.Append(UI.AssistantText.S("\n**Références**\n", "\n**References**\n"));
            builder.Append(UI.AssistantText.S(
                "- `#fichier` ou `#fichier:chemin` — le document actif ou un fichier\n- `#sélection` — la sélection de l'éditeur\n- `#élément` — l'élément sélectionné dans le concepteur de vues\n",
                "- `#file` or `#file:path` — the active document or a file\n- `#selection` — the editor selection\n- `#element` — the element selected in the view designer\n"));
            return builder.ToString();
        }

        /// <summary>Buffer-aware read for the tools: the pending proposal of this answer, the editor buffer, then the disk.</summary>
        private string? ReadDocument(string path)
        {
            if (_toolContext?.WhyDenied(path) is not null)
            {
                return null;
            }

            if (_changes.Find(path) is { } pending)
            {
                return pending.ProposedText;
            }

#pragma warning disable VSTHRD104 // Tools run on background threads; the read needs the UI thread briefly.
            return _jtf.Run(async () =>
            {
                await _jtf.SwitchToMainThreadAsync();
                return new VsChangeSetBufferHost().GetCurrentText(path);
            });
#pragma warning restore VSTHRD104
        }

        private void ProposeChange(string path, string? original, string proposed)
        {
            lock (_changes)
            {
                _changes.Propose(path, original, proposed);
            }
        }

        private async Task<object?> HandleHostRequestAsync(RpcMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != DevAssistantMethods.ToolInvoke)
            {
                throw new RpcException(RpcErrorCodes.MethodNotFound, "Unknown method " + request.Method);
            }

            var invoke = request.ParamsAs<ToolInvokeParams>();
            if (!_activeTools.TryGetValue(invoke.Name, out var tool) || _toolContext is null)
            {
                return new ToolInvokeResult { Content = $"Unknown tool '{invoke.Name}'.", IsError = true };
            }

            var errors = ToolInputValidator.Validate(tool.Descriptor.InputSchema, invoke.Input);
            DevAssistantToolResult result;
            if (errors.Count > 0)
            {
                result = DevAssistantToolResult.Error("INVALID_INPUT: " + string.Join("; ", errors));
            }
            else
            {
                try
                {
                    result = await tool.InvokeAsync(invoke.Input, _toolContext, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    KubunoLog.WriteException("Kubuno Dev Assistant: tool " + invoke.Name + " failed", exception);
                    result = DevAssistantToolResult.Error("Tool failed: " + exception.Message);
                }
            }

            var masked = _masker.Mask(result.Content);
            await _jtf.SwitchToMainThreadAsync();
            _view.CompleteToolCall(invoke.ToolUseId, result.IsError, FirstLine(masked));
            return new ToolInvokeResult { Content = masked, IsError = result.IsError };
        }

        private void HandleHostNotification(RpcMessage notification)
        {
            switch (notification.Method)
            {
                case DevAssistantMethods.StreamDelta:
                    var delta = notification.ParamsAs<StreamDeltaParams>();
                    _ = _jtf.RunAsync(async () =>
                    {
                        await _jtf.SwitchToMainThreadAsync();
                        _view.AppendDelta(delta.Kind, delta.Text, delta.Round);
                    });
                    break;
                case DevAssistantMethods.StreamToolCall:
                    var call = notification.ParamsAs<StreamToolCallParams>();
                    _ = _jtf.RunAsync(async () =>
                    {
                        await _jtf.SwitchToMainThreadAsync();
                        _view.AddToolCall(call.ToolUseId, call.Name, Summarize(call.Input));
                    });
                    break;
                case DevAssistantMethods.Usage:
                    var usage = notification.ParamsAs<UsageParams>();
                    _ = _jtf.RunAsync(async () =>
                    {
                        await _jtf.SwitchToMainThreadAsync();
                        _view.UpdateCost(usage.SessionCostUsd, usage.Usage);
                    });
                    break;
                case DevAssistantMethods.RequestSent:
                    var sent = notification.ParamsAs<RequestSentParams>();
                    lock (_requestBodies)
                    {
                        _requestBodies.Add($"// {UI.AssistantText.S("Appel", "Call")} {sent.Round + 1} → {sent.Endpoint}\n{PrettyJson(sent.Body)}");
                    }

                    break;
            }
        }

        private static string PrettyJson(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                return JsonSerializer.Serialize(document.RootElement, RpcCodec.IndentedOptions);
            }
            catch (JsonException)
            {
                return body;
            }
        }

        private static string Summarize(JsonElement? input)
        {
            if (input is not { ValueKind: JsonValueKind.Object } value)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            foreach (var property in value.EnumerateObject())
            {
                var text = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.GetRawText();
                text = text.Replace('\n', ' ');
                parts.Add(property.Name + "=" + (text.Length > 60 ? text.Substring(0, 60) + "…" : text));
            }

            return string.Join(", ", parts);
        }

        private static string FirstLine(string text)
        {
            var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 1) ?? string.Empty;
            return line.Length > 120 ? line.Substring(0, 120) + "…" : line;
        }

        public void Dispose()
        {
            _turn?.Cancel();
            _host.Dispose();
        }
    }
}

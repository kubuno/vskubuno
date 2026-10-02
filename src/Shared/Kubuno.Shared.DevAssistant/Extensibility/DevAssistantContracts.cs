using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Core.DevAssistant.Logic.Changes;
using Kubuno.Core.DevAssistant.Logic.Prompts;
using Kubuno.Core.DevAssistant.Logic.Protocol;
using Kubuno.Core.DevAssistant.Logic.Secrets;

namespace Kubuno.Core.DevAssistant.Extensibility
{
    /// <summary>
    /// A tool the model may call (docs/AI-ASSISTANT.md section 6.3). Its descriptor is MCP-shaped; the policy
    /// (<see cref="Logic.Tools.ToolPolicy"/>) decides whether it may be exposed at all. A tool never writes a file: a
    /// write-class tool only proposes a change through <see cref="DevAssistantToolContext.ProposeChange"/>, applied after
    /// the developer's review.
    /// </summary>
    public interface IDevAssistantTool
    {
        ToolDescriptor Descriptor { get; }

        /// <summary>Runs the tool (any thread). <paramref name="input"/> has been validated against the descriptor's schema.</summary>
        Task<DevAssistantToolResult> InvokeAsync(JsonElement input, DevAssistantToolContext context, CancellationToken cancellationToken);
    }

    /// <summary>MEF contract: a layer's tools (exported with <c>[Export(typeof(IDevAssistantToolProvider))]</c>).</summary>
    public interface IDevAssistantToolProvider
    {
        IEnumerable<IDevAssistantTool> GetTools();
    }

    /// <summary>MEF contract: a layer's <c>#</c> references (section 5.2).</summary>
    public interface IDevAssistantReferenceProvider
    {
        /// <summary>The canonical kinds it resolves (<see cref="ReferenceKinds"/>).</summary>
        IEnumerable<string> Kinds { get; }

        /// <summary>The reference's content (masked later by the caller), or null when it cannot be resolved now.</summary>
        Task<ResolvedReference?> ResolveAsync(PromptReference reference, DevAssistantToolContext context, CancellationToken cancellationToken);
    }

    /// <summary>MEF contract: a layer's <c>/</c> commands (section 5.3).</summary>
    public interface IDevAssistantCommandProvider
    {
        IEnumerable<DevAssistantCommand> GetCommands();
    }

    /// <summary>MEF contract: started once at idle when the package loads (e.g. to follow the designer's selection).</summary>
    public interface IDevAssistantStartup
    {
        void Start();
    }

    /// <summary>A <c>/</c> command: names, the digest added to the system prompt while it runs, and its effort.</summary>
    public sealed class DevAssistantCommand
    {
        /// <summary>Canonical French name without the slash (<c>vue</c>).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Other spellings (<c>view</c>).</summary>
        public IReadOnlyList<string> Aliases { get; set; } = Array.Empty<string>();

        public string DescriptionFr { get; set; } = string.Empty;

        public string DescriptionEn { get; set; } = string.Empty;

        /// <summary>The command digest (section 6.1, tier 2), sent at the second cache breakpoint while the command runs.</summary>
        public string Digest { get; set; } = string.Empty;

        /// <summary>low, medium or high (section 5.3's table).</summary>
        public string Effort { get; set; } = "medium";

        /// <summary>References resolved automatically when the developer gives none (e.g. <c>/vue</c> attaches the active view).</summary>
        public IReadOnlyList<string> DefaultReferences { get; set; } = Array.Empty<string>();
    }

    /// <summary>A resolved <c>#</c> reference.</summary>
    public sealed class ResolvedReference
    {
        public ResolvedReference(string kind, string label, string content)
        {
            Kind = kind;
            Label = label;
            Content = content;
        }

        public string Kind { get; }

        /// <summary>Short label shown on the chip and in the transcript (<c>Grid "settingsGrid" (settings_view.kbview)</c>).</summary>
        public string Label { get; }

        /// <summary>The text attached to the message (masked by the caller before it leaves Visual Studio).</summary>
        public string Content { get; }
    }

    /// <summary>The result of a tool: text sent back to the model (masked by the caller).</summary>
    public sealed class DevAssistantToolResult
    {
        private DevAssistantToolResult(string content, bool isError)
        {
            Content = content;
            IsError = isError;
        }

        public string Content { get; }

        public bool IsError { get; }

        public static DevAssistantToolResult Text(string content) => new DevAssistantToolResult(content, false);

        public static DevAssistantToolResult Json(object value) => new DevAssistantToolResult(JsonSerializer.Serialize(value, RpcCodec.IndentedOptions), false);

        public static DevAssistantToolResult Error(string message) => new DevAssistantToolResult(message, true);
    }

    /// <summary>What tools and reference resolvers get from the assistant.</summary>
    public sealed class DevAssistantToolContext
    {
        private readonly Func<string, string?> _readDocument;
        private readonly Action<string, string?, string> _proposeChange;

        public DevAssistantToolContext(
            IReadOnlyList<string> solutionRoots,
            SecretMasker masker,
            ChangeSet changeSet,
            Func<string, string?> readDocument,
            Action<string, string?, string> proposeChange)
        {
            SolutionRoots = solutionRoots;
            Masker = masker;
            ChangeSet = changeSet;
            _readDocument = readDocument;
            _proposeChange = proposeChange;
        }

        /// <summary>The roots tools may read and propose writes in (solution folder, opened folder).</summary>
        public IReadOnlyList<string> SolutionRoots { get; }

        /// <summary>The conversation's masker: restores placeholders in proposed edits.</summary>
        public SecretMasker Masker { get; }

        /// <summary>The change set of the current answer.</summary>
        public ChangeSet ChangeSet { get; }

        /// <summary>
        /// The current text of a file: its editor buffer when open (unsaved changes included), the disk otherwise; the
        /// pending proposal when this answer already changed it; null when missing, outside the roots or denied.
        /// </summary>
        public string? ReadDocument(string path) => _readDocument(path);

        /// <summary>Proposes that <paramref name="path"/> becomes <paramref name="proposedText"/> (<paramref name="originalText"/> null: a new file).</summary>
        public void ProposeChange(string path, string? originalText, string proposedText) => _proposeChange(path, originalText, proposedText);

        /// <summary>Why <paramref name="path"/> may not be read or written, or null (section 7.4 / 8.3).</summary>
        public string? WhyDenied(string path) => DeniedFiles.WhyDenied(path, (IReadOnlyCollection<string>)SolutionRoots);
    }
}

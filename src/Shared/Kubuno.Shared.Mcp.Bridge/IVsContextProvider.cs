using System.Threading;
using System.Threading.Tasks;
using Kubuno.Shared.Mcp.Bridge.Contracts;

namespace Kubuno.Shared.Mcp.Bridge
{
    /// <summary>
    /// What the VS-side bridge (running in-proc in devenv.exe) exposes to
    /// <see cref="PipeProtocol.BridgeDispatcher"/>, one method per MCP tool. The VSIX
    /// (src/Kubuno.VisualStudio, owned by another agent - not touched here) implements this with
    /// DTE/VS services; <see cref="Dte.DteVsContextProvider"/> (net48 only) is a ready-to-use
    /// reference implementation for the five tools whose data DTE alone can supply.
    /// </summary>
    /// <remarks>
    /// Every method here is read-only by design (see docs/MCP.md "Security"): none of them can
    /// mutate the developer's solution, open documents, or debug session. Implementations should
    /// throw on genuine failure (e.g. "no active document") rather than return silently-wrong
    /// data; <see cref="PipeProtocol.BridgeDispatcher"/> turns any exception into a
    /// <see cref="BridgeErrorInfo"/> so the MCP client gets a clear message instead of a
    /// half-populated DTO.
    /// </remarks>
    public interface IVsContextProvider
    {
        /// <summary>Backs the <c>vs_active_document</c> tool.</summary>
        Task<ActiveDocumentInfo> GetActiveDocumentAsync(ActiveDocumentParams parameters, CancellationToken cancellationToken);

        /// <summary>Backs the <c>vs_selection</c> tool.</summary>
        Task<SelectionInfo> GetSelectionAsync(CancellationToken cancellationToken);

        /// <summary>Backs the <c>vs_error_list</c> tool.</summary>
        Task<ErrorListInfo> GetErrorListAsync(ErrorListParams parameters, CancellationToken cancellationToken);

        /// <summary>Backs the <c>vs_open_documents</c> tool.</summary>
        Task<OpenDocumentsInfo> GetOpenDocumentsAsync(CancellationToken cancellationToken);

        /// <summary>Backs the <c>vs_solution_or_folder</c> tool.</summary>
        Task<SolutionOrFolderInfo> GetSolutionOrFolderAsync(CancellationToken cancellationToken);

        /// <summary>Backs the <c>vs_debugger_state</c> tool.</summary>
        Task<DebuggerStateInfo> GetDebuggerStateAsync(CancellationToken cancellationToken);

        /// <summary>Backs the <c>vs_output_pane</c> tool.</summary>
        Task<OutputPaneInfo> GetOutputPaneAsync(OutputPaneParams parameters, CancellationToken cancellationToken);
    }

    /// <summary>The seven MCP tool / bridge method names, shared by the dispatcher, the pipe client and Kubuno.Shared.Mcp's tool type.</summary>
    public static class BridgeMethods
    {
        public const string ActiveDocument = "vs_active_document";
        public const string Selection = "vs_selection";
        public const string ErrorList = "vs_error_list";
        public const string OpenDocuments = "vs_open_documents";
        public const string SolutionOrFolder = "vs_solution_or_folder";
        public const string DebuggerState = "vs_debugger_state";
        public const string OutputPane = "vs_output_pane";
    }
}

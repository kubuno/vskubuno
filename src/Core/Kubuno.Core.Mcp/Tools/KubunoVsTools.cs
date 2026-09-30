using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Core.Mcp.Bridge;
using Kubuno.Core.Mcp.Bridge.Contracts;
using Kubuno.Core.Mcp.Connectivity;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Kubuno.Core.Mcp.Tools
{
    /// <summary>
    /// The MCP tools Claude Code sees: a read-only window onto what the developer is looking at in
    /// Visual Studio (active document, selection, Error List, open documents, solution/folder
    /// root, debugger state, an output pane). See docs/MCP.md "Tools" for the full contract and
    /// "Security" for why every tool here is read-only.
    /// </summary>
    /// <remarks>
    /// Each method forwards to <see cref="IBridgeConnector"/> and returns the bridge's JSON result
    /// as text (the SDK wraps a returned <see cref="string"/> in a single text content block - see
    /// the <see cref="McpServerToolAttribute"/> remarks). A <see cref="BridgeUnavailableException"/>
    /// (Visual Studio not running, or no bridge loaded) or a bridge-side failure both become an
    /// <see cref="McpException"/>, which the SDK reports to the client as a normal tool error with
    /// that message - never an unhandled crash of this process.
    /// </remarks>
    [McpServerToolType]
    public sealed class KubunoVsTools
    {
        private static readonly JsonSerializerOptions ResultTextOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        private readonly IBridgeConnector _connector;

        public KubunoVsTools(IBridgeConnector connector)
        {
            _connector = connector ?? throw new ArgumentNullException(nameof(connector));
        }

        [McpServerTool(Name = "vs_active_document", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("The document currently active/focused in the Visual Studio editor: its path, language, unsaved state, and text (optionally narrowed to a line range).")]
        public Task<string> ActiveDocumentAsync(
            [Description("1-based inclusive first line to return. Omit together with endLine to get the full document text.")] int? startLine = null,
            [Description("1-based inclusive last line to return.")] int? endLine = null,
            CancellationToken cancellationToken = default) =>
            InvokeAsync(BridgeMethods.ActiveDocument, new ActiveDocumentParams { StartLine = startLine, EndLine = endLine }, cancellationToken);

        [McpServerTool(Name = "vs_selection", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("The current text selection in the active Visual Studio document: file, line/column range, and selected text.")]
        public Task<string> SelectionAsync(CancellationToken cancellationToken) =>
            InvokeAsync(BridgeMethods.Selection, null, cancellationToken);

        [McpServerTool(Name = "vs_error_list", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("The Visual Studio Error List: severity, file, line, column and message for each item (build errors/warnings, including Cargo diagnostics reported via --message-format=json).")]
        public Task<string> ErrorListAsync(
            [Description("Caps the number of items returned (most severe/most recent first, as VS orders them). Omit for all items.")] int? maxItems = null,
            CancellationToken cancellationToken = default) =>
            InvokeAsync(BridgeMethods.ErrorList, new ErrorListParams { MaxItems = maxItems }, cancellationToken);

        [McpServerTool(Name = "vs_open_documents", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("Every document currently open as a tab in Visual Studio, with which one is active and whether each has unsaved changes.")]
        public Task<string> OpenDocumentsAsync(CancellationToken cancellationToken) =>
            InvokeAsync(BridgeMethods.OpenDocuments, null, cancellationToken);

        [McpServerTool(Name = "vs_solution_or_folder", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("What Visual Studio has open: a solution or an Open Folder workspace, its root path, and any Cargo workspace roots (Cargo.toml) detected under it.")]
        public Task<string> SolutionOrFolderAsync(CancellationToken cancellationToken) =>
            InvokeAsync(BridgeMethods.SolutionOrFolder, null, cancellationToken);

        [McpServerTool(Name = "vs_debugger_state", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("The native debugger's current state: design/run/break mode, the call stack, and the locals of the current frame when stopped at a breakpoint.")]
        public Task<string> DebuggerStateAsync(CancellationToken cancellationToken) =>
            InvokeAsync(BridgeMethods.DebuggerState, null, cancellationToken);

        [McpServerTool(Name = "vs_output_pane", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("The text currently shown in a Visual Studio Output window pane, e.g. \"Build\" or \"Kubuno\".")]
        public Task<string> OutputPaneAsync(
            [Description("Pane display name, e.g. \"Build\" or \"Kubuno\". Defaults to \"Build\".")] string? paneName = null,
            [Description("Caps the number of trailing characters returned, to keep large build logs bounded.")] int? maxLength = null,
            CancellationToken cancellationToken = default) =>
            InvokeAsync(BridgeMethods.OutputPane, new OutputPaneParams { PaneName = paneName, MaxLength = maxLength }, cancellationToken);

        private async Task<string> InvokeAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            BridgeResponse response;
            try
            {
                response = await _connector.SendAsync(method, parameters, cancellationToken).ConfigureAwait(false);
            }
            catch (BridgeUnavailableException ex)
            {
                throw new McpException(ex.Message, ex);
            }

            if (!response.Success)
            {
                throw new McpException(response.Error?.Message ?? $"The Kubuno Visual Studio bridge reported an unspecified error for '{method}'.");
            }

            JsonElement result = response.Result ?? throw new McpException($"The Kubuno Visual Studio bridge returned no data for '{method}'.");
            return JsonSerializer.Serialize(result, ResultTextOptions);
        }
    }
}

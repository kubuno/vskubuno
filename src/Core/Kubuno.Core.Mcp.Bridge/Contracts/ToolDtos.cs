using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Kubuno.Core.Mcp.Bridge.Contracts
{
    // -----------------------------------------------------------------------------------------
    // Request parameter DTOs (Kubuno.Core.Mcp -> bridge). All fields optional: a tool call with no
    // arguments still round-trips through JsonSerializer to an instance with defaults.
    // -----------------------------------------------------------------------------------------

    public sealed class ActiveDocumentParams
    {
        /// <summary>1-based inclusive start line; when null together with <see cref="EndLine"/>, the full text is returned.</summary>
        [JsonPropertyName("startLine")]
        public int? StartLine { get; set; }

        /// <summary>1-based inclusive end line.</summary>
        [JsonPropertyName("endLine")]
        public int? EndLine { get; set; }
    }

    public sealed class ErrorListParams
    {
        /// <summary>Caps the number of items returned; the bridge still reports the true total via <see cref="ErrorListInfo.TotalCount"/>.</summary>
        [JsonPropertyName("maxItems")]
        public int? MaxItems { get; set; }
    }

    public sealed class OutputPaneParams
    {
        /// <summary>Pane display name, e.g. <c>"Build"</c> or <c>"Kubuno"</c>. Defaults to <c>"Build"</c> when omitted.</summary>
        [JsonPropertyName("paneName")]
        public string? PaneName { get; set; }

        /// <summary>Caps the number of trailing characters returned, to keep large build logs bounded.</summary>
        [JsonPropertyName("maxLength")]
        public int? MaxLength { get; set; }
    }

    // -----------------------------------------------------------------------------------------
    // Result DTOs (bridge -> Kubuno.Core.Mcp -> MCP client).
    // -----------------------------------------------------------------------------------------

    public sealed class ActiveDocumentInfo
    {
        [JsonPropertyName("hasActiveDocument")]
        public bool HasActiveDocument { get; set; }

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("hasUnsavedChanges")]
        public bool HasUnsavedChanges { get; set; }

        /// <summary>Full text, or the requested line range when <see cref="ActiveDocumentParams"/> narrowed it.</summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("startLine")]
        public int? StartLine { get; set; }

        [JsonPropertyName("endLine")]
        public int? EndLine { get; set; }

        [JsonPropertyName("totalLineCount")]
        public int? TotalLineCount { get; set; }
    }

    public sealed class SelectionInfo
    {
        [JsonPropertyName("hasSelection")]
        public bool HasSelection { get; set; }

        [JsonPropertyName("filePath")]
        public string? FilePath { get; set; }

        [JsonPropertyName("isEmpty")]
        public bool IsEmpty { get; set; }

        [JsonPropertyName("startLine")]
        public int StartLine { get; set; }

        [JsonPropertyName("startColumn")]
        public int StartColumn { get; set; }

        [JsonPropertyName("endLine")]
        public int EndLine { get; set; }

        [JsonPropertyName("endColumn")]
        public int EndColumn { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    public sealed class ErrorListItem
    {
        /// <summary>"Error", "Warning" or "Message" (mirrors EnvDTE's <c>vsBuildErrorLevel</c>).</summary>
        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "Message";

        [JsonPropertyName("filePath")]
        public string? FilePath { get; set; }

        [JsonPropertyName("line")]
        public int? Line { get; set; }

        [JsonPropertyName("column")]
        public int? Column { get; set; }

        /// <summary>Diagnostic/compiler code (e.g. a rustc <c>E0308</c>) when the source distinguishes it from the message text.</summary>
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("project")]
        public string? Project { get; set; }
    }

    public sealed class ErrorListInfo
    {
        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }

        [JsonPropertyName("items")]
        public List<ErrorListItem> Items { get; set; } = new List<ErrorListItem>();
    }

    public sealed class OpenDocumentInfo
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("hasUnsavedChanges")]
        public bool HasUnsavedChanges { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }
    }

    public sealed class OpenDocumentsInfo
    {
        [JsonPropertyName("documents")]
        public List<OpenDocumentInfo> Documents { get; set; } = new List<OpenDocumentInfo>();
    }

    public sealed class SolutionOrFolderInfo
    {
        /// <summary>"Solution", "Folder" or "None" (nothing open).</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "None";

        [JsonPropertyName("rootPath")]
        public string? RootPath { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>Directories directly containing a <c>Cargo.toml</c>, found under <see cref="RootPath"/> (best-effort scan, not <c>cargo metadata</c>).</summary>
        [JsonPropertyName("cargoWorkspaceRoots")]
        public List<string> CargoWorkspaceRoots { get; set; } = new List<string>();
    }

    public sealed class StackFrameInfo
    {
        [JsonPropertyName("functionName")]
        public string? FunctionName { get; set; }

        [JsonPropertyName("filePath")]
        public string? FilePath { get; set; }

        [JsonPropertyName("line")]
        public int? Line { get; set; }

        [JsonPropertyName("isCurrent")]
        public bool IsCurrent { get; set; }
    }

    public sealed class LocalVariableInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public string? Value { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    public sealed class DebuggerStateInfo
    {
        /// <summary>"Design", "Run" or "Break" (mirrors EnvDTE's <c>dbgDebugMode</c>).</summary>
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "Design";

        [JsonPropertyName("stackFrames")]
        public List<StackFrameInfo> StackFrames { get; set; } = new List<StackFrameInfo>();

        /// <summary>Locals of the current (topmost, or selected) stack frame; null outside a break.</summary>
        [JsonPropertyName("currentFrameLocals")]
        public List<LocalVariableInfo>? CurrentFrameLocals { get; set; }
    }

    public sealed class OutputPaneInfo
    {
        [JsonPropertyName("paneName")]
        public string PaneName { get; set; } = string.Empty;

        [JsonPropertyName("found")]
        public bool Found { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("truncated")]
        public bool Truncated { get; set; }
    }
}

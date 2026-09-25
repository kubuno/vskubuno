using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Mcp.Bridge;
using Kubuno.Mcp.Bridge.Contracts;

namespace Kubuno.Mcp.Tests.Fakes
{
    /// <summary>
    /// Canned <see cref="IVsContextProvider"/> used by dispatcher and pipe end-to-end tests: no
    /// Visual Studio, no DTE - just fixed data so the pipe/dispatch plumbing can be asserted
    /// against known values.
    /// </summary>
    public sealed class FakeVsContextProvider : IVsContextProvider
    {
        public const string DocumentPath = @"C:\repo\src\lib.rs";
        public const string DocumentText = "fn main() {\n    println!(\"hi\");\n}\n";

        public Task<ActiveDocumentInfo> GetActiveDocumentAsync(ActiveDocumentParams parameters, CancellationToken cancellationToken) =>
            Task.FromResult(new ActiveDocumentInfo
            {
                HasActiveDocument = true,
                Path = DocumentPath,
                Language = "Rust",
                HasUnsavedChanges = true,
                Text = parameters.StartLine is null ? DocumentText : $"lines {parameters.StartLine}-{parameters.EndLine}",
                StartLine = parameters.StartLine,
                EndLine = parameters.EndLine,
                TotalLineCount = 3,
            });

        public Task<SelectionInfo> GetSelectionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SelectionInfo
            {
                HasSelection = true,
                FilePath = DocumentPath,
                IsEmpty = false,
                StartLine = 2,
                StartColumn = 5,
                EndLine = 2,
                EndColumn = 20,
                Text = "println!(\"hi\");",
            });

        public Task<ErrorListInfo> GetErrorListAsync(ErrorListParams parameters, CancellationToken cancellationToken)
        {
            var all = new List<ErrorListItem>
            {
                new ErrorListItem { Severity = "Error", FilePath = DocumentPath, Line = 2, Column = 5, Message = "mismatched types", Project = "kubuno-core" },
                new ErrorListItem { Severity = "Warning", FilePath = DocumentPath, Line = 1, Column = 1, Message = "unused import", Project = "kubuno-core" },
            };
            int max = parameters.MaxItems ?? all.Count;
            return Task.FromResult(new ErrorListInfo { TotalCount = all.Count, Items = all.GetRange(0, System.Math.Min(max, all.Count)) });
        }

        public Task<OpenDocumentsInfo> GetOpenDocumentsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OpenDocumentsInfo
            {
                Documents = new List<OpenDocumentInfo>
                {
                    new OpenDocumentInfo { Path = DocumentPath, IsActive = true, HasUnsavedChanges = true, Language = "Rust" },
                    new OpenDocumentInfo { Path = @"C:\repo\Cargo.toml", IsActive = false, HasUnsavedChanges = false, Language = "TOML" },
                },
            });

        public Task<SolutionOrFolderInfo> GetSolutionOrFolderAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SolutionOrFolderInfo
            {
                Kind = "Folder",
                RootPath = @"C:\repo",
                Name = "repo",
                CargoWorkspaceRoots = new List<string> { @"C:\repo", @"C:\repo\crates\core" },
            });

        public Task<DebuggerStateInfo> GetDebuggerStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DebuggerStateInfo
            {
                Mode = "Break",
                StackFrames = new List<StackFrameInfo>
                {
                    new StackFrameInfo { FunctionName = "kubuno_core::main", FilePath = DocumentPath, Line = 2, IsCurrent = true },
                    new StackFrameInfo { FunctionName = "std::rt::lang_start", FilePath = null, Line = null, IsCurrent = false },
                },
                CurrentFrameLocals = new List<LocalVariableInfo>
                {
                    new LocalVariableInfo { Name = "count", Value = "3", Type = "i32" },
                },
            });

        public Task<OutputPaneInfo> GetOutputPaneAsync(OutputPaneParams parameters, CancellationToken cancellationToken) =>
            Task.FromResult(new OutputPaneInfo
            {
                PaneName = parameters.PaneName ?? "Build",
                Found = true,
                Text = "Build started...\nBuild succeeded.",
                Truncated = false,
            });
    }
}

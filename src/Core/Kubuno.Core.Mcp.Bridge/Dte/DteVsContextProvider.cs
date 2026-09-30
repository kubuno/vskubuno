using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Kubuno.Core.Mcp.Bridge.Contracts;

namespace Kubuno.Core.Mcp.Bridge.Dte
{
    /// <summary>
    /// Reference <see cref="IVsContextProvider"/> implementation built entirely on
    /// <c>EnvDTE</c>/<c>EnvDTE80</c> - the same automation surface the classic VSSDK macro/add-in
    /// model has always exposed, chosen here because it needs no extra VS service wiring beyond a
    /// <see cref="DTE2"/> instance, which the VSIX package already has at hand
    /// (<c>GetServiceAsync(typeof(SDTE))</c>) and can pass to this class's constructor when it
    /// starts the bridge (see docs/MCP.md "Integration"). This class is not itself referenced by
    /// the VSIX yet - that wiring is the VSIX owner's job - but it compiles against the exact same
    /// <c>Microsoft.VisualStudio.SDK</c>/<c>EnvDTE</c> package versions as
    /// src/Kubuno.VisualStudio/Kubuno.VisualStudio.csproj, so it is ready to use as-is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Threading:</b> DTE objects live on the main (UI) thread's single-threaded apartment.
    /// <see cref="VsMcpBridgeHost"/> dispatches each pipe request from a background task, so every
    /// method here starts with <c>await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(...)</c>
    /// before touching any DTE object.
    /// </para>
    /// <para>
    /// <b>Known EnvDTE gaps (not worked around, to avoid inventing data the debugger did not
    /// report):</b> <see cref="EnvDTE.StackFrame"/> exposes no file/line members, so
    /// <see cref="GetDebuggerStateAsync"/> can only attach a location to the current frame, and
    /// only when the break was caused by actually hitting a breakpoint
    /// (<see cref="EnvDTE.Debugger.BreakpointLastHit"/>) rather than a step or an exception.
    /// <see cref="EnvDTE80.ErrorItem"/> has no dedicated diagnostic-code field separate from its
    /// description text, so <see cref="ErrorListItem.Code"/> is always left <see langword="null"/>
    /// by <see cref="GetErrorListAsync"/>.
    /// </para>
    /// </remarks>
    public sealed class DteVsContextProvider : IVsContextProvider
    {
        private static readonly string[] SkippedDirectoryNames = { "target", "node_modules", "bin", "obj" };
        private const int MaxCargoWorkspaceResults = 25;
        private const int MaxCargoScanDepth = 4;
        private const int DefaultMaxOutputPaneLength = 200_000;

        private readonly DTE2 _dte;

        public DteVsContextProvider(DTE2 dte)
        {
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
        }

        public async Task<ActiveDocumentInfo> GetActiveDocumentAsync(ActiveDocumentParams parameters, CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Document active = _dte.ActiveDocument;
            if (active is null)
            {
                return new ActiveDocumentInfo { HasActiveDocument = false };
            }

            var info = new ActiveDocumentInfo
            {
                HasActiveDocument = true,
                Path = active.FullName,
                Language = active.Language,
                HasUnsavedChanges = !active.Saved,
            };

            if (active.Object("TextDocument") is TextDocument textDocument)
            {
                info.TotalLineCount = textDocument.EndPoint.Line;

                if (parameters.StartLine is int startLine && parameters.EndLine is int endLine
                    && startLine > 0 && endLine >= startLine)
                {
                    int clampedStart = Math.Min(startLine, info.TotalLineCount.Value);
                    int clampedEnd = Math.Min(endLine, info.TotalLineCount.Value);

                    EditPoint rangeStart = textDocument.StartPoint.CreateEditPoint();
                    rangeStart.LineDown(clampedStart - 1);
                    rangeStart.StartOfLine();

                    EditPoint rangeEnd = textDocument.StartPoint.CreateEditPoint();
                    rangeEnd.LineDown(clampedEnd - 1);
                    rangeEnd.EndOfLine();

                    info.Text = rangeStart.GetText(rangeEnd);
                    info.StartLine = clampedStart;
                    info.EndLine = clampedEnd;
                }
                else
                {
                    info.Text = textDocument.StartPoint.CreateEditPoint().GetText(textDocument.EndPoint);
                }
            }

            return info;
        }

        public async Task<SelectionInfo> GetSelectionAsync(CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Document active = _dte.ActiveDocument;
            if (active?.Selection is not TextSelection selection)
            {
                return new SelectionInfo { HasSelection = false };
            }

            return new SelectionInfo
            {
                HasSelection = true,
                FilePath = active.FullName,
                IsEmpty = selection.IsEmpty,
                StartLine = selection.TopPoint.Line,
                StartColumn = selection.TopPoint.LineCharOffset,
                EndLine = selection.BottomPoint.Line,
                EndColumn = selection.BottomPoint.LineCharOffset,
                Text = selection.Text,
            };
        }

        public async Task<ErrorListInfo> GetErrorListAsync(ErrorListParams parameters, CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            ErrorItems errorItems = _dte.ToolWindows.ErrorList.ErrorItems;
            int total = errorItems.Count;
            int max = parameters.MaxItems is int requested && requested > 0 ? Math.Min(requested, total) : total;

            var info = new ErrorListInfo { TotalCount = total };
            // EnvDTE collections are 1-based.
            for (int i = 1; i <= max; i++)
            {
                ErrorItem item = errorItems.Item(i);
                info.Items.Add(new ErrorListItem
                {
                    Severity = ToSeverityString(item.ErrorLevel),
                    FilePath = string.IsNullOrEmpty(item.FileName) ? null : item.FileName,
                    Line = item.Line > 0 ? item.Line : (int?)null,
                    Column = item.Column > 0 ? item.Column : (int?)null,
                    Message = item.Description ?? string.Empty,
                    Project = string.IsNullOrEmpty(item.Project) ? null : item.Project,
                    // Code intentionally left null: see the "Known EnvDTE gaps" remarks on this class.
                });
            }

            return info;
        }

        public async Task<OpenDocumentsInfo> GetOpenDocumentsAsync(CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            string? activePath = _dte.ActiveDocument?.FullName;
            var info = new OpenDocumentsInfo();

            foreach (Document doc in _dte.Documents)
            {
                info.Documents.Add(new OpenDocumentInfo
                {
                    Path = doc.FullName,
                    // EnvDTE.Document has no "is active" flag; comparing FullName to the current
                    // ActiveDocument is the standard best-effort substitute.
                    IsActive = !string.IsNullOrEmpty(activePath)
                        && string.Equals(doc.FullName, activePath, StringComparison.OrdinalIgnoreCase),
                    HasUnsavedChanges = !doc.Saved,
                    Language = doc.Language,
                });
            }

            return info;
        }

        public async Task<SolutionOrFolderInfo> GetSolutionOrFolderAsync(CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var info = new SolutionOrFolderInfo();
            Solution solution = _dte.Solution;
            if (solution is null || !solution.IsOpen)
            {
                return info;
            }

            string fullName = solution.FullName ?? string.Empty;
            if (!string.IsNullOrEmpty(fullName) && fullName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) && File.Exists(fullName))
            {
                info.Kind = "Solution";
                info.RootPath = Path.GetDirectoryName(fullName);
                info.Name = Path.GetFileName(fullName);
            }
            else if (!string.IsNullOrEmpty(fullName) && Directory.Exists(fullName))
            {
                // Open Folder mode: recent VS reports the opened folder itself as Solution.FullName.
                info.Kind = "Folder";
                info.RootPath = fullName;
                info.Name = Path.GetFileName(fullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            else if (!string.IsNullOrEmpty(fullName))
            {
                // Fallback for VS versions that still synthesize a non-existent .sln path for a folder.
                info.Kind = "Folder";
                info.RootPath = Path.GetDirectoryName(fullName);
                info.Name = info.RootPath is not null ? Path.GetFileName(info.RootPath) : null;
            }

            if (info.RootPath is not null && Directory.Exists(info.RootPath))
            {
                info.CargoWorkspaceRoots = FindCargoWorkspaceRoots(info.RootPath);
            }

            return info;
        }

        public async Task<DebuggerStateInfo> GetDebuggerStateAsync(CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Debugger debugger = _dte.Debugger;
            var state = new DebuggerStateInfo { Mode = ToModeString(debugger.CurrentMode) };

            if (debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
            {
                return state;
            }

            StackFrame currentFrame = debugger.CurrentStackFrame;

            // Best-effort location for the current frame only - see the "Known EnvDTE gaps"
            // remarks on this class for why other frames never get a FilePath/Line.
            Breakpoint lastHit = debugger.BreakpointLastHit;
            string? currentFilePath = !string.IsNullOrEmpty(lastHit?.File) ? lastHit?.File : null;
            int? currentLine = lastHit is not null && lastHit.FileLine > 0 ? lastHit.FileLine : (int?)null;

            // Stack frames live on the current thread, not on EnvDTE.Program (EnvDTE.Thread is
            // spelled out here to avoid ambiguity with System.Threading.Thread).
            if (debugger.CurrentThread is EnvDTE.Thread thread)
            {
                bool first = true;
                foreach (StackFrame frame in thread.StackFrames)
                {
                    bool isCurrent = currentFrame is not null
                        ? string.Equals(frame.FunctionName, currentFrame.FunctionName, StringComparison.Ordinal) && first
                        : first;

                    state.StackFrames.Add(new StackFrameInfo
                    {
                        FunctionName = frame.FunctionName,
                        FilePath = isCurrent ? currentFilePath : null,
                        Line = isCurrent ? currentLine : null,
                        IsCurrent = isCurrent,
                    });
                    first = false;
                }
            }

            if (currentFrame is not null)
            {
                state.CurrentFrameLocals = new List<LocalVariableInfo>();
                foreach (Expression local in currentFrame.Locals)
                {
                    state.CurrentFrameLocals.Add(new LocalVariableInfo
                    {
                        Name = local.Name,
                        Value = local.Value,
                        Type = local.Type,
                    });
                }
            }

            return state;
        }

        public async Task<OutputPaneInfo> GetOutputPaneAsync(OutputPaneParams parameters, CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            string paneName = string.IsNullOrEmpty(parameters.PaneName) ? "Build" : parameters.PaneName!;
            var info = new OutputPaneInfo { PaneName = paneName };

            OutputWindowPanes panes = _dte.ToolWindows.OutputWindow.OutputWindowPanes;
            OutputWindowPane? pane = null;
            foreach (OutputWindowPane candidate in panes)
            {
                if (string.Equals(candidate.Name, paneName, StringComparison.OrdinalIgnoreCase))
                {
                    pane = candidate;
                    break;
                }
            }

            if (pane is null)
            {
                info.Found = false;
                return info;
            }

            info.Found = true;
            TextDocument document = pane.TextDocument;
            string text = document.StartPoint.CreateEditPoint().GetText(document.EndPoint);

            int maxLength = parameters.MaxLength is int requested && requested > 0 ? requested : DefaultMaxOutputPaneLength;
            if (text.Length > maxLength)
            {
                info.Text = text.Substring(text.Length - maxLength);
                info.Truncated = true;
            }
            else
            {
                info.Text = text;
                info.Truncated = false;
            }

            return info;
        }

        private static string ToModeString(dbgDebugMode mode) => mode switch
        {
            dbgDebugMode.dbgRunMode => "Run",
            dbgDebugMode.dbgBreakMode => "Break",
            _ => "Design",
        };

        private static string ToSeverityString(vsBuildErrorLevel level) => level switch
        {
            vsBuildErrorLevel.vsBuildErrorLevelHigh => "Error",
            vsBuildErrorLevel.vsBuildErrorLevelMedium => "Warning",
            _ => "Message",
        };

        /// <summary>
        /// Directories containing a <c>Cargo.toml</c>, found by a depth- and result-bounded
        /// breadth-first scan that skips common heavy/build directories. Intentionally not
        /// <c>cargo metadata</c> (that requires invoking cargo itself; Kubuno.Rust.Cargo already owns
        /// that for the workspace VS has actually opened) - this is a cheap, dependency-free
        /// approximation good enough to point Claude at candidate workspace roots.
        /// </summary>
        private static List<string> FindCargoWorkspaceRoots(string root)
        {
            var results = new List<string>();
            var pending = new Queue<(string Path, int Depth)>();
            pending.Enqueue((root, 0));

            while (pending.Count > 0 && results.Count < MaxCargoWorkspaceResults)
            {
                (string current, int depth) = pending.Dequeue();

                try
                {
                    if (File.Exists(Path.Combine(current, "Cargo.toml")))
                    {
                        results.Add(current);
                    }

                    if (depth >= MaxCargoScanDepth)
                    {
                        continue;
                    }

                    foreach (string subdirectory in Directory.EnumerateDirectories(current))
                    {
                        string name = Path.GetFileName(subdirectory);
                        if (name.StartsWith(".", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        bool skip = false;
                        foreach (string skipped in SkippedDirectoryNames)
                        {
                            if (string.Equals(name, skipped, StringComparison.OrdinalIgnoreCase))
                            {
                                skip = true;
                                break;
                            }
                        }

                        if (!skip)
                        {
                            pending.Enqueue((subdirectory, depth + 1));
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }

            return results;
        }
    }
}

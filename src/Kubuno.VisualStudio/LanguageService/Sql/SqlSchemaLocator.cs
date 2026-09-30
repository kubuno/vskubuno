using System;
using Kubuno.VisualStudio.Core.Sql;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;

namespace Kubuno.VisualStudio.LanguageService.Sql
{
    /// <summary>
    /// The schema a Rust buffer's SQL is checked and completed against: the crate of the file (nearest <c>Cargo.toml</c>),
    /// the connections its <c>.kbdata</c> files name, and their snapshots under the Open Folder workspace or the solution
    /// directory (<see cref="SchemaSnapshotStore.ResolveRoot"/>). Resolved once per buffer and file path.
    /// </summary>
    internal static class SqlSchemaLocator
    {
        private static string? _solutionDirectory;

        public static SqlSchemaSource? For(ITextBuffer buffer, ITextDocumentFactoryService documents, IVsFolderWorkspaceService? workspace)
        {
            if (!documents.TryGetTextDocument(buffer, out var document) || string.IsNullOrEmpty(document.FilePath))
            {
                return null;
            }

            var path = document.FilePath;
            if (buffer.Properties.TryGetProperty(typeof(SqlSchemaLocator), out Tuple<string, SqlSchemaSource?> cached) && string.Equals(cached.Item1, path, StringComparison.OrdinalIgnoreCase))
            {
                return cached.Item2;
            }

            SqlSchemaSource? source = null;
            try
            {
                var crate = KbdataConnections.FindCrateDirectory(path);
                if (crate != null)
                {
                    string? folder = null;
                    try
                    {
                        folder = workspace?.CurrentWorkspace?.Location;
                    }
                    catch (InvalidOperationException)
                    {
                        folder = null;
                    }

                    var root = SchemaSnapshotStore.ResolveRoot(path, new[] { folder, SolutionDirectory() }) ?? crate;
                    source = SqlSchemaSource.GetOrCreate(crate, root);
                }
            }
            catch (Exception exception) when (exception is ArgumentException or System.IO.IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Logging.KubunoLog.WriteLine("SQL IntelliSense: no schema for " + path + ": " + exception.Message);
                source = null;
            }

            buffer.Properties[typeof(SqlSchemaLocator)] = Tuple.Create(path, source);
            return source;
        }

        /// <summary>The open solution's directory (read on the UI thread; the last value seen elsewhere).</summary>
        private static string? SolutionDirectory()
        {
            if (!ThreadHelper.CheckAccess())
            {
                return _solutionDirectory;
            }

#pragma warning disable VSTHRD010 // Guarded by ThreadHelper.CheckAccess() above: the caller may be a background completion thread.
            if (Package.GetGlobalService(typeof(SVsSolution)) is IVsSolution solution
                && solution.GetSolutionInfo(out var directory, out _, out _) == VSConstants.S_OK
                && !string.IsNullOrEmpty(directory))
            {
                _solutionDirectory = directory;
            }
#pragma warning restore VSTHRD010

            return _solutionDirectory;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Kubuno.Shared.DevAssistant.Logic.Changes;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;

namespace Kubuno.Shared.DevAssistant.Changes
{
    /// <summary>
    /// Applies change sets to Visual Studio's own text buffers (docs/AI-ASSISTANT.md section 5.5), never behind the
    /// editor's back on disk: an open document is edited in its buffer (the designer of a <c>.kbview</c> follows its
    /// buffer); a file that is not open is opened first, so dirty tracking, the running document table and the undo
    /// history stay right. All replacements of a file go into one <see cref="ITextEdit"/> inside one named undo
    /// transaction; several files are wrapped in one linked undo transaction - one Ctrl+Z undoes the whole change set.
    /// UI thread only.
    /// </summary>
    internal sealed class VsChangeSetBufferHost : IChangeSetBufferHost
    {
        private readonly IVsEditorAdaptersFactoryService _adapters;
        private readonly ITextUndoHistoryRegistry _undoRegistry;
        private string _description = "Kubuno Dev Assistant";

        public VsChangeSetBufferHost()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var componentModel = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
            _adapters = componentModel.GetService<IVsEditorAdaptersFactoryService>();
            _undoRegistry = componentModel.GetService<ITextUndoHistoryRegistry>();
        }

        /// <summary>The text of an open document's buffer, or null when the document is not open.</summary>
        public string? GetOpenBufferText(string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return FindOpenBuffer(path)?.CurrentSnapshot.GetText();
        }

        public string? GetCurrentText(string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var open = GetOpenBufferText(path);
            if (open is not null)
            {
                return open;
            }

            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }

        public IDisposable BeginUndoUnit(string description, IReadOnlyList<string> paths)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _description = description;
            if (paths.Count <= 1)
            {
                return new Scope(null);
            }

            // Open every buffer first: a linked transaction only spans documents that are open in an editor.
            foreach (var path in paths)
            {
                EnsureBuffer(path);
            }

            var manager = Package.GetGlobalService(typeof(SVsLinkedUndoTransactionManager)) as IVsLinkedUndoTransactionManager;
            if (manager is null || ErrorHandler.Failed(manager.OpenLinkedUndo((uint)LinkedTransactionFlags2.mdtGlobal, description)))
            {
                return new Scope(null);
            }

            return new Scope(manager);
        }

        public void Replace(string path, IReadOnlyList<TextReplacement> replacements)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var buffer = EnsureBuffer(path) ?? throw new InvalidOperationException("Cannot open " + path + " in an editor.");
            ITextUndoTransaction? transaction = null;
            if (_undoRegistry.TryGetHistory(buffer, out var history))
            {
                transaction = history.CreateTransaction(_description);
            }

            try
            {
                using (var edit = buffer.CreateEdit())
                {
                    foreach (var replacement in replacements.OrderByDescending(r => r.Start))
                    {
                        edit.Replace(replacement.Start, replacement.Length, replacement.NewText);
                    }

                    edit.Apply();
                }

                transaction?.Complete();
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        public void CreateFile(string path, string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, path);
        }

        /// <summary>The buffer of an open document, or null.</summary>
        private ITextBuffer? FindOpenBuffer(string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var rdt = Package.GetGlobalService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
            if (rdt is null)
            {
                return null;
            }

            var docData = IntPtr.Zero;
            try
            {
                if (ErrorHandler.Failed(rdt.FindAndLockDocument((uint)_VSRDTFLAGS.RDT_NoLock, path, out _, out _, out docData, out _)) || docData == IntPtr.Zero)
                {
                    return null;
                }

                var data = System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(docData);
                var textBuffer = data as IVsTextBuffer ?? (data as IVsTextBufferProvider) switch
                {
                    { } provider when ErrorHandler.Succeeded(provider.GetTextBuffer(out var lines)) => lines,
                    _ => null,
                };
                return textBuffer is null ? null : _adapters.GetDocumentBuffer(textBuffer);
            }
            finally
            {
                if (docData != IntPtr.Zero)
                {
                    System.Runtime.InteropServices.Marshal.Release(docData);
                }
            }
        }

        /// <summary>The buffer of <paramref name="path"/>, opening the document in its default editor when needed.</summary>
        private ITextBuffer? EnsureBuffer(string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var buffer = FindOpenBuffer(path);
            if (buffer is not null)
            {
                return buffer;
            }

            VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, path);
            return FindOpenBuffer(path);
        }

        private sealed class Scope : IDisposable
        {
            private IVsLinkedUndoTransactionManager? _manager;

            public Scope(IVsLinkedUndoTransactionManager? manager)
            {
                _manager = manager;
            }

            public void Dispose()
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                _manager?.CloseLinkedUndo();
                _manager = null;
            }
        }
    }
}

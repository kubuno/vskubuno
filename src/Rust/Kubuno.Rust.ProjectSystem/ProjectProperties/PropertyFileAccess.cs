using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Kubuno.VisualStudio.Core.ProjectProperties;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace Kubuno.VisualStudio.RustProjectSystem.ProjectProperties
{
    /// <summary>
    /// Reads for property VALUES (any thread, called for every property of every page): the saved file on disk,
    /// cached by timestamp and size. The editor writes through <see cref="PropertyTextBufferWriter"/>, which
    /// saves the files it edits, so the disk is current unless the developer has unsaved edits of their own in
    /// an open Cargo.toml - those are read by the writer before any change, never overwritten.
    /// </summary>
    internal sealed class CachedDiskPropertyFileReader : IPropertyFileReader
    {
        public static CachedDiskPropertyFileReader Instance { get; } = new CachedDiskPropertyFileReader();

        private readonly ConcurrentDictionary<string, (DateTime Stamp, long Length, string Text)> _cache =
            new ConcurrentDictionary<string, (DateTime, long, string)>(StringComparer.OrdinalIgnoreCase);

        public string? ReadText(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return null;
                }
                if (_cache.TryGetValue(path, out var cached) && cached.Stamp == info.LastWriteTimeUtc && cached.Length == info.Length)
                {
                    return cached.Text;
                }
                string text = File.ReadAllText(path);
                _cache[path] = (info.LastWriteTimeUtc, info.Length, text);
                return text;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        public bool FileExists(string path) => File.Exists(path);

        public IReadOnlyList<string> GetFiles(string directory, string searchPattern) =>
            FileSystemPropertyFileReader.Instance.GetFiles(directory, searchPattern);
    }

    /// <summary>
    /// Reads for property WRITES (UI thread): the text of an open document's buffer, including unsaved changes,
    /// else the file on disk - so an edit is always computed against the exact text it is applied to.
    /// </summary>
    internal sealed class BufferAwarePropertyFileReader : IPropertyFileReader
    {
        public string? ReadText(string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsTextLines? open = PropertyTextBufferWriter.TryGetOpenBuffer(path);
            return open != null ? PropertyTextBufferWriter.GetText(open) : FileSystemPropertyFileReader.Instance.ReadText(path);
        }

        public bool FileExists(string path) => File.Exists(path) || PropertyTextBufferWriter.TryGetOpenBuffer(path) != null;

        public IReadOnlyList<string> GetFiles(string directory, string searchPattern) =>
            FileSystemPropertyFileReader.Instance.GetFiles(directory, searchPattern);
    }

    /// <summary>
    /// Applies the property model's <see cref="FileTextEdit"/>s through Visual Studio's text buffers (docs/RSPROJ.md,
    /// "Project properties like .NET"): the document's own buffer when it is open, otherwise an invisible editor on
    /// it. Each change is ONE <c>IVsTextLines.ReplaceLines</c> call, so it is one undo unit of that document (open
    /// Cargo.toml, Ctrl+Z restores the previous value), and only the edited span changes. A document that had no
    /// unsaved changes is saved afterwards (cargo reads the file, not the buffer); one that already had unsaved
    /// changes of the developer is left dirty, with the edit applied on top of them.
    /// </summary>
    internal static class PropertyTextBufferWriter
    {
        private static readonly Guid IVsTextLinesGuid = typeof(IVsTextLines).GUID;

        /// <summary>Applies <paramref name="edits"/> in order (UI thread).</summary>
        public static void Apply(IReadOnlyList<FileTextEdit> edits)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (FileTextEdit edit in edits)
            {
                if (edit.CreatesFile && !File.Exists(edit.Path) && TryGetOpenBuffer(edit.Path) is null)
                {
                    // A new file (rustfmt.toml): nothing to undo into, the project's file glob picks it up.
                    string created = edit.Edit.ApplyTo(string.Empty);
                    File.WriteAllText(edit.Path, created, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    continue;
                }
                ApplyToBuffer(edit.Path, edit.Edit);
            }
        }

        /// <summary>The buffer of <paramref name="path"/> when it is open in an editor, else null (UI thread).</summary>
        public static IVsTextLines? TryGetOpenBuffer(string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsRunningDocumentTable)) is not IVsRunningDocumentTable rdt)
            {
                return null;
            }

            int hr = rdt.FindAndLockDocument((uint)_VSRDTFLAGS.RDT_NoLock, path, out _, out _, out IntPtr docData, out _);
            if (ErrorHandler.Failed(hr) || docData == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                object data = Marshal.GetObjectForIUnknown(docData);
                if (data is IVsTextLines lines)
                {
                    return lines;
                }
                if (data is IVsTextBufferProvider provider && ErrorHandler.Succeeded(provider.GetTextBuffer(out IVsTextLines provided)))
                {
                    return provided;
                }
                return null;
            }
            finally
            {
                Marshal.Release(docData);
            }
        }

        /// <summary>The whole text of a buffer.</summary>
        public static string GetText(IVsTextLines lines)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ErrorHandler.ThrowOnFailure(lines.GetLastLineIndex(out int lastLine, out int lastIndex));
            ErrorHandler.ThrowOnFailure(lines.GetLineText(0, 0, lastLine, lastIndex, out string text));
            return text;
        }

        private static void ApplyToBuffer(string path, TextEdit edit)
        {
            IVsTextLines? lines = TryGetOpenBuffer(path);
            IVsInvisibleEditor? invisibleEditor = null;
            IntPtr invisibleDocData = IntPtr.Zero;
            try
            {
                if (lines is null)
                {
                    var manager = (IVsInvisibleEditorManager)ServiceProvider.GlobalProvider.GetService(typeof(SVsInvisibleEditorManager));
                    ErrorHandler.ThrowOnFailure(manager.RegisterInvisibleEditor(path, null, (uint)_EDITORREGFLAGS.RIEF_ENABLECACHING, null, out invisibleEditor));
                    Guid iid = IVsTextLinesGuid;
                    ErrorHandler.ThrowOnFailure(invisibleEditor.GetDocData(1, ref iid, out invisibleDocData));
                    lines = (IVsTextLines)Marshal.GetObjectForIUnknown(invisibleDocData);
                }

                var persist = lines as IVsPersistDocData;
                bool wasDirty = persist != null && ErrorHandler.Succeeded(persist.IsDocDataDirty(out int dirty)) && dirty != 0;

                ErrorHandler.ThrowOnFailure(lines.GetLineIndexOfPosition(edit.Start, out int startLine, out int startColumn));
                ErrorHandler.ThrowOnFailure(lines.GetLineIndexOfPosition(edit.Start + edit.OldLength, out int endLine, out int endColumn));
                IntPtr text = Marshal.StringToCoTaskMemUni(edit.NewText);
                try
                {
                    ErrorHandler.ThrowOnFailure(lines.ReplaceLines(startLine, startColumn, endLine, endColumn, text, edit.NewText.Length, null));
                }
                finally
                {
                    Marshal.FreeCoTaskMem(text);
                }

                if (!wasDirty && persist != null)
                {
                    ErrorHandler.ThrowOnFailure(persist.SaveDocData(VSSAVEFLAGS.VSSAVE_SilentSave, out _, out int canceled));
                }
            }
            finally
            {
                if (invisibleDocData != IntPtr.Zero)
                {
                    Marshal.Release(invisibleDocData);
                }
                if (invisibleEditor != null)
                {
                    // Releasing the last reference unregisters the invisible editor (and closes a document nobody else holds).
                    Marshal.ReleaseComObject(invisibleEditor);
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Kubuno.Views.Logic.Resources;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.Views.Resources.Editor
{
    /// <summary>
    /// Keeps the text buffers of a resource set in step with its <see cref="ResourceSetModel"/>. The neutral file's
    /// buffer is the editor's document; each satellite (<c>name.fr.kbres</c>) is opened through an invisible editor,
    /// which makes it a real, dirty-tracked document saved by Save All. Model changes are pushed into the buffers
    /// (only where the text differs, as a minimal edit); edits made to a buffer from elsewhere (the XML view, source
    /// control reloads) reload the model, with a re-entrancy guard both ways.
    /// </summary>
    internal sealed class KbresDocumentSession : IDisposable
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private readonly IVsTextLines _neutralLines;
        private readonly string _neutralPath;
        private readonly ServiceProvider _services;
        private readonly IVsEditorAdaptersFactoryService _adapters;
        private readonly Action<string> _reportError;
        private readonly Dictionary<string, BufferEntry> _entries = new Dictionary<string, BufferEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer _reloadTimer;
        private BufferLoadedSink? _loadSink;
        private int _pushing;
        private bool _reloading;
        private bool _disposed;

        public KbresDocumentSession(IVsTextLines neutralLines, string neutralPath, OleInterop.IServiceProvider ole, Action<string> reportError)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _neutralLines = neutralLines;
            _neutralPath = neutralPath;
            _services = new ServiceProvider(ole);
            _adapters = KbresEditorFactory.GetEditorAdapters(ole);
            _reportError = reportError;
            _reloadTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
            _reloadTimer.Tick += (_, _) =>
            {
                _reloadTimer.Stop();
                ReloadFromBuffers();
            };
        }

        /// <summary>The model, once the document is loaded (null before).</summary>
        public ResourceSetModel? Model { get; private set; }

        /// <summary>Raised once, when <see cref="Model"/> has been created.</summary>
        public event EventHandler? ModelReady;

        /// <summary>Creates the model now when the document is loaded, else as soon as the shell loads it.</summary>
        public void Start()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (TryInitialize())
            {
                return;
            }

            _loadSink = BufferLoadedSink.TryAdvise(_neutralLines, () =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                _loadSink?.Dispose();
                _loadSink = null;
                TryInitialize();
            });
        }

        private bool TryInitialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Model is not null || _disposed)
            {
                return true;
            }

            ITextBuffer? neutral;
            try
            {
                neutral = _adapters.GetDataBuffer(_neutralLines);
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException)
            {
                neutral = null;
            }

            if (neutral is null)
            {
                return false;
            }

            var entry = Track(_neutralPath, neutral, _neutralLines, editor: null);
            var satellites = new List<(string Culture, string Text)>();
            foreach (var (culture, path) in ResourceNames.Satellites(_neutralPath))
            {
                try
                {
                    var sat = OpenSatellite(path);
                    satellites.Add((culture, sat.Text()));
                }
                catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    _reportError(ResourceText.SatelliteOpenFailed(path, ex.Message));
                }
            }

            Model = new ResourceSetModel(_neutralPath, NeutralText(entry), satellites);
            Model.Changed += OnModelChanged;
            ModelReady?.Invoke(this, EventArgs.Empty);
            return true;
        }

        // ---- Buffers ----

        private BufferEntry Track(string path, ITextBuffer buffer, IVsTextLines lines, IVsInvisibleEditor? editor)
        {
            var entry = new BufferEntry(path, buffer, lines, editor);
            entry.Buffer.PostChanged += OnBufferChanged;
            _entries[path] = entry;
            return entry;
        }

        private BufferEntry OpenSatellite(string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_entries.TryGetValue(path, out var known))
            {
                return known;
            }

            if (!File.Exists(path))
            {
                File.WriteAllText(path, new KbresFile().ToText(), Utf8);
            }

            var manager = _services.GetService(typeof(SVsInvisibleEditorManager)) as IVsInvisibleEditorManager
                ?? throw new InvalidOperationException("the invisible editor manager is unavailable");
            ErrorHandler.ThrowOnFailure(manager.RegisterInvisibleEditor(path, null, (uint)_EDITORREGFLAGS.RIEF_ENABLECACHING, null, out var editor));
            var iid = typeof(IVsTextLines).GUID;
            ErrorHandler.ThrowOnFailure(editor.GetDocData(0, ref iid, out var pointer));
            IVsTextLines lines;
            try
            {
                lines = (IVsTextLines)Marshal.GetObjectForIUnknown(pointer);
            }
            finally
            {
                Marshal.Release(pointer);
            }

            var buffer = _adapters.GetDataBuffer(lines) ?? throw new InvalidOperationException("the culture file has no text buffer");
            return Track(path, buffer, lines, editor);
        }

        private static string NeutralText(BufferEntry neutral)
        {
            var text = neutral.Text();
            return string.IsNullOrWhiteSpace(text) ? new KbresFile().ToText() : text;
        }

        // ---- Model -> buffers ----

        private void OnModelChanged(object? sender, EventArgs e)
        {
            if (_reloading || Model is null || _disposed)
            {
                return;
            }

            ThreadHelper.ThrowIfNotOnUIThread();
            _pushing++;
            try
            {
                var texts = Model.Texts();
                foreach (var pair in texts)
                {
                    BufferEntry? entry;
                    try
                    {
                        entry = pair.Key.Equals(_neutralPath, StringComparison.OrdinalIgnoreCase) ? _entries[_neutralPath] : OpenSatellite(pair.Key);
                    }
                    catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        _reportError(ResourceText.SatelliteOpenFailed(pair.Key, ex.Message));
                        continue;
                    }

                    Push(entry, pair.Value);
                }

                // A culture file the model no longer has (an undone "Add culture") goes back to an empty set.
                var empty = new KbresFile().ToText();
                foreach (var entry in _entries.Values.Where(x => !texts.ContainsKey(x.Path)).ToList())
                {
                    Push(entry, empty);
                }
            }
            finally
            {
                _pushing--;
            }
        }

        private void Push(BufferEntry entry, string text)
        {
            var current = entry.Text();
            var target = current.Contains("\r\n") ? text.Replace("\n", "\r\n") : text;
            if (current == target)
            {
                return;
            }

            if (!entry.Buffer.CheckEditAccess())
            {
                _reportError(ResourceText.CannotCreateFile(entry.Path, "read-only"));
                return;
            }

            var max = Math.Min(current.Length, target.Length);
            var prefix = 0;
            while (prefix < max && current[prefix] == target[prefix])
            {
                prefix++;
            }

            var suffix = 0;
            while (suffix < max - prefix && current[current.Length - 1 - suffix] == target[target.Length - 1 - suffix])
            {
                suffix++;
            }

            entry.Buffer.Replace(new Span(prefix, current.Length - prefix - suffix), target.Substring(prefix, target.Length - prefix - suffix));
        }

        // ---- Buffers -> model ----

        private void OnBufferChanged(object? sender, EventArgs e)
        {
            if (_pushing > 0 || _reloading || _disposed)
            {
                return;
            }

            _reloadTimer.Stop();
            _reloadTimer.Start();
        }

        private void ReloadFromBuffers()
        {
            if (Model is null || _disposed || _pushing > 0)
            {
                return;
            }

            ThreadHelper.ThrowIfNotOnUIThread();
            var satellites = new List<(string Culture, string Text)>();
            foreach (var entry in _entries.Values.Where(x => !x.Path.Equals(_neutralPath, StringComparison.OrdinalIgnoreCase)))
            {
                var split = ResourceNames.SplitFileName(Path.GetFileName(entry.Path));
                if (split?.Culture is not { } culture)
                {
                    continue;
                }

                var text = entry.Text();
                if (!Model.Cultures.Contains(culture) && KbresFile.Parse(text).Entries.Count == 0)
                {
                    continue;
                }

                satellites.Add((culture, text));
            }

            _reloading = true;
            try
            {
                Model.Reload(NeutralText(_entries[_neutralPath]), satellites);
            }
            finally
            {
                _reloading = false;
            }
        }

        // ---- Saving ----

        /// <summary>True when the neutral file or any culture file has unsaved changes.</summary>
        public bool AnyDirty()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _entries.Values.Any(e => e.IsDirty());
        }

        /// <summary>Saves the neutral file and every dirty culture file (through the running document table).</summary>
        public void SaveAll()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_services.GetService(typeof(SVsRunningDocumentTable)) is not IVsRunningDocumentTable rdt)
            {
                return;
            }

            foreach (var entry in _entries.Values.Where(e => e.IsDirty()).ToList())
            {
                if (ErrorHandler.Failed(rdt.FindAndLockDocument((uint)_VSRDTFLAGS.RDT_NoLock, entry.Path, out var hierarchy, out var itemId, out var docData, out var cookie)))
                {
                    continue;
                }

                if (docData != IntPtr.Zero)
                {
                    Marshal.Release(docData);
                }

                rdt.SaveDocuments((uint)__VSRDTSAVEOPTIONS.RDTSAVEOPT_SaveIfDirty, hierarchy, itemId, cookie);
            }
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _reloadTimer.Stop();
            _loadSink?.Dispose();
            if (Model is not null)
            {
                Model.Changed -= OnModelChanged;
            }

            foreach (var entry in _entries.Values)
            {
                entry.Buffer.PostChanged -= OnBufferChanged;
                if (entry.Editor is not null)
                {
                    try
                    {
                        Marshal.ReleaseComObject(entry.Editor);
                    }
                    catch (ArgumentException)
                    {
                        // Already released.
                    }
                }
            }

            _entries.Clear();
        }

        private sealed class BufferEntry
        {
            public BufferEntry(string path, ITextBuffer buffer, IVsTextLines lines, IVsInvisibleEditor? editor)
            {
                Path = path;
                Buffer = buffer;
                Lines = lines;
                Editor = editor;
            }

            public string Path { get; }

            public ITextBuffer Buffer { get; }

            public IVsTextLines Lines { get; }

            /// <summary>Non-null for a culture file opened through the invisible editor manager.</summary>
            public IVsInvisibleEditor? Editor { get; }

            public string Text() => Buffer.CurrentSnapshot.GetText();

            public bool IsDirty()
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return Lines is IVsPersistDocData doc && ErrorHandler.Succeeded(doc.IsDocDataDirty(out var dirty)) && dirty != 0;
            }
        }
    }

    /// <summary>One-shot <c>IVsTextBufferDataEvents</c> subscription: calls back once the shell has loaded the document into its buffer.</summary>
    internal sealed class BufferLoadedSink : IVsTextBufferDataEvents, IDisposable
    {
        private readonly Action _onLoaded;
        private OleInterop.IConnectionPoint? _connectionPoint;
        private uint _cookie;

        private BufferLoadedSink(Action onLoaded) => _onLoaded = onLoaded;

        internal static BufferLoadedSink? TryAdvise(IVsTextLines buffer, Action onLoaded)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (buffer is not OleInterop.IConnectionPointContainer container)
            {
                return null;
            }

            var sink = new BufferLoadedSink(onLoaded);
            var iid = typeof(IVsTextBufferDataEvents).GUID;
            container.FindConnectionPoint(ref iid, out var connectionPoint);
            if (connectionPoint is null)
            {
                return null;
            }

            connectionPoint.Advise(sink, out sink._cookie);
            sink._connectionPoint = connectionPoint;
            return sink;
        }

        public void OnFileChanged(uint grfChange, uint dwFileAttrs)
        {
        }

        public int OnLoadCompleted(int fReload)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _onLoaded();
            return VSConstants.S_OK;
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var point = _connectionPoint;
            _connectionPoint = null;
            if (point is not null)
            {
                try
                {
                    point.Unadvise(_cookie);
                }
                catch (COMException)
                {
                    // The buffer is already gone.
                }
            }
        }
    }
}

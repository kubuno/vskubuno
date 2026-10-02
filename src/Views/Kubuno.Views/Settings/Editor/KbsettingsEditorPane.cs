using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Kubuno.Views.Logging;
using Kubuno.Views.Logic.Settings;
using Kubuno.Views.Resources.Editor;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.Views.Settings.Editor
{
    /// <summary>
    /// The window pane of the settings editor for one <c>.kbsettings</c> document: hosts <see cref="SettingsEditorView"/>
    /// and keeps it in step with the document's text buffer, which stays the document (dirty tracking, Save, source
    /// control and the code view all work on the XML). A change in the grid is written into the buffer as the canonical
    /// text (as a minimal edit, so the text undo history stays readable); an edit made to the buffer elsewhere (the code
    /// view, a reload) reloads the grid, with a re-entrancy guard both ways.
    /// </summary>
    public sealed class KbsettingsEditorPane : WindowPane
    {
        private readonly IVsTextLines _lines;
        private readonly string _path;
        private readonly ServiceProvider _services;
        private readonly IVsEditorAdaptersFactoryService _adapters;
        private readonly SettingsEditorView _view;
        private readonly DispatcherTimer _pushTimer;
        private readonly DispatcherTimer _reloadTimer;
        private BufferLoadedSink? _loadSink;
        private ITextBuffer? _buffer;
        private bool _pushing;
        /// <summary>The buffer is not readable XML: the grid shows nothing and must never overwrite it.</summary>
        private bool _unreadable;
        private bool _disposed;

        public KbsettingsEditorPane(IVsTextLines textLines, OleInterop.IServiceProvider ole, string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _lines = textLines;
            _path = path;
            _services = new ServiceProvider(ole);
            _adapters = KbresEditorFactory.GetEditorAdapters(ole);
            _view = new SettingsEditorView(ViewCode) { FileName = Path.GetFileName(path) };
            _view.Changed += (_, _) =>
            {
                _pushTimer!.Stop();
                _pushTimer.Start();
            };
            _pushTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
            _pushTimer.Tick += (_, _) =>
            {
                _pushTimer.Stop();
                PushToBuffer();
            };
            _reloadTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
            _reloadTimer.Tick += (_, _) =>
            {
                _reloadTimer.Stop();
                ReloadFromBuffer();
            };
            Content = _view;
            if (!TryAttach())
            {
                _loadSink = BufferLoadedSink.TryAdvise(_lines, () =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    _loadSink?.Dispose();
                    _loadSink = null;
                    TryAttach();
                });
            }
        }

        private bool TryAttach()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_buffer is not null || _disposed)
            {
                return true;
            }

            try
            {
                _buffer = _adapters.GetDataBuffer(_lines);
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException)
            {
                _buffer = null;
            }

            if (_buffer is null)
            {
                return false;
            }

            _buffer.Changed += OnBufferChanged;
            ReloadFromBuffer();
            return true;
        }

        private void OnBufferChanged(object? sender, TextContentChangedEventArgs e)
        {
            if (_pushing)
            {
                return;
            }

            _reloadTimer.Stop();
            _reloadTimer.Start();
        }

        private void ReloadFromBuffer()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_buffer is null || _disposed)
            {
                return;
            }

            var text = _buffer.CurrentSnapshot.GetText();
            if (string.IsNullOrWhiteSpace(text))
            {
                _unreadable = false;
                _view.Load(new KbsettingsFile(), Array.Empty<string>());
                return;
            }

            var file = KbsettingsFile.Parse(text, out var errors);
            // Not XML (or not <Settings>): the grid stays empty and the buffer is left alone until it is fixed.
            _unreadable = errors.Count > 0 && file.Entries.Count == 0;
            _view.Load(file, errors);
        }

        /// <summary>Writes the grid's file into the buffer (only where the text differs).</summary>
        private void PushToBuffer()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_buffer is null || _disposed || _unreadable)
            {
                return;
            }

            var current = _buffer.CurrentSnapshot.GetText();
            var text = _view.Current().ToText();
            var target = current.Contains("\r\n") ? text.Replace("\n", "\r\n") : text;
            if (current == target)
            {
                return;
            }

            if (!_buffer.CheckEditAccess())
            {
                KubunoViewsLogHost.Current.WriteLine("[settings] " + SettingsText.ReadOnlyFile + " " + _path);
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

            _pushing = true;
            try
            {
                using var edit = _buffer.CreateEdit();
                edit.Replace(new Span(prefix, current.Length - prefix - suffix), target.Substring(prefix, target.Length - prefix - suffix));
                edit.Apply();
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                KubunoViewsLogHost.Current.WriteException("[settings] could not update " + _path, ex);
            }
            finally
            {
                _pushing = false;
            }
        }

        private void ViewCode()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                VsShellUtilities.OpenDocument(_services, _path, VSConstants.LOGVIEWID_Code, out _, out _, out var frame);
                frame?.Show();
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                KubunoViewsLogHost.Current.WriteException("[settings] View Code failed", ex);
            }
        }

        protected override void Dispose(bool disposing)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (disposing && !_disposed)
            {
                _disposed = true;
                _pushTimer.Stop();
                _reloadTimer.Stop();
                _loadSink?.Dispose();
                _loadSink = null;
                if (_buffer is not null)
                {
                    _buffer.Changed -= OnBufferChanged;
                }
            }

            base.Dispose(disposing);
        }
    }
}

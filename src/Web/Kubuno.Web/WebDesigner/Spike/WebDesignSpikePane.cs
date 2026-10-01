using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Kubuno.Core.Logging;
using Kubuno.Core.UI;
using Kubuno.Web.Logic.WebDesigner;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.Web.WebDesigner.Spike
{
    /// <summary>
    /// SPIKE (docs/WEB-VIEWS.md, WV-9a): the Design window of a <c>.kbwebspike</c> document - the WebView2 surface
    /// (<see cref="WebSurfaceView"/>) wired to Visual Studio like the desktop designer's pane:
    /// <list type="bullet">
    /// <item>the text buffer is the single source of truth: the page gets the text (<c>setText</c>), sends intents
    /// (<c>editRequest</c>), and every intent becomes one buffer edit = one undo unit (Edit.Undo/Redo here are the
    /// buffer's history, and the page follows the buffer);</item>
    /// <item>the page's selection is published to the Properties window (<c>STrackSelection</c>);</item>
    /// <item>keys reported by WebView2 run Visual Studio's bindings (<see cref="VsKeyBindings"/>, <see cref="AcceleratorRouting"/>);</item>
    /// <item>the Toolbox: <see cref="IVsToolboxUser"/> (double-click inserts), drags through <see cref="HostDropTarget"/> or the page's HTML5 drop;</item>
    /// <item>Visual Studio's theme, live (<c>setVsTheme</c>, the profile's preferred colour scheme).</item>
    /// </list>
    /// </summary>
    public sealed class WebDesignSpikePane : WindowPane, IVsToolboxUser
    {
        private readonly IVsTextLines _lines;
        private readonly OleInterop.IServiceProvider _site;
        private readonly string _moniker;
        private readonly WebSurfaceView _view;
        private BufferEvents? _bufferEvents;
        private ITextBuffer? _buffer;
        private ITextUndoHistory? _history;
        private string? _selectedId;
        private bool _pageEditingText;
        private bool _textQueued;
        private bool _toolboxUser;
        private bool _disposed;

        public WebDesignSpikePane(IVsTextLines lines, OleInterop.IServiceProvider site, string moniker)
            : base()
        {
            _lines = lines;
            _site = site;
            _moniker = moniker;
            _view = new WebSurfaceView(OnSurfaceKey);
            _view.MessageReceived += OnMessage;
            Content = _view;
            s_live++;
            KubunoLog.WriteLine($"[web-spike] pane opened ({s_live} live): {moniker}");
        }

        /// <summary>Live panes (lifecycle measurement: back to 0 after closing every spike window).</summary>
        private static int s_live;

        protected override void Initialize()
        {
            base.Initialize();
            ThreadHelper.ThrowIfNotOnUIThread();
            if (GetService(typeof(IMenuCommandService)) is OleMenuCommandService commands)
            {
                AddCommand(commands, VSConstants.VSStd97CmdID.Undo, () => _history?.CanUndo == true, () => _history?.Undo(1));
                AddCommand(commands, VSConstants.VSStd97CmdID.Redo, () => _history?.CanRedo == true, () => _history?.Redo(1));
                AddCommand(commands, VSConstants.VSStd97CmdID.Delete, () => !string.IsNullOrEmpty(_selectedId), DeleteSelection);

                // View.ViewCode (F7): the shell does not switch an editor factory's own views by itself (found live: the
                // command is posted and nothing opens) - open the Code logical view of the same document, like WinForms.
                AddCommand(commands, VSConstants.VSStd97CmdID.ViewCode, () => true, ViewCode);
            }

            SpikeToolbox.AddUser();
            _toolboxUser = true;
            VSColorTheme.ThemeChanged += OnThemeChanged;
            _bufferEvents = BufferEvents.Advise(_lines, OnBufferLoaded);

            // The shell loads the file into the buffer after CreateEditorInstance returns; a buffer opened in another
            // window already has its text.
            OnBufferLoaded();
#pragma warning disable VSSDK007 // no package-owned JoinableTaskFactory reachable from an editor pane; the task is observed by FileAndForget.
            ThreadHelper.JoinableTaskFactory.RunAsync(InitializeSurfaceAsync).FileAndForget("Kubuno/WebSpike/Initialize");
#pragma warning restore VSSDK007
        }

        private async System.Threading.Tasks.Task InitializeSurfaceAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var failure = await _view.InitializeAsync(VsTheme.IsDark(), OnDropped);
            if (failure is not null && !_disposed)
            {
                ShowInfoBar(failure);
            }
        }

        // ---- the buffer ----

        private void OnBufferLoaded()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_buffer is not null || _disposed)
            {
                return;
            }

            var adapters = WebDesignSpikeEditorFactory.Adapters(_site);
            _buffer = adapters.GetDocumentBuffer(_lines);
            if (_buffer is null)
            {
                return; // not loaded yet: IVsTextBufferDataEvents.OnLoadCompleted calls back
            }

            // The buffer's undo history, created by the editor's own undo manager if no text view made it yet (the code
            // window, F7, shares it).
            if (new ServiceProvider(_site).GetService(typeof(Microsoft.VisualStudio.ComponentModelHost.SComponentModel)) is Microsoft.VisualStudio.ComponentModelHost.IComponentModel model)
            {
                _history = model.GetService<ITextBufferUndoManagerProvider>().GetTextBufferUndoManager(_buffer).TextBufferUndoHistory;
            }

            _buffer.Changed += OnBufferChanged;
            QueueText();
        }

        private void OnBufferChanged(object? sender, TextContentChangedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            QueueText();
        }

        /// <summary>Sends the buffer's text to the page once per burst of changes (setText), then refreshes the Properties window.</summary>
        private void QueueText()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_textQueued)
            {
                return;
            }

            _textQueued = true;
#pragma warning disable VSTHRD001, VSTHRD110 // a plain UI-thread coalescing post; nothing to await.
            _view.Dispatcher.BeginInvoke(new Action(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                _textQueued = false;
                if (_disposed || _buffer is null)
                {
                    return;
                }

                _view.Post(WebSurfaceProtocol.EncodeSetText(_buffer.CurrentSnapshot.GetText()));
                PublishSelection();
            }));
#pragma warning restore VSTHRD001, VSTHRD110
        }

        private SpikeViewDocument? CurrentDocument(out string? error)
        {
            error = "the document is not loaded";
            return _buffer is null ? null : SpikeViewDocument.Parse(_buffer.CurrentSnapshot.GetText(), out error);
        }

        /// <summary>Applies one intent of the page or of Visual Studio to the buffer: one replacement, one undo unit.</summary>
        private bool ApplyEdit(SurfaceEditOp op, string description)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var document = CurrentDocument(out var error);
            var change = document?.Apply(op, out error);
            if (_buffer is null || change is not { } c)
            {
                KubunoLog.WriteLine($"[web-spike] edit refused ({op.Kind}): {error}");
                return false;
            }

            if (c.IsEmpty)
            {
                return true;
            }

            using (var transaction = _history?.CreateTransaction(description))
            {
                using var edit = _buffer.CreateEdit();
                edit.Replace(new Span(c.Start, c.OldLength), c.NewText);
                edit.Apply();
                transaction?.Complete();
            }

            KubunoLog.WriteLine($"[web-spike] edit applied: {description} ({c.OldLength} -> {c.NewText.Length} chars at {c.Start})");
            return true;
        }

        // ---- the page ----

        private void OnMessage(object? sender, WebSurfaceMessage message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            switch (message)
            {
                case SurfaceInfoMessage info:
                    if (WebSurfaceProtocol.CheckSurfaceInfo(info) is { } refused)
                    {
                        ShowInfoBar("The web design surface was refused: " + refused);
                        return;
                    }

                    _view.MarkPageReady();
                    _view.Post(WebSurfaceProtocol.EncodeSetComponents(SpikeElementCatalog.Elements));
                    SendTheme();
                    _view.Channel = _view.Channel;
                    _view.Post(WebSurfaceProtocol.EncodeSetDesignMode(true));
                    if (_buffer is not null)
                    {
                        _view.Post(WebSurfaceProtocol.EncodeSetText(_buffer.CurrentSnapshot.GetText()));
                    }

                    if (_selectedId is not null)
                    {
                        _view.Post(WebSurfaceProtocol.EncodeSelect(_selectedId));
                    }

                    KubunoLog.WriteLine($"[web-spike] surfaceInfo: version {info.Version}, target {info.Target}, views {info.Views}");
                    break;
                case SelectionChangedMessage selection:
                    EnsureFrameActive("selection");
                    _selectedId = selection.PrimaryId;
                    PublishSelection();
                    break;
                case EditRequestMessage edit:
                    OnEditRequest(edit.Op);
                    break;
                case ToolboxDragDetectedMessage:
                    _view.ShowDropOverlay();
                    break;
                case DropTargetChangedMessage drop:
                    _view.SetDropTargetValid(drop.Target?.Valid ?? false);
                    break;
                case FocusStateMessage focus:
                    _pageEditingText = focus.Editing;
                    KubunoLog.WriteLine($"[web-spike] page text editor focused: {focus.Editing}");
                    break;
                case MetricsMessage metrics:
                    _view.OnMetrics(metrics);
                    break;
                case LogMessage log:
                    KubunoLog.WriteLine("[web-spike] page: " + log.Message);
                    break;
                case SurfaceErrorMessage error:
                    KubunoLog.WriteLine($"[web-spike] page cannot render the markup ({error.Line},{error.Column}): {error.Message.Trim()}");
                    break;
                case UnhandledKeyMessage key:
                    KubunoLog.WriteLine($"[web-spike] page reports an unhandled key {key.Key}");
                    break;
            }
        }

        private void OnEditRequest(SurfaceEditOp op)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var description = op.Kind switch
            {
                SurfaceEditKind.SetAttribute => $"Set {op.Name}",
                SurfaceEditKind.InsertChild => "Insert element",
                SurfaceEditKind.RemoveElement => "Delete element",
                _ => "Move element",
            };
            if (!ApplyEdit(op, description))
            {
                return;
            }

            if (op.Kind == SurfaceEditKind.InsertChild)
            {
                // Select what was inserted, like the desktop designer.
                var parent = CurrentDocument(out _)?.Find(op.ParentId);
                if (parent is not null && parent.Children.Count > 0)
                {
                    var index = Math.Min(Math.Max(op.Index, 0), parent.Children.Count - 1);
                    _selectedId = parent.Children[index].Id;
                    _view.Post(WebSurfaceProtocol.EncodeSelect(_selectedId));
                }
            }
        }

        // ---- selection -> Properties window ----

        private void PublishSelection()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed || GetService(typeof(STrackSelection)) is not ITrackSelection track)
            {
                return;
            }

            var node = CurrentDocument(out _)?.Find(_selectedId);
            var container = new SelectionContainer(true, false);
            if (node is not null)
            {
                var id = node.Id;
                var item = new SpikeElementObject(node, (name, value) =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    ApplyEdit(SurfaceEditOp.SetAttribute(id, name, value), $"Set {name}");
                });
                container.SelectableObjects = new object[] { item };
                container.SelectedObjects = new object[] { item };
            }

            track.OnSelectChange(container);
        }

        private void DeleteSelection()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (string.IsNullOrEmpty(_selectedId))
            {
                return;
            }

            var removed = _selectedId!;
            if (ApplyEdit(SurfaceEditOp.RemoveElement(removed), "Delete element"))
            {
                _selectedId = removed.Contains(".") ? removed.Substring(0, removed.LastIndexOf('.')) : string.Empty;
                _view.Post(WebSurfaceProtocol.EncodeSelect(_selectedId));
            }
        }

        private void ViewCode()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                VsShellUtilities.OpenDocument(new ServiceProvider(_site), _moniker, VSConstants.LOGVIEWID_Code, out _, out _, out var frame);
                frame?.Show();
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
            {
                KubunoLog.WriteException("[web-spike] View Code failed", ex);
            }
        }

        // ---- activation ----

        /// <summary>
        /// Makes this document the active window of Visual Studio on a user gesture in the page (pointer down, which posts
        /// selectionChanged; an accelerator key) - not on the browser's focus events, which also fire while Visual Studio
        /// switches to another window (F7 opened the code window, then this frame was re-activated over it: found live). A click in the WebView2
        /// lands in a window of the browser process: Visual Studio's window manager does not see it, so without this the
        /// previously active window (e.g. the Properties window) stays the command target and Edit.Undo, Edit.Delete or
        /// Ctrl+S posted from the page reach that window instead of this document (found live, WV-9a).
        /// </summary>
        private void EnsureFrameActive(string why)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed || GetService(typeof(SVsWindowFrame)) is not IVsWindowFrame frame ||
                GetService(typeof(SVsShellMonitorSelection)) is not IVsMonitorSelection selection ||
                selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_WindowFrame, out var active) != VSConstants.S_OK)
            {
                return;
            }

            if (ReferenceEquals(active, frame))
            {
                return;
            }

            var caption = active is IVsWindowFrame other && other.GetProperty((int)__VSFPROPID.VSFPROPID_Caption, out var c) == VSConstants.S_OK ? c as string : null;
            frame.Show();
            KubunoLog.WriteLine($"[web-spike] document frame activated ({why}; was '{caption}')");
        }

        // ---- keys ----

        /// <summary>A key WebView2 reported (accelerator keys only): true when Visual Studio takes it.</summary>
        private bool OnSurfaceKey(KeyEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            {
                return false;
            }

            var virtualKey = KeyInterop.VirtualKeyFromKey(key);
            var control = VsKeyBindings.IsDown(0x11);
            var shift = VsKeyBindings.IsDown(0x10);
            var alt = VsKeyBindings.IsDown(0x12);
            var name = (control ? "Ctrl+" : string.Empty) + (shift ? "Shift+" : string.Empty) + (alt ? "Alt+" : string.Empty) + key;
            var wpfModifiers = Keyboard.Modifiers;
            var route = AcceleratorRouting.Route(virtualKey, control, shift, alt, _pageEditingText);
            if (route == KeyRoute.Page)
            {
                KubunoLog.WriteLine($"[web-spike] key {name} -> page (editing text: {_pageEditingText}; WPF modifiers {wpfModifiers})");
                return false;
            }

            EnsureFrameActive("key");
            if (VsKeyBindings.TryFind(_view.WindowHandle, virtualKey, alt, out var group, out var id, out var diagnostic) && VsKeyBindings.Post(group, id))
            {
                KubunoLog.WriteLine($"[web-spike] key {name} -> Visual Studio: {VsKeyBindings.NameOf(group, id)} posted (WPF modifiers {wpfModifiers}; {diagnostic})");
                return true;
            }

            KubunoLog.WriteLine($"[web-spike] key {name} -> no Visual Studio binding, left to the page ({diagnostic})");
            return false;
        }

        // ---- Toolbox ----

        public int IsSupported(OleInterop.IDataObject pDO) => HostDropTarget.ReadComponent(pDO) is null ? VSConstants.S_FALSE : VSConstants.S_OK;

        public int ItemPicked(OleInterop.IDataObject pDO)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (HostDropTarget.ReadComponent(pDO) is not { } component || SpikeElementCatalog.ToolboxXml(component) is not { } xml)
            {
                return VSConstants.S_FALSE;
            }

            // Into the selected container, else the selected element's container, else the root; at the end.
            var document = CurrentDocument(out _);
            var target = document?.Find(_selectedId) ?? document?.Root;
            while (target is not null && SpikeElementCatalog.Find(target.Name) is { IsContainer: false })
            {
                target = target.Parent;
            }

            OnEditRequest(SurfaceEditOp.InsertChild(target?.Id ?? string.Empty, int.MaxValue, xml));
            OnDropped(component);
            return VSConstants.S_OK;
        }

        private void OnDropped(string component)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            KubunoLog.WriteLine($"[web-spike] Toolbox item '{component}' used");
            (GetService(typeof(SVsToolbox)) as IVsToolbox)?.DataUsed();
        }

        // ---- theme ----

        private void OnThemeChanged(ThemeChangedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SendTheme();
        }

        private void SendTheme()
        {
            var dark = VsTheme.IsDark();
            var colors = new Dictionary<string, string>
            {
                ["canvas"] = Hex(EnvironmentColors.DesignerBackgroundColorKey),
                ["text"] = Hex(EnvironmentColors.ToolWindowTextColorKey),
                ["accent"] = Hex(EnvironmentColors.SystemHighlightColorKey),
                ["border"] = Hex(EnvironmentColors.ToolWindowBorderColorKey),
                ["panel"] = Hex(EnvironmentColors.ToolWindowBackgroundColorKey),
            };
            _view.SetPreferredColorScheme(dark);
            _view.Post(WebSurfaceProtocol.EncodeSetVsTheme(dark, colors));
            KubunoLog.WriteLine($"[web-spike] theme sent: {(dark ? "dark" : "light")}, canvas {colors["canvas"]}");
        }

        private static string Hex(ThemeResourceKey key)
        {
            var c = VSColorTheme.GetThemedColor(key);
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        // ---- info bar ----

        private void ShowInfoBar(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (GetService(typeof(SVsWindowFrame)) is IVsWindowFrame frame &&
                    frame.GetProperty((int)__VSFPROPID7.VSFPROPID_InfoBarHost, out var hostObject) == VSConstants.S_OK &&
                    hostObject is IVsInfoBarHost host &&
                    GetService(typeof(SVsInfoBarUIFactory)) is IVsInfoBarUIFactory factory)
                {
                    var model = new InfoBarModel(
                        new[] { new InfoBarTextSpan("Kubuno web design surface: " + message + " Install the Microsoft Edge WebView2 Runtime (Evergreen), then reopen the document.") },
                        KnownMonikers.StatusWarning,
                        isCloseButtonVisible: true);
                    host.AddInfoBar(factory.CreateInfoBar(model));
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                KubunoLog.WriteException("[web-spike] could not show the info bar", ex);
            }
        }

        private void AddCommand(OleMenuCommandService commands, VSConstants.VSStd97CmdID id, Func<bool> enabled, Action run)
        {
            var command = new OleMenuCommand((_, _) => run(), new CommandID(VSConstants.GUID_VSStandardCommandSet97, (int)id));
            command.BeforeQueryStatus += (_, _) =>
            {
                command.Supported = true;
                command.Enabled = enabled();
            };
            commands.AddCommand(command);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
#pragma warning disable VSTHRD010 // Dispose(true) comes from ClosePane, on the UI thread.
                VSColorTheme.ThemeChanged -= OnThemeChanged;
                if (_buffer is not null)
                {
                    _buffer.Changed -= OnBufferChanged;
                }

                _bufferEvents?.Dispose();
                _view.MessageReceived -= OnMessage;
                _view.Dispose();
                if (_toolboxUser)
                {
                    SpikeToolbox.RemoveUser();
                }

                (GetService(typeof(STrackSelection)) as ITrackSelection)?.OnSelectChange(new SelectionContainer());
#pragma warning restore VSTHRD010
                s_live--;
                KubunoLog.WriteLine($"[web-spike] pane closed ({s_live} live): {_moniker}");
            }

            base.Dispose(disposing);
        }

        /// <summary><c>IVsTextBufferDataEvents</c>: tells the pane when the shell has loaded the file into the buffer.</summary>
        private sealed class BufferEvents : IVsTextBufferDataEvents, IDisposable
        {
            private readonly Action _loaded;
            private OleInterop.IConnectionPoint? _point;
            private uint _cookie;

            private BufferEvents(Action loaded) => _loaded = loaded;

            public static BufferEvents? Advise(IVsTextLines lines, Action loaded)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (lines is not OleInterop.IConnectionPointContainer container)
                {
                    return null;
                }

                var events = new BufferEvents(loaded);
                var guid = typeof(IVsTextBufferDataEvents).GUID;
                container.FindConnectionPoint(ref guid, out var point);
                if (point is null)
                {
                    return null;
                }

                point.Advise(events, out events._cookie);
                events._point = point;
                return events;
            }

            public void OnFileChanged(uint grfChange, uint dwFileAttrs)
            {
            }

            public int OnLoadCompleted(int fReload)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                _loaded();
                return VSConstants.S_OK;
            }

            public void Dispose()
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (_point is not null && _cookie != 0)
                {
                    _point.Unadvise(_cookie);
                }

                _point = null;
            }
        }
    }
}

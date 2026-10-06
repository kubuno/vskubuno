using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Kubuno.Shared.Logging;
using Kubuno.Views.Designer.DesignSurface;
using Kubuno.Web.Logic.WebDesigner;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.Web.WebView2.Core;

namespace Kubuno.Web.WebDesigner
{
    /// <summary>
    /// The seam's members (<see cref="IProtocolDesignSurfaceHost"/>, <see cref="IDesignSurfaceStatusAware"/>) and the page's
    /// messages. The state the designer pushes (text, design mode, selection, zoom, language, project controls, theme) is
    /// kept here and sent again whole each time a page reports <c>ready</c> - a dev server that starts or stops reloads the
    /// page, which must come back as it was (the desktop host replays a restarted surface the same way).
    /// </summary>
    internal sealed partial class WebDesignSurfaceHost
    {
        private string? _text;
        private bool _designMode;
        private string? _selection;
        private IReadOnlyList<string>? _selectionIds;
        private string? _projectComponents;
        private double _zoom = 1.0;
        private string _designCulture = string.Empty;
        private DesignSurfaceRenderStatus? _renderStatus;
        private bool _pageEditingText;
        private string? _surfaceMode;
        private string? _sentHostRegistryStamp;

        public event EventHandler<DesignSurfaceSelectionChangedEventArgs>? SelectionChanged;

        public event EventHandler<DesignSurfaceEditRequestedEventArgs>? EditRequested;

        public event EventHandler<DesignSurfaceDragDropEditRequestedEventArgs>? DragDropEditRequested;

        public event EventHandler<DesignSurfaceEditRequestsReceivedEventArgs>? EditRequestsReceived;

        public event EventHandler<DesignSurfaceKeyEventArgs>? UnhandledSurfaceKey;

        public event EventHandler<DesignSurfaceContextMenuEventArgs>? ContextMenuRequested;

        public event EventHandler<DesignSurfaceDoubleClickEventArgs>? ElementDoubleClicked;

        public event EventHandler<DesignSurfaceCommandEventArgs>? SurfaceCommandRequested;

        /// <summary>Never raised: the web view's strings are i18next dictionaries, not <c>.kbres</c> files watched by the designer.</summary>
        public event EventHandler? ResourcesChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? RenderStatusChanged;

        /// <summary>Never raised: a page that fails reloads (no crash loop to report, unlike a surface process).</summary>
        public event EventHandler? HealthChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<DesignSurfaceDiagnostic>? SourceNavigationRequested;

        /// <summary>The buffer text the page shows (the last <see cref="SetDocumentText"/>).</summary>
        internal string? CurrentText => _text;

        public DesignSurfaceRenderStatus? RenderStatus => _renderStatus;

        public bool IsFailingRepeatedly => false;

        public double Zoom => _zoom;

        public string DesignCulture => _designCulture;

        public void SetDocumentText(string xmlText)
        {
            _text = xmlText ?? string.Empty;
            Post(DesignSurfaceProtocol.EncodeSetText(_text, BaseDirectory));
        }

        public void SetDesignMode(bool on)
        {
            _designMode = on;
            Post(DesignSurfaceProtocol.EncodeSetDesignMode(on));
        }

        public void Select(string? elementId)
        {
            _selection = elementId;
            _selectionIds = elementId is null ? null : new[] { elementId };
            Post(DesignSurfaceProtocol.EncodeSelect(elementId));
        }

        public void SelectMany(IReadOnlyList<string> elementIds, string? primary)
        {
            _selection = primary;
            _selectionIds = elementIds;
            Post(DesignSurfaceProtocol.EncodeSelectMany(elementIds, primary));
        }

        public void SetProjectComponents(string componentsJsonArray)
        {
            _projectComponents = componentsJsonArray;
            Post(DesignSurfaceProtocol.EncodeProjectComponents(componentsJsonArray));
        }

        public void Format(string command) => Post(DesignSurfaceProtocol.EncodeFormat(command));

        public void SetZoom(double zoom)
        {
            _zoom = zoom;
            Post(WebDesignMessages.EncodeSetZoom(zoom));
        }

        public void SetDesignCulture(string culture)
        {
            _designCulture = culture ?? string.Empty;
            Post(WebDesignMessages.EncodeSetLanguage(_designCulture));
        }

        /// <summary>The languages the design-time language picker offers: those of the web views' pilot checks (docs/WEB-VIEWS.md section 7).</summary>
        public IReadOnlyList<string> ProjectCultures() => new[] { "ar", "en", "fr" };

        /// <summary>Sends a message to a ready page; before <c>ready</c> nothing is lost: the whole state is replayed then.</summary>
        private void Post(string json)
        {
            if (_disposed || !_pageReady || _webView.CoreWebView2 is not { } core)
            {
                return;
            }

            try
            {
                core.PostWebMessageAsJson(json);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                KubunoLog.WriteException("[web-designer] posting a message to the page failed", ex);
            }
        }

        /// <summary>The page is ready: everything it must know, in the desktop host's order (theme, options, project, text, mode, selection).</summary>
        private void ReplayState()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SendTheme();
            Post(DesignSurfaceProtocol.EncodeSetDesignOptions(Kubuno.Views.Designer.Options.DesignerOptionsHost.Current?.ShowDesignOutlines ?? true));
            if (_zoom != 1.0)
            {
                Post(WebDesignMessages.EncodeSetZoom(_zoom));
            }

            if (_designCulture.Length > 0)
            {
                Post(WebDesignMessages.EncodeSetLanguage(_designCulture));
            }

            SendHostRegistry();
            SendDocumentInfo();
            if (_projectComponents is not null)
            {
                Post(DesignSurfaceProtocol.EncodeProjectComponents(_projectComponents));
            }

            if (_text is not null)
            {
                Post(DesignSurfaceProtocol.EncodeSetText(_text, BaseDirectory));
            }

            Post(DesignSurfaceProtocol.EncodeSetDesignMode(_designMode));
            if (_selectionIds is { Count: > 1 } many)
            {
                Post(DesignSurfaceProtocol.EncodeSelectMany(many, _selection));
            }
            else if (_selection is not null)
            {
                Post(DesignSurfaceProtocol.EncodeSelect(_selection));
            }
        }

        /// <summary>
        /// <c>setHostRegistry</c> to a bundled page: the project's own host registry (the one <c>kbview-tsc</c> compiles with),
        /// never the older one the bundled design host was built with (docs/WEB-VIEWS.md section 22).
        /// </summary>
        private void SendHostRegistry()
        {
            var text = Project.HostRegistryText();
            _sentHostRegistryStamp = HostRegistryStamp();
            if (WebDesignMessages.ShouldSendHostRegistry(_surfaceMode, text))
            {
                Post(WebDesignMessages.EncodeSetHostRegistry(text!));
                KubunoLog.WriteLine($"[web-designer] {Path.GetFileName(DocumentPath)}: the bundled design host compiles with the project's registry ({Project.HostRegistryPath}).");
            }
        }

        /// <summary>The project's host registry changed (an <c>npm install</c> of a newer <c>@kubuno/ui</c>): sent again to a bundled page.</summary>
        private void CheckHostRegistryChanged()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_pageReady && _sentHostRegistryStamp is not null && HostRegistryStamp() != _sentHostRegistryStamp)
            {
                SendHostRegistry();
            }
        }

        private string HostRegistryStamp()
        {
            try
            {
                return Project.HostRegistryPath is { } path && File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return "error";
            }
        }

        /// <summary><c>setDocumentInfo</c>: the view's path, code-behind, class, the project's user controls and the design data.</summary>
        private void SendDocumentInfo()
        {
            var designData = ReadDesignData();
            Post(WebDesignMessages.EncodeSetDocumentInfo(
                Project.RelativePath(DocumentPath),
                Project.CodeBehindModule(DocumentPath),
                WebViewProject.ClassNameOf(DocumentPath),
                Project.UserControls(),
                designData));
        }

        private string? ReadDesignData()
        {
            var path = WebViewProject.DesignDataPath(DocumentPath);
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                KubunoLog.WriteException("[web-designer] reading the design data failed", ex);
                return null;
            }
        }

        private FileSystemWatcher? _designDataWatcher;

        /// <summary>The design data file (<c>&lt;view&gt;.design.json</c>) is resent when it changes.</summary>
        private void WatchDesignData()
        {
            try
            {
                var folder = Path.GetDirectoryName(DocumentPath);
                if (folder is null || !Directory.Exists(folder))
                {
                    return;
                }

                _designDataWatcher = new FileSystemWatcher(folder, Path.GetFileName(WebViewProject.DesignDataPath(DocumentPath)))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
#pragma warning disable VSTHRD001, VSTHRD110 // a plain dispatcher hop from the watcher's thread, as the desktop host does.
                FileSystemEventHandler changed = (_, _) => _root.Dispatcher.BeginInvoke(new Action(SendDocumentInfo));
                _designDataWatcher.Changed += changed;
                _designDataWatcher.Created += changed;
                _designDataWatcher.Deleted += changed;
                _designDataWatcher.Renamed += (_, _) => _root.Dispatcher.BeginInvoke(new Action(SendDocumentInfo));
#pragma warning restore VSTHRD001, VSTHRD110
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                KubunoLog.WriteException("[web-designer] watching the design data failed", ex);
            }
        }

        // ---- theme ----

        private void OnVsThemeChanged(ThemeChangedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_webView.CoreWebView2 is { } core)
            {
                core.Profile.PreferredColorScheme = Kubuno.Shared.UI.VsTheme.IsDark() ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
            }

            SendTheme();
        }

        /// <summary>The canvas and the page's chrome (toolbar, adorners) in Visual Studio's theme (<c>setVsTheme</c>, <c>setCanvasBackground</c>).</summary>
        private void SendTheme()
        {
            var colors = new Dictionary<string, string>
            {
                ["canvas"] = Hex(EnvironmentColors.DesignerBackgroundColorKey),
                ["text"] = Hex(EnvironmentColors.ToolWindowTextColorKey),
                ["accent"] = Hex(EnvironmentColors.SystemHighlightColorKey),
                ["border"] = Hex(EnvironmentColors.ToolWindowBorderColorKey),
                ["panel"] = Hex(EnvironmentColors.ToolWindowBackgroundColorKey),
                ["commandBar"] = Hex(EnvironmentColors.CommandBarGradientBeginColorKey),
            };
            Post(WebDesignMessages.EncodeSetVsTheme(Kubuno.Shared.UI.VsTheme.IsDark(), colors));
            Post(DesignSurfaceThemeProtocol.EncodeSetCanvasBackground(colors["canvas"]));
        }

        private static string Hex(ThemeResourceKey key)
        {
            var c = VSColorTheme.GetThemedColor(key);
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        // ---- the page's messages ----

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string json;
            try
            {
                json = e.WebMessageAsJson;
            }
            catch (ArgumentException)
            {
                return;
            }

            if (_pageOrigin is null || !e.Source.StartsWith(_pageOrigin, StringComparison.OrdinalIgnoreCase))
            {
                KubunoLog.WriteLine("[web-designer] a message from an unexpected origin was ignored: " + e.Source);
                return;
            }

            try
            {
                Dispatch(json);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
            {
                KubunoLog.WriteException("[web-designer] handling a page message failed", ex);
            }
        }

        private void Dispatch(string json)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            switch (WebDesignMessages.TypeOf(json))
            {
                case "surfaceInfo":
                    _surfaceInfoSeen = true;
                    _surfaceMode = WebDesignMessages.SurfaceMode(json);
                    if (WebDesignMessages.CheckSurfaceInfo(json) is { } refused)
                    {
                        KubunoLog.WriteLine("[web-designer] surface refused: " + refused);
                        _runtime.ReportRejected(refused);
                        RuntimeRejected?.Invoke(this, refused);
                    }
                    else
                    {
                        KubunoLog.WriteLine($"[web-designer] surface: {json}");
                    }

                    return;
                case "ready":
                    _pageReady = true;
                    ReplayState();
                    return;
                case "selectionChanged" when DesignSurfaceProtocol.TryParseSelectionChanged(json, out var ids):
                    _selectionIds = ids;
                    _selection = ids.Count > 0 ? ids[0] : null;
                    ActivateFrame("selection");
                    SelectionChanged?.Invoke(this, new DesignSurfaceSelectionChangedEventArgs(ids));
                    return;
                case "editRequest":
                    ActivateFrame("edit");
                    if (DesignSurfaceProtocol.TryParseEditRequest(json, out var op) && op is not null)
                    {
                        EditRequested?.Invoke(this, new DesignSurfaceEditRequestedEventArgs(op));
                    }
                    else if (DesignSurfaceDragDropProtocol.TryParseDragDropEditRequest(json, out var dragOp) && dragOp is not null)
                    {
                        DragDropEditRequested?.Invoke(this, new DesignSurfaceDragDropEditRequestedEventArgs(dragOp));
                    }

                    return;
                case "editRequests" when DesignSurfaceDragDropProtocol.TryParseEditRequestsBatch(json, out var ops, out var gesture):
                    ActivateFrame("edits");
                    EditRequestsReceived?.Invoke(this, new DesignSurfaceEditRequestsReceivedEventArgs(ops, gesture));
                    return;
                case "dropTargetChanged" when DesignSurfaceDragDropProtocol.TryParseDropTargetChanged(json, out var target):
                    SetDropTargetValid(target?.Valid ?? false);
                    return;
                case "contextMenu" when DesignSurfaceContextMenuProtocol.TryParseContextMenu(json, out var menu) && menu is not null:
                    ActivateFrame("context menu");
                    var screen = _webView.PointToScreen(new Point(menu.X, menu.Y));
                    ContextMenuRequested?.Invoke(this, new DesignSurfaceContextMenuEventArgs(menu.X, menu.Y, (int)screen.X, (int)screen.Y, menu.ElementId, menu.Menu));
                    return;
                case "doubleClick" when DesignSurfaceContextMenuProtocol.TryParseDoubleClick(json, out var doubleClick) && doubleClick is not null:
                    ActivateFrame("double-click");
                    ElementDoubleClicked?.Invoke(this, doubleClick);
                    return;
                case "command" when DesignSurfaceContextMenuProtocol.TryParseCommand(json, out var command) && command is not null:
                    ActivateFrame("command");
                    SurfaceCommandRequested?.Invoke(this, command);
                    return;
                case "renderStatus" when DesignSurfaceProtocol.TryParseRenderStatus(json, out var status):
                    _renderStatus = status;
                    RenderStatusChanged?.Invoke(this, EventArgs.Empty);
                    return;
                case "goToSource" when DesignSurfaceProtocol.TryParseGoToSource(json, out var diagnostic) && diagnostic is not null:
                    SourceNavigationRequested?.Invoke(this, diagnostic);
                    return;
                case "focusState":
                    _pageEditingText = WebDesignMessages.FlagOf(json, "editing");
                    return;
                case "unhandledKey":
                    var key = WebDesignMessages.VirtualKeyOf(WebDesignMessages.StringOf(json, "key"));
                    if (key != 0)
                    {
                        UnhandledSurfaceKey?.Invoke(this, new DesignSurfaceKeyEventArgs(key, WebDesignMessages.FlagOf(json, "ctrl"), WebDesignMessages.FlagOf(json, "shift"), WebDesignMessages.FlagOf(json, "alt")));
                    }

                    return;
                case "toolboxDragDetected":
                    ShowDropOverlay();
                    return;
                case "log":
                    KubunoLog.WriteLine("[web-designer] page: " + WebDesignMessages.StringOf(json, "message"));
                    return;
                case "surfaceError":
                    KubunoLog.WriteLine("[web-designer] page error: " + json);
                    return;
                case "viewportChanged":
                    KubunoLog.WriteLine("[web-designer] viewport: " + json);
                    return;
                default:
                    KubunoLog.WriteLine("[web-designer] unrecognized page message: " + (json.Length > 300 ? json.Substring(0, 300) : json));
                    return;
            }
        }
    }
}

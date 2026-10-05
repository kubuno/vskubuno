using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Kubuno.Shared.Logging;
using Kubuno.Views.Designer.DesignSurface;
using Kubuno.Web.Logic.WebDesigner;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Kubuno.Web.WebDesigner
{
    /// <summary>
    /// The design surface of a web view (docs/WEB-VIEWS.md sections 4.2-4.4, lots WV-9b/WV-10): the shared Kubuno View
    /// Designer's Design pane (Design | XML | Split, Toolbox, Properties, ⚡ events, outline - all of
    /// <c>Kubuno.Views</c>) rendered by a WebView2 page instead of the desktop's Rust surface. The page is the project's
    /// own design page, served by its dev server (<c>/__kubuno_design__/</c> of the <c>@kubuno/views-compiler</c> Vite
    /// plugin: the project's real components, code-behinds and <c>@kubuno/views</c>), or - while that server is not
    /// running - the design host bundled with the extension (<c>tools\web-design-host</c>, the <c>@kubuno/host-runtime</c>
    /// build: host elements only, project controls as placeholders), with the designer's runtime bar saying so.
    /// </summary>
    /// <remarks>
    /// The page speaks the desktop surface's protocol (<see cref="DesignSurfaceProtocol"/>: <c>setText</c>, <c>select</c>,
    /// <c>selectionChanged</c>, <c>editRequest(s)</c>, drops, context menus, <c>renderStatus</c>…) over
    /// <c>PostWebMessageAsJson</c> / <c>chrome.webview.postMessage</c>, plus the web additions of
    /// <see cref="WebDesignMessages"/>; the page never writes text: every gesture is an intent the shared
    /// <c>DesignSurfaceEditingCoordinator</c> turns into one buffer edit (one undo unit). Split in partial files: this one
    /// (WebView2, pages, lifecycle), <c>.Protocol.cs</c> (the seam's members and the page's messages), <c>.Input.cs</c>
    /// (keys, frame activation, Toolbox drops), <c>.Language.cs</c> (registry, handlers).
    /// </remarks>
    internal sealed partial class WebDesignSurfaceHost : IProtocolDesignSurfaceHost, IDesignSurfaceStatusAware, IDesignSurfaceRuntimeAware, IDesignSurfaceLanguageServices
    {
        /// <summary>The virtual host the bundled design page is served from (never resolved on the network).</summary>
        internal const string BundledHostName = "kubuno-design.invalid";

        private static readonly TimeSpan MonitorInterval = TimeSpan.FromSeconds(4);

        private readonly Grid _root;
        private readonly WebView2 _webView;
        private readonly TextBlock _failure;
        private readonly WebDesignRuntimeSource _runtime;
        private readonly DispatcherTimer _monitor;
        private readonly string _bundledFolder;
        private readonly bool _bundledAvailable;
        private bool _initStarted;
        private bool _disposed;
        private bool _pageReady;
        private string? _pageOrigin;
        private Uri? _projectPage;
        private int _projectPageFailures;
        private bool _monitoring;

        public WebDesignSurfaceHost(DesignSurfaceDocument document, WebViewProject project, string bundledFolder)
        {
            DocumentPath = document.Path;
            Project = project;
            BaseDirectory = Path.GetDirectoryName(document.Path);
            _bundledFolder = bundledFolder;
            _bundledAvailable = File.Exists(Path.Combine(bundledFolder, "index.html"));
            _runtime = new WebDesignRuntimeSource(OnRuntimeAction);

            _root = new Grid { Focusable = false };
            _root.SetResourceReference(Panel.BackgroundProperty, EnvironmentColors.DesignerBackgroundBrushKey);
            _failure = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16), Visibility = Visibility.Collapsed };
            _failure.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            _webView = new WebView2 { Visibility = Visibility.Hidden };
            _webView.PreviewKeyDown += OnWebViewPreviewKeyDown;
            _webView.WebMessageReceived += OnWebMessageReceived;
            _webView.NavigationCompleted += OnNavigationCompleted;
            _root.Children.Add(_webView);
            _root.Children.Add(_failure);
            _root.Loaded += OnLoaded;
            _root.GotKeyboardFocus += (_, e) =>
            {
                // The frame gave the pane the focus (activation, Ctrl+Tab): hand it to the page.
                if (ReferenceEquals(e.NewFocus, _root) && _webView.Visibility == Visibility.Visible)
                {
                    _webView.Focus();
                }
            };

            _monitor = new DispatcherTimer(DispatcherPriority.Background) { Interval = MonitorInterval };
            _monitor.Tick += OnMonitorTick;
            VSColorTheme.ThemeChanged += OnVsThemeChanged;
            WatchDesignData();
        }

        /// <summary>The view file.</summary>
        internal string DocumentPath { get; }

        /// <summary>The web project of the view.</summary>
        internal WebViewProject Project { get; }

        /// <summary>The view's folder (<c>setText</c>'s <c>baseDir</c>).</summary>
        public string? BaseDirectory { get; }

        public FrameworkElement Content => _root;

        public IDesignSurfaceRuntimeSource RuntimeSource => _runtime;

        public event EventHandler<string>? RuntimeRejected;

        /// <summary>Whether the page shows the project's dev server (true) or the bundled design host (false).</summary>
        internal bool OnProjectServer => _projectPage is not null;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_initStarted || _disposed)
            {
                return;
            }

            _initStarted = true;
#pragma warning disable VSSDK007 // no package JoinableTaskFactory reachable here: the shell's, as the shared designer does.
            ThreadHelper.JoinableTaskFactory.RunAsync(InitializeAsync).FileAndForget("Kubuno/WebDesigner/Initialize");
#pragma warning restore VSSDK007
        }

        private async Task InitializeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var version = WebView2Runtime.AvailableVersion(out var runtimeError);
            if (version is null)
            {
                ShowFailure(T("The WebView2 runtime is missing: ", "Le runtime WebView2 est absent : ") + runtimeError);
                return;
            }

            try
            {
                var environment = await WebView2Runtime.EnvironmentAsync();
                var options = environment.CreateCoreWebView2ControllerOptions();
                await _webView.EnsureCoreWebView2Async(environment, options);
                if (_disposed)
                {
                    return;
                }

                var core = _webView.CoreWebView2!;
                var settings = core.Settings;
                settings.AreBrowserAcceleratorKeysEnabled = false; // no Ctrl+F, Ctrl+P, F5, Ctrl+R or zoom keys of the browser
                settings.AreDefaultContextMenusEnabled = false;
                settings.IsStatusBarEnabled = false;
                settings.IsZoomControlEnabled = false;
                settings.IsPinchZoomEnabled = false;
                settings.IsSwipeNavigationEnabled = false;
                settings.AreDefaultScriptDialogsEnabled = false;
                settings.IsWebMessageEnabled = true;
                core.Profile.PreferredColorScheme = Kubuno.Shared.UI.VsTheme.IsDark() ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
                core.NewWindowRequested += (_, e) => e.Handled = true;
                core.ProcessFailed += OnProcessFailed;
                if (_bundledAvailable)
                {
                    core.SetVirtualHostNameToFolderMapping(BundledHostName, _bundledFolder, CoreWebView2HostResourceAccessKind.Allow);
                }

                // Toolbox drags: Chromium detects them, Visual Studio's drop target takes them over (docs/WEB-VIEWS.md section 11).
                _webView.AllowExternalDrop = true;
                CreateDropOverlay();
                _webView.Visibility = Visibility.Visible;
                KubunoLog.WriteLine($"[web-designer] WebView2 {version} ready for {Path.GetFileName(DocumentPath)} (browser pid {core.BrowserProcessId}).");

                await ChooseRuntimeAsync();
                _monitor.Start();
            }
            catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or IOException)
            {
                KubunoLog.WriteException("[web-designer] WebView2 initialization failed", ex);
                ShowFailure(T("The web design surface could not start WebView2: ", "La surface de conception web n'a pas pu démarrer WebView2 : ") + ex.Message);
            }
        }

        /// <summary>The project's dev server when one answers, else the bundled design host.</summary>
        private async Task ChooseRuntimeAsync()
        {
            var page = await DesignServer.FindAsync(Project.Root, CancellationToken.None).ConfigureAwait(true);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_disposed)
            {
                return;
            }

            if (page is not null)
            {
                ShowProjectPage(page);
            }
            else
            {
                ShowBundledPage(DesignServer.IsStarting(Project.Root) ? DesignSurfaceRuntimeState.Building : DesignSurfaceRuntimeState.NotBuilt);
            }
        }

        private void ShowProjectPage(Uri page)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _projectPage = page;
            _projectPageFailures = 0;
            _runtime.SetProject(page);
            Navigate(page);
            KubunoLog.WriteLine($"[web-designer] {Path.GetFileName(DocumentPath)}: the project's dev server ({page}).");
        }

        private void ShowBundledPage(DesignSurfaceRuntimeState state, string? detail = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var wasBundled = _projectPage is null && _pageOrigin is not null && _pageOrigin.Contains(BundledHostName);
            _projectPage = null;
            _runtime.SetBundled(state, _bundledFolder, _bundledAvailable, detail);
            if (!_bundledAvailable)
            {
                ShowFailure(_runtime.BarMessage ?? string.Empty);
                return;
            }

            if (!wasBundled)
            {
                Navigate(new Uri("https://" + BundledHostName + "/index.html"));
                KubunoLog.WriteLine($"[web-designer] {Path.GetFileName(DocumentPath)}: the bundled design host ({state}).");
            }
        }

        private void Navigate(Uri page)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_webView.CoreWebView2 is not { } core)
            {
                return;
            }

            _pageReady = false;
            _pageOrigin = page.GetLeftPart(UriPartial.Authority);
            _failure.Visibility = Visibility.Collapsed;
            _webView.Visibility = Visibility.Visible;
            core.Navigate(page.AbsoluteUri);
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (e.IsSuccess || _disposed)
            {
                return;
            }

            KubunoLog.WriteLine($"[web-designer] navigation failed ({e.WebErrorStatus}) on {_pageOrigin}.");
            if (_projectPage is not null)
            {
                // The dev server went away between the probe and the navigation.
                ShowBundledPage(DesignSurfaceRuntimeState.NotBuilt);
            }
        }

        /// <summary>
        /// Every few seconds: is the dev server still there (else the bundled host), did one start (then the project's
        /// page), did the one Visual Studio started fail.
        /// </summary>
        private void OnMonitorTick(object? sender, EventArgs e)
        {
            if (_disposed || _monitoring || _webView.CoreWebView2 is null)
            {
                return;
            }

            _monitoring = true;
#pragma warning disable VSSDK007, VSTHRD110
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await MonitorAsync();
                }
                finally
                {
                    _monitoring = false;
                }
            }).FileAndForget("Kubuno/WebDesigner/Monitor");
#pragma warning restore VSSDK007, VSTHRD110
        }

        private async Task MonitorAsync()
        {
            if (_projectPage is { } page)
            {
                var alive = await DesignServer.IsAliveAsync(page, CancellationToken.None).ConfigureAwait(true);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_disposed || !ReferenceEquals(page, _projectPage))
                {
                    return;
                }

                _projectPageFailures = alive ? 0 : _projectPageFailures + 1;
                if (_projectPageFailures >= 2)
                {
                    KubunoLog.WriteLine($"[web-designer] the dev server at {page} stopped answering: bundled design host.");
                    ShowBundledPage(DesignSurfaceRuntimeState.NotBuilt);
                }

                return;
            }

            var found = await DesignServer.FindAsync(Project.Root, CancellationToken.None).ConfigureAwait(true);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_disposed || _projectPage is not null)
            {
                return;
            }

            if (found is not null)
            {
                ShowProjectPage(found);
            }
            else if (_runtime.State == DesignSurfaceRuntimeState.Building && !DesignServer.IsStarting(Project.Root))
            {
                ShowBundledPage(DesignSurfaceRuntimeState.Failed);
            }
        }

        /// <summary>The runtime bar's action: start the dev server, cancel its start, or retry.</summary>
        private void OnRuntimeAction(DesignSurfaceRuntimeState state)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            switch (state)
            {
                case DesignSurfaceRuntimeState.NotBuilt:
                case DesignSurfaceRuntimeState.Failed:
                    if (DesignServer.Start(Project.Root) is { } error)
                    {
                        ShowBundledPage(DesignSurfaceRuntimeState.Failed, error);
                    }
                    else
                    {
                        ShowBundledPage(DesignSurfaceRuntimeState.Building);
                    }

                    break;
                case DesignSurfaceRuntimeState.Building:
                    DesignServer.Stop(Project.Root);
                    ShowBundledPage(DesignSurfaceRuntimeState.NotBuilt);
                    break;
            }
        }

        private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            KubunoLog.WriteLine($"[web-designer] WebView2 process failed: {e.ProcessFailedKind} ({e.Reason}, exit {e.ExitCode}).");
            _pageReady = false;
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
            {
                ShowFailure(T("The WebView2 browser process ended (", "Le processus du navigateur WebView2 s'est arrêté (") + e.Reason + T("). Close and reopen the document.", "). Fermez et rouvrez le document."));
            }
            else if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                Restart();
            }
        }

        private void ShowFailure(string message)
        {
            _webView.Visibility = Visibility.Collapsed;
            _failure.Text = message;
            _failure.Visibility = Visibility.Visible;
            KubunoLog.WriteLine("[web-designer] " + message);
        }

        /// <summary>Reloads the page (the « Relancer » action, a crashed renderer); the page then gets the whole state again.</summary>
        public void Restart()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _pageReady = false;
            _webView.CoreWebView2?.Reload();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _monitor.Stop();
            VSColorTheme.ThemeChanged -= OnVsThemeChanged;
            _designDataWatcher?.Dispose();
            DestroyDropOverlay();
            _webView.PreviewKeyDown -= OnWebViewPreviewKeyDown;
            _webView.WebMessageReceived -= OnWebMessageReceived;
            _webView.NavigationCompleted -= OnNavigationCompleted;
            if (_webView.CoreWebView2 is { } core)
            {
                core.ProcessFailed -= OnProcessFailed;
            }

            _webView.Dispose();
        }

        private static string T(string english, string french) => Kubuno.Views.Designer.DesignerText.IsFrench ? french : english;
    }
}

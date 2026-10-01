using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kubuno.Core.Logging;
using Kubuno.Web.Logic.WebDesigner;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Kubuno.Web.WebDesigner.Spike
{
    /// <summary>
    /// SPIKE (docs/WEB-VIEWS.md, WV-9a): the content of the spike's Design window - a small toolbar (drop channel, DPI
    /// simulation, reload, developer tools) over the WPF <see cref="WebView2"/> control showing the test page from
    /// Kubuno.Web.dll's resources. Owns the WebView2 plumbing only: the environment, the page's resources, the messages,
    /// the Toolbox drop target and the measurements; <see cref="WebDesignSpikePane"/> owns the Visual Studio side.
    /// </summary>
    internal sealed class WebSurfaceView : DockPanel, IDisposable
    {
        private readonly WebView2 _webView;
        private readonly TextBlock _status;
        private readonly Button _channelButton;
        private readonly Button _dpiButton;
        private readonly TextBlock _failure;
        private readonly Queue<string> _pending = new Queue<string>();
        private readonly Func<KeyEventArgs, bool> _onKey;
        private HostDropTarget? _dropTarget;
        private IntPtr _dropWindow;
        private readonly System.Windows.Threading.DispatcherTimer _overlayTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        private bool _pageReady;
        private bool _disposed;
        private bool _simulated100;
        private bool _specimen;

        public WebSurfaceView(Func<KeyEventArgs, bool> onKey)
        {
            _onKey = onKey;
            Focusable = true;

            // Tab past the page's last element (WebView2's MoveFocusRequested, which the WPF control turns into a WPF
            // MoveFocus) must land on the pane's own toolbar, then come back into the page: cycle inside the pane.
            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
            SetResourceReference(BackgroundProperty, EnvironmentColors.DesignerBackgroundBrushKey);

            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 3, 4, 3) };
            _channelButton = BarButton(string.Empty, (_, _) => Channel = Channel == DropChannel.Host ? DropChannel.Html5 : DropChannel.Host);
            _dpiButton = BarButton("Simulate 100 %", (_, _) => ToggleSimulated100());
            bar.Children.Add(_channelButton);
            bar.Children.Add(_dpiButton);
            bar.Children.Add(BarButton("Font specimen", (_, _) => { _specimen = !_specimen; Post(WebSurfaceProtocol.EncodeShowFontSpecimen(_specimen)); }));
            bar.Children.Add(BarButton("Reload", (_, _) => Reload()));
            bar.Children.Add(BarButton("DevTools", (_, _) => _webView?.CoreWebView2?.OpenDevToolsWindow()));
            _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            _status.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            bar.Children.Add(_status);
            var barBorder = new Border { Child = bar, BorderThickness = new Thickness(0, 0, 0, 1) };
            barBorder.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.CommandBarGradientBeginBrushKey);
            barBorder.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            SetDock(barBorder, Dock.Top);
            Children.Add(barBorder);

            _failure = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16), Visibility = Visibility.Collapsed };
            _failure.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            SetDock(_failure, Dock.Top);
            Children.Add(_failure);

            _webView = new WebView2 { Visibility = Visibility.Hidden };
            _webView.PreviewKeyDown += OnWebViewPreviewKeyDown;
            _webView.WebMessageReceived += OnWebMessageReceived;
            _webView.NavigationCompleted += (_, e) => KubunoLog.WriteLine($"[web-spike] navigation completed: success={e.IsSuccess} status={e.WebErrorStatus}");
            Children.Add(_webView);
            UpdateChannelButton();

            IsKeyboardFocusWithinChanged += (_, e) => KubunoLog.WriteLine($"[web-spike] WPF keyboard focus within the surface: {e.NewValue} ({Keyboard.FocusedElement?.GetType().Name})");
            GotKeyboardFocus += (_, e) =>
            {
                // The frame gave the pane the focus (activation, Ctrl+Tab): hand it to the page.
                if (ReferenceEquals(e.NewFocus, this) && _webView.Visibility == Visibility.Visible)
                {
                    _webView.Focus();
                }
            };
        }

        /// <summary>A message the page posted, parsed (UI thread).</summary>
        public event EventHandler<WebSurfaceMessage>? MessageReceived;

        /// <summary>The page's <c>devicePixelRatio</c> (its last <c>metrics</c>): page CSS px = window physical px / this.</summary>
        public double DevicePixelRatio { get; private set; }

        /// <summary>The WebView2 control's window (the HwndHost's), where the host drop target is registered.</summary>
        public IntPtr WindowHandle => _webView.Handle;

        private DropChannel _channel = DropChannel.Host;

        /// <summary>The Toolbox drag channel (see <see cref="DropChannel"/>).</summary>
        public DropChannel Channel
        {
            get => _channel;
            set
            {
                _channel = value;
                UpdateChannelButton();
                if (_webView.CoreWebView2 is not null)
                {
                    // Both channels need Chromium to see the drag: the host channel uses it to detect the drag only.
                    _webView.AllowExternalDrop = true;
                    KubunoLog.WriteLine($"[web-spike] drop channel {value}: AllowExternalDrop={_webView.AllowExternalDrop}");
                }

                Post(WebSurfaceProtocol.EncodeSetDropChannel(value));
            }
        }

        /// <summary>The page's answer about the current drag target (host channel).</summary>
        public void SetDropTargetValid(bool valid)
        {
            if (_dropTarget is not null)
            {
                _dropTarget.TargetValid = valid;
            }
        }

        /// <summary>Creates the environment and the controller, then loads the page. Returns why it could not, or null.</summary>
        public async Task<string?> InitializeAsync(bool darkTheme, Action<string> dropped)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var version = WebView2Runtime.AvailableVersion(out var runtimeError);
            if (version is null)
            {
                return ShowFailure("The WebView2 runtime is missing: " + runtimeError);
            }

            try
            {
                var started = System.Diagnostics.Stopwatch.StartNew();
                var environment = await WebView2Runtime.EnvironmentAsync();
                var options = environment.CreateCoreWebView2ControllerOptions();

                // Experiment switch (findings, question 1): route the WebView2's input through Visual Studio's own message
                // loop first. Off by default: the accelerator path below works without it.
                options.AllowHostInputProcessing = Environment.GetEnvironmentVariable("KUBUNO_WEBVIEW2_HOST_INPUT") == "1";
                await _webView.EnsureCoreWebView2Async(environment, options);
                if (_disposed)
                {
                    return "closed";
                }

                var core = _webView.CoreWebView2!;
                var settings = core.Settings;
                settings.AreBrowserAcceleratorKeysEnabled = false; // no Ctrl+F, Ctrl+P, F5, Ctrl+R, zoom keys of the browser
                settings.AreDefaultContextMenusEnabled = false;
                settings.IsStatusBarEnabled = false;
                settings.IsZoomControlEnabled = false;
                settings.IsPinchZoomEnabled = false;
                settings.IsSwipeNavigationEnabled = false;
                settings.AreDefaultScriptDialogsEnabled = false;
                settings.IsWebMessageEnabled = true;
                core.Profile.PreferredColorScheme = darkTheme ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
                core.AddWebResourceRequestedFilter(WebDesignSpikeConstants.PageOrigin + "*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += OnWebResourceRequested;
                core.NewWindowRequested += (_, e) => e.Handled = true;
                core.ProcessFailed += OnProcessFailed;

                _webView.AllowExternalDrop = true;
                RegisterDropTarget(dropped);
                if (Controller() is { } controller)
                {
                    // Diagnostics of question 1: focus entering/leaving the browser, Tab past the page's ends.
                    controller.GotFocus += (_, _) => KubunoLog.WriteLine("[web-spike] browser got the focus");
                    controller.LostFocus += (_, _) => KubunoLog.WriteLine("[web-spike] browser lost the focus");
                    controller.MoveFocusRequested += (_, e) => KubunoLog.WriteLine($"[web-spike] MoveFocusRequested {e.Reason} (handled by the WPF control: {e.Handled})");
                }

                _webView.Visibility = Visibility.Visible;
                core.Navigate(WebDesignSpikeConstants.PageOrigin + "index.html");
                KubunoLog.WriteLine($"[web-spike] WebView2 {version} ready in {started.ElapsedMilliseconds} ms (browser pid {core.BrowserProcessId}, host input processing {options.AllowHostInputProcessing}).");
                return null;
            }
            catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or IOException)
            {
                KubunoLog.WriteException("[web-spike] WebView2 initialization failed", ex);
                return ShowFailure("The web design surface could not start WebView2: " + ex.Message);
            }
        }

        /// <summary>Sends a protocol message (queued until the page's handshake).</summary>
        public void Post(string json)
        {
            if (_pageReady && _webView.CoreWebView2 is { } core)
            {
                core.PostWebMessageAsJson(json);
            }
            else
            {
                _pending.Enqueue(json);
            }
        }

        /// <summary>The page completed its handshake: flush what was queued before.</summary>
        public void MarkPageReady()
        {
            _pageReady = true;
            while (_pending.Count > 0 && _webView.CoreWebView2 is { } core)
            {
                core.PostWebMessageAsJson(_pending.Dequeue());
            }
        }

        public void SetPreferredColorScheme(bool dark)
        {
            if (_webView.CoreWebView2 is { } core)
            {
                core.Profile.PreferredColorScheme = dark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
            }
        }

        /// <summary>Records the page's metrics and compares them with WPF's view of the control (DPI measurement).</summary>
        public void OnMetrics(MetricsMessage metrics)
        {
            DevicePixelRatio = metrics.DevicePixelRatio;
            var dpi = VisualTreeHelper.GetDpi(_webView);
            var physicalWidth = Math.Round(_webView.ActualWidth * dpi.DpiScaleX);
            var pageWidth = Math.Round(metrics.Width * metrics.DevicePixelRatio);
            _status.Text = string.Format(CultureInfo.InvariantCulture, "dpr {0:0.###} · WPF scale {1:0.###} · {2:0}×{3:0} CSS px", metrics.DevicePixelRatio, dpi.DpiScaleX, metrics.Width, metrics.Height);
            KubunoLog.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "[web-spike] metrics: page dpr {0:0.###}, viewport {1:0.#}x{2:0.#} CSS px = {3} physical px wide; WPF control {4:0.#} DIP x scale {5:0.###} = {6} physical px",
                metrics.DevicePixelRatio, metrics.Width, metrics.Height, pageWidth, _webView.ActualWidth, dpi.DpiScaleX, physicalWidth));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _overlayTimer.Stop();
            if (_dropWindow != IntPtr.Zero)
            {
                HostDropTarget.RevokeDragDrop(_dropWindow);
                DestroyWindow(_dropWindow);
                _dropWindow = IntPtr.Zero;
            }

            _webView.PreviewKeyDown -= OnWebViewPreviewKeyDown;
            _webView.WebMessageReceived -= OnWebMessageReceived;
            if (_webView.CoreWebView2 is { } core)
            {
                core.WebResourceRequested -= OnWebResourceRequested;
                core.ProcessFailed -= OnProcessFailed;
            }

            _webView.Dispose();
        }

        /// <summary>
        /// The host channel's drop target: an almost transparent child window (layered, alpha 1/255 so that it is still
        /// hit-tested; <c>SS_NOTIFY</c> so the static control does not answer HTTRANSPARENT) laid over the browser's
        /// windows, hidden until the page reports a Toolbox drag. An <c>IDropTarget</c> on the WebView2 control's own window
        /// (or on the in-process Chrome_WidgetWin_0 under it) is never called: OLE stops at the browser's cross-process
        /// windows (measured, WV-9a).
        /// </summary>
        private void RegisterDropTarget(Action<string> dropped)
        {
            _dropTarget = new HostDropTarget(CursorInPage, Post, dropped, HideDropOverlay);
            _dropWindow = CreateWindowEx(WsExLayered, "Static", null, WsChild | WsClipSiblings | SsNotify, 0, 0, 1, 1, _webView.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_dropWindow == IntPtr.Zero)
            {
                KubunoLog.WriteLine("[web-spike] could not create the drop overlay window");
                return;
            }

            SetLayeredWindowAttributes(_dropWindow, 0, 1, LwaAlpha);
            var hr = HostDropTarget.RegisterDragDrop(_dropWindow, _dropTarget);
            KubunoLog.WriteLine($"[web-spike] host drop target on overlay window 0x{_dropWindow.ToInt64():X}: RegisterDragDrop hr=0x{hr:X8}");
            _overlayTimer.Tick += (_, _) =>
            {
                // Safety net: a drag that ended without a DragLeave/Drop on the overlay (cancelled elsewhere).
                if ((GetAsyncKeyState(0x01) & 0x8000) == 0 && (GetAsyncKeyState(0x02) & 0x8000) == 0)
                {
                    HideDropOverlay();
                }
            };
        }

        /// <summary>The page reported a Toolbox drag: lay the overlay over the page so that Visual Studio's drop target takes the drag.</summary>
        public void ShowDropOverlay()
        {
            if (_dropWindow == IntPtr.Zero || !GetClientRect(_webView.Handle, out var rect))
            {
                return;
            }

            SetWindowPos(_dropWindow, IntPtr.Zero, 0, 0, rect.Right, rect.Bottom, SwpShowWindow | SwpNoActivate);
            _overlayTimer.Start();
            KubunoLog.WriteLine($"[web-spike] drop overlay raised ({rect.Right}x{rect.Bottom} px)");
        }

        private void HideDropOverlay()
        {
            _overlayTimer.Stop();
            if (_dropWindow != IntPtr.Zero && IsWindowVisible(_dropWindow))
            {
                ShowWindow(_dropWindow, 0);
                KubunoLog.WriteLine("[web-spike] drop overlay hidden");
            }
        }
        /// <summary>The cursor in page CSS pixels (physical client pixels of the WebView2 window / devicePixelRatio), null when outside.</summary>
        private (double X, double Y)? CursorInPage()
        {
            if (!GetCursorPos(out var point) || _dropWindow == IntPtr.Zero || !ScreenToClient(_dropWindow, ref point))
            {
                return null;
            }

            var scale = DevicePixelRatio > 0 ? DevicePixelRatio : VisualTreeHelper.GetDpi(_webView).DpiScaleX;
            return (point.X / scale, point.Y / scale);
        }

        private void OnWebViewPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // The WPF control raises WebView2's AcceleratorKeyPressed as PreviewKeyDown/KeyDown; Handled = true keeps the
            // key from the page.
            if (_onKey(e))
            {
                e.Handled = true;
            }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string json;
            try
            {
                json = e.WebMessageAsJson;
            }
            catch (ArgumentException)
            {
                return;
            }

            if (!e.Source.StartsWith(WebDesignSpikeConstants.PageOrigin, StringComparison.Ordinal))
            {
                KubunoLog.WriteLine("[web-spike] message from an unexpected origin ignored: " + e.Source);
                return;
            }

            if (WebSurfaceProtocol.TryParse(json, out var message) && message is not null)
            {
                MessageReceived?.Invoke(this, message);
            }
            else
            {
                KubunoLog.WriteLine("[web-spike] unrecognized page message: " + (json.Length > 300 ? json.Substring(0, 300) : json));
            }
        }

        private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            var path = new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');
            var stream = path.Contains("..") ? null : typeof(WebSurfaceView).Assembly.GetManifestResourceStream("WebSurfaceSpike/" + (path.Length == 0 ? "index.html" : path.Replace('/', '\\')));
            var environment = _webView.CoreWebView2?.Environment;
            if (environment is null)
            {
                return;
            }

            if (stream is null)
            {
                e.Response = environment.CreateWebResourceResponse(null, 404, "Not Found", "Content-Type: text/plain");
                return;
            }

            var type = Path.GetExtension(path) switch
            {
                ".js" => "text/javascript",
                ".css" => "text/css",
                ".woff2" => "font/woff2",
                ".txt" => "text/plain",
                _ => "text/html",
            };
            e.Response = environment.CreateWebResourceResponse(stream, 200, "OK", $"Content-Type: {type}" + (type.StartsWith("font/", StringComparison.Ordinal) ? string.Empty : "; charset=utf-8") + "\r\nCache-Control: no-store");
        }

        private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        {
            KubunoLog.WriteLine($"[web-spike] WebView2 process failed: {e.ProcessFailedKind} ({e.Reason}, exit {e.ExitCode})");
            _pageReady = false;
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
            {
                ShowFailure("The WebView2 browser process ended (" + e.Reason + "). Close and reopen the document.");
            }
            else if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited || e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                Reload();
            }
        }

        private void Reload()
        {
            _pageReady = false;
            _webView.CoreWebView2?.Navigate(WebDesignSpikeConstants.PageOrigin + "index.html");
        }

        /// <summary>
        /// Measurement aid: a per-monitor DPI change seen by the page, without moving the window to another monitor - the
        /// controller's RasterizationScale forced to 1.0 (what 100 % gives) and back to the monitor's. The WPF control
        /// does not expose its controller: read through reflection, spike only.
        /// </summary>
        private void ToggleSimulated100()
        {
            if (Controller() is not { } controller)
            {
                KubunoLog.WriteLine("[web-spike] no controller to simulate a DPI change");
                return;
            }

            _simulated100 = !_simulated100;
            controller.ShouldDetectMonitorScaleChanges = !_simulated100;
            controller.RasterizationScale = _simulated100 ? 1.0 : VisualTreeHelper.GetDpi(_webView).DpiScaleX;
            _dpiButton.Content = _simulated100 ? "Monitor DPI" : "Simulate 100 %";
            KubunoLog.WriteLine($"[web-spike] RasterizationScale set to {controller.RasterizationScale} (monitor detection {controller.ShouldDetectMonitorScaleChanges})");
        }

        /// <summary>The control's controller: not public in the WPF control, read through reflection (spike only).</summary>
        private CoreWebView2Controller? Controller()
        {
            // WebView2 (an HwndHost) delegates to a private WebView2Base (m_webview2Base) that owns the controller.
            var inner = typeof(WebView2).GetField("m_webview2Base", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(_webView);
            return inner?.GetType().GetProperty("CoreWebView2Controller", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(inner) as CoreWebView2Controller;
        }

        private string ShowFailure(string message)
        {
            _webView.Visibility = Visibility.Collapsed;
            _failure.Text = message;
            _failure.Visibility = Visibility.Visible;
            KubunoLog.WriteLine("[web-spike] " + message);
            return message;
        }

        private void UpdateChannelButton() =>
            _channelButton.Content = Channel == DropChannel.Host ? "Drop: host IDropTarget" : "Drop: HTML5 text/plain";

        private static Button BarButton(string text, RoutedEventHandler click)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(8, 1, 8, 1), MinWidth = 0 };
            button.SetResourceReference(StyleProperty, VsResourceKeys.ButtonStyleKey);
            button.Click += click;
            return button;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint point);

        private const int WsExLayered = 0x00080000;
        private const int WsChild = 0x40000000;
        private const int WsClipSiblings = 0x04000000;
        private const int SsNotify = 0x00000100;
        private const uint LwaAlpha = 0x2;
        private const uint SwpShowWindow = 0x0040;
        private const uint SwpNoActivate = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string? windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint point);
    }
}

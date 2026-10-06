using System;
using Kubuno.Shared.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.Settings;

namespace Kubuno.Views.Designer.ToolWindows
{
    /// <summary>
    /// The Windows Forms layout of the designer's tool windows (docs/DESIGNER.md section 11): Visual Studio's Toolbox docked
    /// - pinned, never auto-hidden - on the left, and its Properties window docked in the right column, below Solution
    /// Explorer.
    /// <list type="bullet">
    /// <item>The first time a designer opens for this user (<see cref="AppliedProperty"/> in the user settings store), both
    /// windows are docked there and shown.</item>
    /// <item>Afterwards the user's own arrangement wins: a designer only brings back a window that is closed, where the
    /// user last left it.</item>
    /// <item>"Tools &gt; Kubuno: Reset Designer Window Layout" (<see cref="ResetCommand"/>) docks them again.</item>
    /// </list>
    /// The Toolbox is pinned because an auto-hidden tool window's flyout is drawn UNDER a native child window - the WebView2
    /// of the web design surface, the Rust surface's HWND - so its list was invisible over the surface (docs/WEB-VIEWS.md,
    /// "Airspace").
    /// </summary>
    public static class DesignerToolLayout
    {
        /// <summary>The layout version applied for this user (0 = never); bump <see cref="LayoutVersion"/> to apply a new default once.</summary>
        public const string AppliedProperty = "DefaultToolLayoutVersion";

        /// <summary>The current default layout.</summary>
        public const int LayoutVersion = 1;

        /// <summary>Solution Explorer's tool window GUID (VSConstants.StandardToolWindows.SolutionExplorer).</summary>
        private static readonly Guid SolutionExplorer = new Guid("3AE79031-E1BC-11D0-8F78-00A0C9110057");

        /// <summary>Called when a designer pane is created: once the frame is on screen, applies (or restores) the layout.</summary>
        public static void OnDesignerOpened()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // On idle: the document frame is shown by then, and docking never runs inside CreateEditorInstance.
            _ = ThreadHelper.JoinableTaskFactory.StartOnIdle(() =>
            {
                try
                {
                    Ensure(reset: false);
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
                {
                    KubunoLog.WriteException("[designer] could not arrange the Toolbox and the Properties window", ex);
                }
            });
        }

        /// <summary>The "Reset Designer Window Layout" command: docks both windows again in the default places.</summary>
        public static void ResetCommand()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                Ensure(reset: true);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
            {
                KubunoLog.WriteException("[designer] could not reset the designer window layout", ex);
            }
        }

        private static void Ensure(bool reset)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsUIShell)) is not IVsUIShell shell)
            {
                return;
            }

            var toolbox = FindOrCreate(shell, VSConstants.StandardToolWindows.Toolbox);
            var properties = FindOrCreate(shell, VSConstants.StandardToolWindows.Properties);
            if (toolbox is null || properties is null)
            {
                return;
            }

            var store = SettingsStore();
            var applied = ReadAppliedVersion(store);
            if (reset || applied < LayoutVersion)
            {
                var placed = ApplyDefault(shell, toolbox, properties);
                WriteAppliedVersion(store);
                var outcome = placed
                    ? "Toolbox docked on the left (pinned), Properties docked below Solution Explorer"
                    : "Toolbox and Properties shown; a window that could not be docked was left where it was (see above)";
                KubunoLog.WriteLine(reset
                    ? "[designer] designer window layout reset: " + outcome + "."
                    : "[designer] first designer opened: " + outcome + " (applied once; your own arrangement is kept from now on - Tools > Kubuno: Reset Designer Window Layout docks them again).");
                return;
            }

            // The user's arrangement is kept; a closed window comes back where it was last docked.
            ShowIfClosed(toolbox, VSConstants.StandardToolWindows.Toolbox, "Toolbox");
            ShowIfClosed(properties, VSConstants.StandardToolWindows.Properties, "Properties");
        }

        private static IVsWindowFrame? FindOrCreate(IVsUIShell shell, Guid toolWindow)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var guid = toolWindow;
            return ErrorHandler.Succeeded(shell.FindToolWindow((uint)__VSFINDTOOLWIN.FTW_fForceCreate, ref guid, out var frame)) ? frame : null;
        }

        /// <summary>Shows, pins and docks both windows; false when one of them could not be docked.</summary>
        private static bool ApplyDefault(IVsUIShell shell, IVsWindowFrame toolbox, IVsWindowFrame properties)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var placed = true;

            // The Toolbox: shown and pinned (an auto-hidden one goes back to its docked place), then docked along the left
            // edge of the main window unless it already is there.
            ErrorHandler.ThrowOnFailure(toolbox.ShowNoActivate());
            Unpin(toolbox);
            var main = MainWindowBounds();
            if (Bounds(toolbox) is not { } box || main is null || box.Floating || box.Left > main.Value.Left + main.Value.Width / 4)
            {
                placed &= Dock("Toolbox", WindowManagerDocking.TryDockOutside(VSConstants.StandardToolWindows.Toolbox, "Left", out var error), error);
            }

            // The Properties window: below Solution Explorer, in the right column, unless it already is there.
            ErrorHandler.ThrowOnFailure(properties.ShowNoActivate());
            Unpin(properties);
            var solutionExplorer = SolutionExplorer;
            if (ErrorHandler.Succeeded(shell.FindToolWindow((uint)__VSFINDTOOLWIN.FTW_fForceCreate, ref solutionExplorer, out var explorerFrame)) &&
                explorerFrame is not null)
            {
                explorerFrame.ShowNoActivate();
                var explorer = Bounds(explorerFrame);
                var props = Bounds(properties);
                var inPlace = explorer is { Floating: false } se && props is { Floating: false } p &&
                    Math.Abs(p.Left - se.Left) <= 16 && p.Top >= se.Top + se.Height - 16;
                if (!inPlace)
                {
                    placed &= Dock("Properties", WindowManagerDocking.TryDockBelow(VSConstants.StandardToolWindows.Properties, SolutionExplorer, out var error), error);
                }
            }

            toolbox.ShowNoActivate();
            properties.ShowNoActivate();
            return placed;
        }

        private static bool Dock(string name, bool done, string? error)
        {
            if (!done)
            {
                KubunoLog.WriteLine($"[designer] could not dock the {name} window ({error}); it is left where it is.");
            }

            return done;
        }

        private readonly struct WindowBounds
        {
            public WindowBounds(int left, int top, int width, int height, bool floating)
            {
                Left = left;
                Top = top;
                Width = width;
                Height = height;
                Floating = floating;
            }

            public int Left { get; }

            public int Top { get; }

            public int Width { get; }

            public int Height { get; }

            public bool Floating { get; }
        }

        /// <summary>A tool window's position on screen (its DTE window), or null while it has none (auto-hidden, hidden).</summary>
        private static WindowBounds? Bounds(IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_ExtWindowObject, out var value)) || value is not EnvDTE.Window window)
            {
                return null;
            }

            try
            {
                return window.AutoHides ? null : new WindowBounds(window.Left, window.Top, window.Width, window.Height, window.IsFloating);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return null;
            }
        }

        private static WindowBounds? MainWindowBounds()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(EnvDTE.DTE)) is not EnvDTE.DTE dte)
            {
                return null;
            }

            var main = dte.MainWindow;
            return new WindowBounds(main.Left, main.Top, main.Width, main.Height, floating: false);
        }
        /// <summary>Turns auto-hide off (the window's DTE object: <c>AutoHides</c>), the only supported switch for it.</summary>
        private static void Unpin(IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ErrorHandler.Succeeded(frame.GetProperty((int)__VSFPROPID.VSFPROPID_ExtWindowObject, out var value)) &&
                value is EnvDTE.Window window)
            {
                try
                {
                    if (window.AutoHides)
                    {
                        window.AutoHides = false;
                    }
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // A floating or document-well window has no auto-hide state.
                }
            }
        }

        private static void ShowIfClosed(IVsWindowFrame frame, Guid toolWindow, string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // An auto-hidden window the user chose to keep collapsed is not "closed": it is left alone.
            if (frame.IsVisible() != VSConstants.S_OK && !WindowManagerDocking.IsAutoHidden(toolWindow) &&
                ErrorHandler.Succeeded(frame.ShowNoActivate()))
            {
                KubunoLog.WriteLine($"[designer] the {name} window was closed: shown again where it was last docked.");
            }
        }

        private static WritableSettingsStore? SettingsStore()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return new ShellSettingsManager(ServiceProvider.GlobalProvider).GetWritableSettingsStore(SettingsScope.UserSettings);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                KubunoLog.WriteException("[designer] the user settings store is unavailable", ex);
                return null;
            }
        }

        private static int ReadAppliedVersion(WritableSettingsStore? store) =>
            store is not null && store.CollectionExists(DesignerConstants.UserSettingsCollection) && store.PropertyExists(DesignerConstants.UserSettingsCollection, AppliedProperty)
                ? store.GetInt32(DesignerConstants.UserSettingsCollection, AppliedProperty, 0)
                : 0;

        private static void WriteAppliedVersion(WritableSettingsStore? store)
        {
            if (store is null)
            {
                return;
            }

            if (!store.CollectionExists(DesignerConstants.UserSettingsCollection))
            {
                store.CreateCollection(DesignerConstants.UserSettingsCollection);
            }

            store.SetInt32(DesignerConstants.UserSettingsCollection, AppliedProperty, LayoutVersion);
        }
    }
}

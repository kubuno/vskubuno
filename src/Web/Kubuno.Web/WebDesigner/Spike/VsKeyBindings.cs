using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.Web.WebDesigner.Spike
{
    /// <summary>
    /// SPIKE (docs/WEB-VIEWS.md, WV-9a, question 1): runs Visual Studio's key bindings for a key the WebView2 reported
    /// (<c>AcceleratorKeyPressed</c>) - the key itself never entered Visual Studio's message loop. Two steps, so that the
    /// browser (blocked while the accelerator handler runs) is released at once:
    /// <list type="number">
    /// <item><c>IVsFilterKeys2.TranslateAcceleratorEx</c> with <c>VSTAEXF_NoFireCommand</c>, on a WM_KEYDOWN built from the
    /// key: which command (if any) is bound to it in the active key binding scopes - the handler then marks the key
    /// handled, so the page never sees it;</item>
    /// <item><c>IVsUIShell.PostExecCommand</c> of that command: Visual Studio runs it after the handler returned, through
    /// the normal command routing (the active document's pane first: Edit.Undo/Redo/Delete reach the design pane).</item>
    /// </list>
    /// The modifier state comes from <c>GetKeyState</c>: the WebView2's windows belong to the browser process but are
    /// children of a Visual Studio window, and Windows attaches the input queues of such threads, so Visual Studio's
    /// thread sees Ctrl/Shift/Alt down.
    /// </summary>
    internal static class VsKeyBindings
    {
        private const uint WmKeyDown = 0x0100;
        private const uint WmSysKeyDown = 0x0104;

        /// <summary>The command bound to the key, or false. Never throws.</summary>
        public static bool TryFind(IntPtr hwnd, int virtualKey, bool alt, out Guid group, out uint id, out string? diagnostic)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            group = Guid.Empty;
            id = 0;
            try
            {
                if (Package.GetGlobalService(typeof(SVsFilterKeys)) is not IVsFilterKeys2 filterKeys)
                {
                    diagnostic = "SVsFilterKeys unavailable";
                    return false;
                }

                var scan = MapVirtualKey((uint)virtualKey, 0);
                var lParam = new IntPtr(1 | (int)(scan << 16) | (alt ? 1 << 29 : 0));
                var message = new OleInterop.MSG { hwnd = hwnd, message = alt ? WmSysKeyDown : WmKeyDown, wParam = new IntPtr(virtualKey), lParam = lParam };
                var hr = filterKeys.TranslateAcceleratorEx(
                    new[] { message },
                    (uint)__VSTRANSACCELEXFLAGS.VSTAEXF_NoFireCommand,
                    0,
                    Array.Empty<Guid>(),
                    out group,
                    out id,
                    out var translated,
                    out var startsMultiKeyChord);
                diagnostic = $"hr={hr:X8} found={translated != 0} chord={startsMultiKeyChord != 0}";
                return hr == VSConstants.S_OK && translated != 0 && group != Guid.Empty;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
            {
                diagnostic = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>Queues the command for execution once the current message is done (<c>IVsUIShell.PostExecCommand</c>).</summary>
        public static bool Post(Guid group, uint id)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsUIShell)) is not IVsUIShell shell)
            {
                return false;
            }

            object? none = null;
            return shell.PostExecCommand(ref group, id, 0, ref none) == VSConstants.S_OK;
        }

        /// <summary>The command's canonical name (<c>File.SaveSelectedItems</c>) for the log, or its guid:id.</summary>
        public static string NameOf(Guid group, uint id)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (Package.GetGlobalService(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte &&
                    dte.Commands.Item("{" + group.ToString().ToUpperInvariant() + "}", (int)id) is { } command &&
                    !string.IsNullOrEmpty(command.Name))
                {
                    return command.Name;
                }
            }
            catch (Exception ex) when (ex is COMException or ArgumentException)
            {
                // Unnamed command: the id is enough.
            }

            return $"{{{group}}}:{id}";
        }

        /// <summary>True when the key is down for the calling thread (Visual Studio's view of the keyboard).</summary>
        public static bool IsDown(int virtualKey) => (GetKeyState(virtualKey) & 0x8000) != 0;

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int virtualKey);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);
    }
}

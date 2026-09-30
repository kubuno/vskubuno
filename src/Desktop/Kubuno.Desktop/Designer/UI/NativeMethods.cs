using System;
using System.Runtime.InteropServices;

namespace Kubuno.Desktop.Designer.UI
{
    /// <summary>
    /// The one Win32 call <see cref="CodeWindowHost"/> needs to keep the native
    /// <c>IVsCodeWindow</c> child window's size in sync with WPF layout (see that class's
    /// <c>OnWindowPositionChanged</c> override).
    /// </summary>
    internal static class NativeMethods
    {
        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    }
}

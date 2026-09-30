using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Kubuno.Desktop.Logic.Painting
{
    /// <summary>
    /// Win32 side of the paint-debug toggle: posts the registered "Kubuno.PaintDebug" message to every
    /// <c>KubunoControlsHost</c> window (top-level ones and the children embedded in other processes'
    /// window trees, e.g. the designer's design surfaces inside Visual Studio) and mirrors the setting
    /// into this process's environment so that children started afterwards inherit it.
    /// </summary>
    public static class PaintDebugBroadcaster
    {
        private static readonly object Gate = new object();
        private static bool _originalCaptured;
        private static string? _originalValue;

        /// <summary>Posts <paramref name="flags"/> (0 = off) to every host window; returns how many were reached.</summary>
        public static int Broadcast(int flags)
        {
            var message = RegisterWindowMessage(PaintDebug.WindowMessageName);
            if (message == 0)
            {
                return 0;
            }

            var all = new List<KeyValuePair<IntPtr, string?>>();
            EnumChildWindowsProc collectChild = (child, _) =>
            {
                all.Add(new KeyValuePair<IntPtr, string?>(child, ClassNameOf(child)));
                return true;
            };
            EnumWindows((top, _) =>
            {
                all.Add(new KeyValuePair<IntPtr, string?>(top, ClassNameOf(top)));
                EnumChildWindows(top, collectChild, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
            GC.KeepAlive(collectChild);

            var posted = 0;
            foreach (var hwnd in PaintDebug.SelectHostWindows(all))
            {
                if (PostMessage(hwnd, message, (IntPtr)flags, IntPtr.Zero))
                {
                    posted++;
                }
            }

            return posted;
        }

        /// <summary>
        /// Sets (or clears) <c>KUBUNO_PAINT_DEBUG</c> in this process so debuggees and design surfaces started
        /// afterwards inherit it. A value the user had set before the first call is never overridden.
        /// </summary>
        public static void ApplyToProcessEnvironment(int flags)
        {
            lock (Gate)
            {
                if (!_originalCaptured)
                {
                    _originalValue = Environment.GetEnvironmentVariable(PaintDebug.EnvironmentVariableName);
                    _originalCaptured = true;
                }

                var desired = PaintDebug.DesiredProcessValue(flags, _originalValue);
                Environment.SetEnvironmentVariable(PaintDebug.EnvironmentVariableName, desired);
            }
        }

        private static string? ClassNameOf(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : null;
        }

        private delegate bool EnumChildWindowsProc(IntPtr hwnd, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
        private static extern uint RegisterWindowMessage(string name);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumChildWindows(IntPtr parent, EnumChildWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

        [DllImport("user32.dll", EntryPoint = "PostMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    }
}

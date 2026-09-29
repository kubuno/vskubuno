using System;
using System.Collections.Generic;
using System.Linq;

namespace Kubuno.Launch
{
    /// <summary>
    /// Pure helpers for the Kubuno runtime's paint-debug overlay (docs/EVENTS.md EVT-8): the flag
    /// bitmask, the <c>KUBUNO_PAINT_DEBUG</c> environment value, the "user value wins" merge and the
    /// window-class filter. No Win32 and no environment access here (see <see cref="PaintDebugBroadcaster"/>).
    /// </summary>
    public static class PaintDebug
    {
        /// <summary>Flashes the invalidated regions.</summary>
        public const int Invalidate = 1;

        /// <summary>Shows layout bounds and padding.</summary>
        public const int Layout = 2;

        /// <summary>Shows the frame time.</summary>
        public const int FrameTime = 4;

        /// <summary>Every overlay.</summary>
        public const int All = Invalidate | Layout | FrameTime;

        /// <summary>Environment variable read by the runtime at launch.</summary>
        public const string EnvironmentVariableName = "KUBUNO_PAINT_DEBUG";

        /// <summary>Registered window message (RegisterWindowMessageW) carrying the flags in wParam.</summary>
        public const string WindowMessageName = "Kubuno.PaintDebug";

        /// <summary>Class name of every Kubuno host window (top-level or embedded child).</summary>
        public const string HostWindowClassName = "KubunoControlsHost";

        /// <summary>The flags a checked / unchecked command stands for.</summary>
        public static int FlagsFor(bool enabled) => enabled ? All : 0;

        /// <summary>
        /// The value of <c>KUBUNO_PAINT_DEBUG</c> for <paramref name="flags"/>: null for none (variable not set),
        /// <c>all</c> for every overlay, otherwise a comma list of <c>invalidate,layout,fps</c>.
        /// </summary>
        public static string? ToEnvironmentValue(int flags)
        {
            flags &= All;
            if (flags == 0)
            {
                return null;
            }

            if (flags == All)
            {
                return "all";
            }

            var parts = new List<string>();
            if ((flags & Invalidate) != 0)
            {
                parts.Add("invalidate");
            }

            if ((flags & Layout) != 0)
            {
                parts.Add("layout");
            }

            if ((flags & FrameTime) != 0)
            {
                parts.Add("fps");
            }

            return string.Join(",", parts);
        }

        /// <summary>
        /// Adds <c>KUBUNO_PAINT_DEBUG</c> to <paramref name="environment"/> (a copy is returned) when
        /// <paramref name="flags"/> is non-zero, unless the user already chose a value: either the map
        /// already holds the variable (launch profile) or <paramref name="ambientValue"/> (the inherited
        /// environment's value) is non-empty.
        /// </summary>
        public static IReadOnlyDictionary<string, string> MergeInto(
            IReadOnlyDictionary<string, string>? environment,
            int flags,
            string? ambientValue)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (environment is not null)
            {
                foreach (var pair in environment)
                {
                    result[pair.Key] = pair.Value;
                }
            }

            var value = ToEnvironmentValue(flags);
            if (value is not null && !result.ContainsKey(EnvironmentVariableName) && string.IsNullOrEmpty(ambientValue))
            {
                result[EnvironmentVariableName] = value;
            }

            return result;
        }

        /// <summary>
        /// The value this process's own environment variable should have so that children inherit the
        /// setting: the user's original value always wins; otherwise the flags' value (null = unset).
        /// </summary>
        public static string? DesiredProcessValue(int flags, string? originalUserValue) =>
            string.IsNullOrEmpty(originalUserValue) ? ToEnvironmentValue(flags) : originalUserValue;

        /// <summary>True when <paramref name="className"/> is a Kubuno host window class (exact match).</summary>
        public static bool IsHostWindowClass(string? className) =>
            string.Equals(className, HostWindowClassName, StringComparison.Ordinal);

        /// <summary>Keeps the handles whose class name is a Kubuno host window class.</summary>
        public static IReadOnlyList<IntPtr> SelectHostWindows(IEnumerable<KeyValuePair<IntPtr, string?>> windows) =>
            windows.Where(w => IsHostWindowClass(w.Value)).Select(w => w.Key).ToList();
    }
}

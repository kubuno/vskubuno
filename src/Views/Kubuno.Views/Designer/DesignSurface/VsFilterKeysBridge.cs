using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.Desktop.Designer.DesignSurface
{
    /// <summary>
    /// Isolates every direct reference to a VS SDK assembly type
    /// (<c>Microsoft.VisualStudio.Interop</c>/<c>Microsoft.VisualStudio.Shell.15.0</c>/...) behind an
    /// <see cref="object"/>-only boundary, so <see cref="RustDesignSurfaceHost"/>'s own ALWAYS-executed
    /// code paths (its constructor, <c>WndProc</c>, <c>HandleUnhandledKey</c>) never mention one of these
    /// types in their OWN method signature or body, and can therefore run standalone (this library's own
    /// tests, <c>spikes/HwndHostSpike</c>) without those assemblies being physically present.
    ///
    /// <para><b>Why this exists at all, exactly - a real, live-observed bug, not caution for its own
    /// sake.</b> Every project in this repo that references <c>Microsoft.VisualStudio.SDK</c> does so
    /// with <c>ExcludeAssets="runtime"</c> (see this project's own csproj comment): the point is that this
    /// library and its spike run OUTSIDE <c>devenv.exe</c>, without its assemblies bundled alongside them.
    /// The .NET JIT resolves every type mentioned in a method's OWN signature or body - a parameter type,
    /// a field type declared ON THE ENCLOSING METHOD's containing type is fine, but a type used INSIDE a
    /// method body, or in ANOTHER method's parameter list that this method calls with a matching
    /// non-erased type - the FIRST TIME that specific method is JIT-compiled (which happens on its first
    /// call), regardless of which runtime branch actually executes. Before this class existed,
    /// <see cref="RustDesignSurfaceHost"/>'s own constructor took an
    /// <c>Microsoft.VisualStudio.OLE.Interop.IServiceProvider?</c> parameter directly and held an
    /// <c>IVsFilterKeys2?</c> field - both VS SDK types - so simply calling <c>new
    /// RustDesignSurfaceHost(exePath)</c> (passing <see langword="null"/> for that parameter, exactly what
    /// the spike does) threw, live: <c>System.IO.FileNotFoundException: Could not load file or assembly
    /// 'Microsoft.VisualStudio.Interop, Version=17.0.0.0, ...'</c> - even though the actual VS-specific
    /// code path was never going to run for a <see langword="null"/> argument. The fix is this class:
    /// every method here is only ever CALLED once a caller already knows it is running inside VS (holds a
    /// non-null service provider/filter-keys object) - checked with a plain reference-equality test that
    /// itself needs no VS type - so JIT compilation of these methods, and the assembly loads it requires,
    /// only happens then.</para>
    /// </summary>
    internal static class VsFilterKeysBridge
    {
        /// <summary>
        /// <paramref name="oleServiceProvider"/> (an <c>Microsoft.VisualStudio.OLE.Interop.
        /// IServiceProvider</c>, passed as <see cref="object"/> so the CALLER never needs that type in its
        /// own signature - see this class's own doc) queried for <c>SVsFilterKeys</c>/<c>IVsFilterKeys2</c>.
        /// Returns the result boxed as <see cref="object"/> (an <c>IVsFilterKeys2</c>), or
        /// <see langword="null"/> for any reason (wrong runtime type passed, service unavailable,
        /// marshaling failure) - never throws, since the caller is its constructor and a missing VS
        /// service must not prevent the pane from opening.
        /// </summary>
        internal static object? TryQuery(object oleServiceProvider)
        {
            // Real callers (DesignerSplitView constructing this host, the spike) are always on the UI
            // thread. ThreadHelper's own lazily-created default JoinableTaskContext (there is no live VS
            // package here) treats whatever thread first touches it as "the" main thread, which is that
            // same UI thread - so this does not throw standalone either, and is a real invariant check,
            // not just quieting the analyzer.
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (oleServiceProvider is not OleInterop.IServiceProvider sp)
                {
                    return null;
                }

                var guidService = typeof(SVsFilterKeys).GUID;
                var guidInterface = typeof(IVsFilterKeys2).GUID;
                var hr = sp.QueryService(ref guidService, ref guidInterface, out var ptr);
                if (hr != 0 || ptr == IntPtr.Zero)
                {
                    // Found live: the package's own QueryService does not hand out IVsFilterKeys2 (the
                    // unhandledKey log showed viaVs=False, so Ctrl+Z on the surface never reached Edit.Undo);
                    // the global service provider does.
                    return Package.GetGlobalService(typeof(SVsFilterKeys)) as IVsFilterKeys2;
                }

                try
                {
                    return Marshal.GetObjectForIUnknown(ptr) as IVsFilterKeys2;
                }
                finally
                {
                    Marshal.Release(ptr);
                }
            }
            catch (Exception ex)
            {
                LogSafely("QueryService(SVsFilterKeys) failed: " + ex);
                return null;
            }
        }

        /// <summary>
        /// <paramref name="vsFilterKeys2"/> (an <c>IVsFilterKeys2</c>, boxed as <see cref="object"/> -
        /// see this class's own doc) <c>.TranslateAcceleratorEx</c> with the forwarded message,
        /// requesting the global keybinding scope (<c>VSTAEXF_UseGlobalKBScope</c> - this is a forwarded
        /// key with no VS window of its own to own a narrower scope). Returns a one-line diagnostic for
        /// the caller to log (keeping this the only class that needs to format a VS-typed result); never
        /// throws - a COM call failing must not crash the designer pane over one forwarded key.
        /// <paramref name="translated"/> reports whether Visual Studio translated (and ran) the key.
        /// </summary>
        internal static string TryTranslateAccelerator(object vsFilterKeys2, IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, out bool translated)
        {
            translated = false;
            // See TryQuery's own comment: always the UI thread, in VS and standalone alike.
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (vsFilterKeys2 is not IVsFilterKeys2 filterKeys)
                {
                    return "IVsFilterKeys2 bridge object was not the expected type.";
                }

                var nativeMsg = new OleInterop.MSG { hwnd = hwnd, message = (uint)msg, wParam = wParam, lParam = lParam };
                var hr = filterKeys.TranslateAcceleratorEx(
                    new[] { nativeMsg },
                    (uint)__VSTRANSACCELEXFLAGS.VSTAEXF_UseGlobalKBScope,
                    0,
                    Array.Empty<Guid>(),
                    out _,
                    out _,
                    out var wasTranslated,
                    out _);
                translated = wasTranslated != 0;
                return $"IVsFilterKeys2.TranslateAcceleratorEx hr={hr:X8} translated={translated}";
            }
            catch (Exception ex)
            {
                return "IVsFilterKeys2.TranslateAcceleratorEx failed: " + ex;
            }
        }

        /// <summary>
        /// <see cref="Kubuno.Desktop.Views.Logging.KubunoViewsLogHost"/> is safe to call from here
        /// (it is a plain, VS-free interface - see its own doc), unlike everything else in this class;
        /// named separately only so a reader scanning for "does this class touch VS types outside its two
        /// public methods" can tell at a glance that it does not.
        /// </summary>
        private static void LogSafely(string message) => Kubuno.Desktop.Views.Logging.KubunoViewsLogHost.Current.WriteLine(message);
    }
}

using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Kubuno.Desktop.Designer.PropertyBrowser;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.Desktop.Designer.Bindings
{
    /// <summary>
    /// What the binding UI needs from Visual Studio's own Properties window (docs/DESIGNER.md, "Data bindings"): the
    /// row selected in its grid (for the context menu and F12). The Properties window is a Windows Forms
    /// <see cref="PropertyGrid"/> in-process; nothing here throws when its shape differs - the features are then simply
    /// absent. (Hooking the grid's own edit box - an auto-complete list - brought Visual Studio down: it is left alone.)
    /// </summary>
    internal static class PropertiesGridBindings
    {
        /// <summary>The selected row of the Properties window, when it is a bindable row of a <c>.kbview</c> element.</summary>
        public static (KbviewElementObject Element, string Attribute, PropertyDescriptor Row)? SelectedRow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (FindGrid() is not { } grid || grid.SelectedGridItem is not { PropertyDescriptor: { } row } item)
            {
                return null;
            }

            switch (row)
            {
                case KbviewAttributePropertyDescriptor attribute when attribute.IsBindable && ElementOf(grid) is { } element:
                    return (element, attribute.AttributeOf(element), row);
                case KbviewBindingPartDescriptor part when item.Parent?.Value is KbviewBindingsValue { Owner: { } owner }:
                    return (owner, part.Name, row);
                default:
                    return null;
            }
        }

        /// <summary>Whether the keyboard focus is in the Properties window's grid (where F12 then means « Aller à la définition » of the row).</summary>
        public static bool HasFocus()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return FindGrid() is { } grid && grid.ContainsFocus;
        }

        private static KbviewElementObject? ElementOf(PropertyGrid grid) => grid.SelectedObject as KbviewElementObject ?? grid.SelectedObjects?.OfType<KbviewElementObject>().FirstOrDefault();

        private static WeakReference<PropertyGrid>? _grid;

        /// <summary>
        /// The Properties window's grid: the Windows Forms <see cref="PropertyGrid"/> among the windows of this (UI) thread that
        /// shows a <c>.kbview</c> element (Visual Studio's own Properties window); null when none does.
        /// </summary>
        public static PropertyGrid? FindGrid()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_grid is not null && _grid.TryGetTarget(out var cached) && !cached.IsDisposed && Shows(cached))
            {
                return cached;
            }

            PropertyGrid? found = null;
            NativeMethods.EnumThreadWindows(NativeMethods.GetCurrentThreadId(), (top, _) =>
            {
                found = Scan(top);
                if (found is null)
                {
                    NativeMethods.EnumChildWindows(top, (child, _) =>
                    {
                        found = Scan(child);
                        return found is null;
                    }, IntPtr.Zero);
                }

                return found is null;
            }, IntPtr.Zero);
            _grid = found is null ? null : new WeakReference<PropertyGrid>(found);
            return found;
        }

        private static PropertyGrid? Scan(IntPtr handle) => Control.FromHandle(handle) is PropertyGrid grid && Shows(grid) ? grid : null;

        private static bool Shows(PropertyGrid grid) => ElementOf(grid) is not null;

        private static class NativeMethods
        {
            public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc callback, IntPtr lParam);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

            [System.Runtime.InteropServices.DllImport("kernel32.dll")]
            public static extern uint GetCurrentThreadId();
        }

    }
}

using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace Kubuno.Views.Designer.ToolWindows
{
    /// <summary>
    /// Docks a tool window through Visual Studio's own window manager (<c>Microsoft.VisualStudio.Shell.ViewManager.dll</c>,
    /// <c>Microsoft.VisualStudio.PlatformUI.Shell.DockOperations</c>) - what a drag onto a dock target does.
    /// <c>IVsWindowFrame.SetFramePos(SFP_fDock | ...)</c>, the documented call, returns E_NOTIMPL in Visual Studio 2026
    /// (verified live), and DTE has no "dock at this edge" either. Reached by reflection on the assembly the shell already
    /// loaded (no build-time reference, nothing loaded that the shell did not): if a later Visual Studio changes these
    /// types, <see cref="TryDockOutside"/>/<see cref="TryDockBelow"/> return false with a reason and the caller leaves the
    /// window where it is.
    /// </summary>
    public static class WindowManagerDocking
    {
        private const string Namespace = "Microsoft.VisualStudio.PlatformUI.Shell.";

        /// <summary>Docks <paramref name="toolWindow"/> (pinned) along the <paramref name="edge"/> of the main window ("Left").</summary>
        public static bool TryDockOutside(Guid toolWindow, string edge, out string? error)
        {
            if (!TryGetModel(out var asm, out var profile, out error))
            {
                return false;
            }

            var view = FindView(profile!, toolWindow);
            var mainSite = Descendants(profile!).FirstOrDefault(e => e.GetType().FullName == Namespace + "MainSite");
            var dockRoot = mainSite is null ? null : Descendants(mainSite).FirstOrDefault(e => e.GetType().FullName == Namespace + "DockRoot");
            if (view is null || dockRoot is null)
            {
                error = view is null ? "tool window view not found among " + DescribeViews(profile!) : "main dock root not found";
                return false;
            }

            return Invoke(asm!, "DockOutside", new[] { dockRoot, view, Direction(asm!, edge) }, out error);
        }

        /// <summary>Docks <paramref name="toolWindow"/> (pinned) below <paramref name="anchor"/> - below its whole tab group.</summary>
        public static bool TryDockBelow(Guid toolWindow, Guid anchor, out string? error)
        {
            if (!TryGetModel(out var asm, out var profile, out error))
            {
                return false;
            }

            var view = FindView(profile!, toolWindow);
            var anchorView = FindView(profile!, anchor);
            if (view is null || anchorView is null)
            {
                error = view is null ? "tool window view not found" : "anchor view not found";
                return false;
            }

            if (IsInAutoHide(anchorView))
            {
                error = "the anchor window is auto-hidden";
                return false;
            }

            // Below the anchor's tab group (Solution Explorer is usually tabbed with Git Changes...), not inside it.
            var parent = Parent(anchorView);
            var target = parent is not null && parent.GetType().FullName == Namespace + "TabGroup" ? parent : anchorView;
            return Invoke(asm!, "Dock", new[] { target, view, Direction(asm!, "Bottom") }, out error);
        }

        /// <summary>Whether the tool window sits in an auto-hide channel (a flyout).</summary>
        public static bool IsAutoHidden(Guid toolWindow)
        {
            return TryGetModel(out _, out var profile, out _) && FindView(profile!, toolWindow) is { } view && IsInAutoHide(view);
        }

        private static bool TryGetModel(out Assembly? asm, out object? profile, out string? error)
        {
            asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Microsoft.VisualStudio.Shell.ViewManager");
            profile = null;
            error = null;
            var manager = asm?.GetType(Namespace + "ViewManager");
            var instance = manager?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            profile = instance is null ? null : manager!.GetProperty("WindowProfile", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
            if (profile is null)
            {
                error = "Visual Studio's window manager (ViewManager) is not available";
                return false;
            }

            return true;
        }

        private static object? FindView(object profile, Guid toolWindow) =>
            Descendants(profile).FirstOrDefault(e =>
                e.GetType().FullName is string name && (name == Namespace + "View" || IsSubclassOf(e.GetType(), Namespace + "View")) &&
                e.GetType().GetProperty("Name")?.GetValue(e) is string viewName &&
                IsToolWindowView(viewName, toolWindow));

        /// <summary>
        /// Whether a view name is the first instance of a tool window. Visual Studio 2026 names them
        /// <c>ST:&lt;instance&gt;:0:{guid}</c> (found live: <c>ST:0:0:{b1e99781-...}</c> is the Toolbox, the Output panes are
        /// <c>ST:1:0:...</c>, <c>ST:2:0:...</c>); a bare GUID is accepted too.
        /// </summary>
        public static bool IsToolWindowView(string viewName, Guid toolWindow)
        {
            var parts = viewName.Split(':');
            if (!Guid.TryParse(parts[parts.Length - 1], out var id) || id != toolWindow)
            {
                return false;
            }

            return parts.Length == 1 || (parts.Length >= 2 && parts[1] == "0");
        }

        /// <summary>The views of the profile (type and name, at most 40), for the log when a tool window cannot be found.</summary>
        private static string DescribeViews(object profile) =>
            string.Join(", ", Descendants(profile)
                .Where(e => e.GetType().FullName == Namespace + "View" || IsSubclassOf(e.GetType(), Namespace + "View"))
                .Take(40)
                .Select(e => e.GetType().Name + ":" + e.GetType().GetProperty("Name")?.GetValue(e)));

        private static bool IsSubclassOf(Type type, string fullName)
        {
            for (var t = type.BaseType; t is not null; t = t.BaseType)
            {
                if (t.FullName == fullName)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsInAutoHide(object element)
        {
            for (var e = Parent(element); e is not null; e = Parent(e))
            {
                // AutoHideGroup / AutoHideChannel: a flyout. Not AutoHideRoot, the ancestor of every docked window too (found live).
                var name = e.GetType().Name;
                if (name == "AutoHideGroup" || name == "AutoHideChannel")
                {
                    return true;
                }
            }

            return false;
        }

        private static object? Parent(object element) => element.GetType().GetProperty("Parent")?.GetValue(element);

        private static System.Collections.Generic.IEnumerable<object> Descendants(object root)
        {
            var stack = new System.Collections.Generic.Stack<object>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (!ReferenceEquals(current, root))
                {
                    yield return current;
                }

                if (current.GetType().GetProperty("Children")?.GetValue(current) is IEnumerable children)
                {
                    foreach (var child in children)
                    {
                        if (child is not null)
                        {
                            stack.Push(child);
                        }
                    }
                }
            }
        }

        private static object Direction(Assembly asm, string name) =>
            Enum.Parse(asm.GetType(Namespace + "DockDirection", throwOnError: true)!, name);

        private static bool Invoke(Assembly asm, string method, object[] args, out string? error)
        {
            error = null;
            var operations = asm.GetType(Namespace + "DockOperations");
            var candidates = operations?.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == method && m.GetParameters().Length == args.Length) ?? Enumerable.Empty<MethodInfo>();
            var target = candidates.FirstOrDefault(m => m.GetParameters().Select((p, i) => p.ParameterType.IsInstanceOfType(args[i])).All(ok => ok));
            if (target is null)
            {
                error = "DockOperations." + method + " not found";
                return false;
            }

            try
            {
                target.Invoke(null, args);
                return true;
            }
            catch (TargetInvocationException ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }
    }
}

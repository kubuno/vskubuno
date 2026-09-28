using System;
using System.ComponentModel.Design;
using Kubuno.VisualStudio.Designer.ToolWindows;
using Kubuno.VisualStudio.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.VisualStudio.DesignerIntegration
{
    /// <summary>
    /// "Tools &gt; Kubuno View Outline" (<c>KubunoCommands.vsct</c>): shows the package-registered
    /// <see cref="OutlineToolWindow"/> that Kubuno.VisualStudio.Designer ships but does not itself
    /// register (the former "Kubuno Toolbox"/"Kubuno Properties" fallbacks were removed - the designer
    /// uses Visual Studio's own Toolbox and Properties window, docs/DESIGNER.md §11) (it must not reference this VSIX assembly - see
    /// that library's own csproj top comment). Mirrors
    /// <see cref="Kubuno.VisualStudio.Debugging.DebugRustTestAtCursorCommand"/>'s own shape for wiring a
    /// command into <c>KubunoPackage.InitializeAsync</c>.
    /// </summary>
    internal static class DesignerToolWindowCommands
    {
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            // Always called from KubunoPackage.InitializeAsync after its own SwitchToMainThreadAsync -
            // asserted explicitly since Add's own lambda (suppressed at its declaration site below) is
            // otherwise inferred as UI-thread-affinitized by the analyzer, which then flags this caller too.
            ThreadHelper.ThrowIfNotOnUIThread();

            Add(commandService, package, PackageIds.ShowKubunoOutlineCommand, typeof(OutlineToolWindow));
        }

        private static void Add(OleMenuCommandService commandService, AsyncPackage package, int commandId, System.Type toolWindowType)
        {
            var id = new CommandID(PackageGuids.KubunoCommandSet, commandId);
#pragma warning disable VSTHRD010 // the command's Execute handler always fires on the UI thread (ShowToolWindow itself asserts this); the analyzer can't see that from the event subscription site - same precedent as DebugRustTestAtCursorCommand.Initialize's own BeforeQueryStatus subscription.
            var command = new OleMenuCommand((_, _) => ShowToolWindow(package, toolWindowType), id);
#pragma warning restore VSTHRD010
            commandService.AddCommand(command);
        }

        private static void ShowToolWindow(AsyncPackage package, System.Type toolWindowType)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Best-effort, like every other entry point in this VSIX (RustLaunchTargetsGenerator,
            // the MCP bridge start, the design surface start): a tool window
            // that fails to construct (e.g. FindToolWindow instantiating it) must not escape as an
            // unhandled exception - that is what surfaces to the developer as VS's generic "this
            // might be caused by an extension" info bar, which names no component and points at an
            // ActivityLog.xml that a normal (non-/log) launch never even writes. Logging here at
            // least says which of our own tool windows misbehaved.
            try
            {
                var window = package.FindToolWindow(toolWindowType, 0, true);
                if (window?.Frame is not IVsWindowFrame frame)
                {
                    KubunoLog.WriteLine($"Kubuno: could not create the '{toolWindowType.Name}' tool window.");
                    return;
                }

                ErrorHandler.ThrowOnFailure(frame.Show());
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException($"Kubuno: failed to show the '{toolWindowType.Name}' tool window", exception);
            }
        }
    }
}

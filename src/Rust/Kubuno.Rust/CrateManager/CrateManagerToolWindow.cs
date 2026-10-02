using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Kubuno.Rust.Commands;
using Kubuno.Rust.Logic.SolutionExplorer;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.Rust.CrateManager
{
    /// <summary>
    /// The crate manager of one <c>.rsproj</c> - the counterpart of NuGet's "Manage NuGet Packages"
    /// document window (Browse / Installed / Updates), opened from the Dependencies node, a dependency,
    /// or "Add > Cargo dependency (crate)...". One instance per project (multi-instance tool window
    /// docked in the document well), reused when opened again.
    /// </summary>
    [Guid(PackageGuidStrings.CrateManagerToolWindow)]
    public sealed class CrateManagerToolWindow : ToolWindowPane
    {
        private static readonly Dictionary<string, int> InstanceIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly CrateManagerControl _control = new CrateManagerControl();

        public CrateManagerToolWindow()
            : base(null)
        {
            Caption = DependenciesText.ManageCratesCommand;
            Content = _control;
        }

        /// <summary>Opens (or brings back) the crate manager of <paramref name="context"/>'s project, selecting <paramref name="crateName"/> if given.</summary>
        internal static async Task ShowAsync(RsprojProjectContext context, string? crateName)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var package = Kubuno.Shared.KubunoHost.Package;
            if (package is null)
            {
                return;
            }

            if (!InstanceIds.TryGetValue(context.ManifestPath, out var id))
            {
                id = InstanceIds.Count + 1;
                InstanceIds[context.ManifestPath] = id;
            }

            var window = await package.FindToolWindowAsync(typeof(CrateManagerToolWindow), id, create: true, package.DisposalToken) as CrateManagerToolWindow;
            if (window?.Frame is not IVsWindowFrame frame)
            {
                return;
            }

            window.Caption = DependenciesText.CrateManagerCaption(context.Project.Name);
            window._control.Initialize(context, crateName);
            ErrorHandler.ThrowOnFailure(frame.Show());
        }
    }
}

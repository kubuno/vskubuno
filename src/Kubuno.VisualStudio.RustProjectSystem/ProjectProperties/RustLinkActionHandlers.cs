using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.ProjectSystem.VS.PropertyPages.Designer;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.VisualStudio.RustProjectSystem.ProjectProperties
{
    /// <summary>
    /// The <c>LinkAction</c> commands of Kubuno.Rust.Sdk's pages (docs/RSPROJ.md, "Project properties like .NET"):
    /// the Project Properties editor calls the <see cref="ILinkActionHandler"/> exported with the link's
    /// <c>Command</c> as <c>CommandName</c> metadata (<c>LinkActionRegistry</c> in the installed
    /// <c>Microsoft.VisualStudio.ProjectSystem.VS.Implementation.dll</c>). The handlers are global exports, so
    /// each checks that the project is a <c>.rsproj</c>.
    /// </summary>
    internal static class RustLinkActions
    {
        public static IVsHierarchy? GetHierarchy(UnconfiguredProject project) =>
            project.Capabilities.AppliesTo(RustProjectCapabilities.RustProjectSystem) ? project.Services.HostObject as IVsHierarchy : null;
    }

    /// <summary>Application page, "Install other targets...": <see cref="RustTargetsDialog"/>.</summary>
    [Export(typeof(ILinkActionHandler))]
    [ExportMetadata("CommandName", "KubunoInstallRustTargets")]
    internal sealed class InstallRustTargetsLinkAction : ILinkActionHandler
    {
        public async Task HandleAsync(UnconfiguredProject project, IReadOnlyDictionary<string, string> editorMetadata)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            new RustTargetsDialog().ShowModal();
        }
    }

    /// <summary>Application page, Dependencies: the crate manager.</summary>
    [Export(typeof(ILinkActionHandler))]
    [ExportMetadata("CommandName", "KubunoOpenCrateManager")]
    internal sealed class OpenCrateManagerLinkAction : ILinkActionHandler
    {
        public async Task HandleAsync(UnconfiguredProject project, IReadOnlyDictionary<string, string> editorMetadata)
        {
            IVsHierarchy? hierarchy = RustLinkActions.GetHierarchy(project);
            if (hierarchy is null)
            {
                return;
            }
            await RustProjectPropertiesHost.EnsurePackageLoadedAsync();
            if (RustProjectPropertiesHost.OpenCrateManagerAsync is { } open)
            {
                await open(hierarchy);
            }
        }
    }

    /// <summary>Application page, Dependencies: the Reference Manager.</summary>
    [Export(typeof(ILinkActionHandler))]
    [ExportMetadata("CommandName", "KubunoOpenReferenceManager")]
    internal sealed class OpenReferenceManagerLinkAction : ILinkActionHandler
    {
        public async Task HandleAsync(UnconfiguredProject project, IReadOnlyDictionary<string, string> editorMetadata)
        {
            IVsHierarchy? hierarchy = RustLinkActions.GetHierarchy(project);
            if (hierarchy is null)
            {
                return;
            }
            await RustProjectPropertiesHost.EnsurePackageLoadedAsync();
            if (RustProjectPropertiesHost.OpenReferenceManagerAsync is { } open)
            {
                await open(hierarchy);
            }
        }
    }

    /// <summary>Debug page, "Open debug launch profile UI": <see cref="RustLaunchProfileDialog"/> over the RustDebugger* user-file properties.</summary>
    [Export(typeof(ILinkActionHandler))]
    [ExportMetadata("CommandName", "KubunoOpenRustLaunchProfile")]
    internal sealed class OpenRustLaunchProfileLinkAction : ILinkActionHandler
    {
        public async Task HandleAsync(UnconfiguredProject project, IReadOnlyDictionary<string, string> editorMetadata)
        {
            if (RustLinkActions.GetHierarchy(project) is null)
            {
                return;
            }

            ConfiguredProject? configured = await project.GetSuggestedConfiguredProjectAsync();
            if (configured?.Services.UserPropertiesProvider is not { } userProvider || configured.Services.ProjectPropertiesProvider is not { } projectProvider)
            {
                return;
            }

            IProjectProperties user = userProvider.GetCommonProperties();
            IProjectProperties msbuild = projectProvider.GetCommonProperties();
            var profile = new RustLaunchProfile
            {
                Executable = await msbuild.GetEvaluatedPropertyValueAsync("TargetPath"),
                Arguments = await user.GetUnevaluatedPropertyValueAsync("RustDebuggerCommandArguments") ?? string.Empty,
                WorkingDirectory = await user.GetUnevaluatedPropertyValueAsync("RustDebuggerWorkingDirectory") ?? string.Empty,
                Environment = await user.GetUnevaluatedPropertyValueAsync("RustDebuggerEnvironment") ?? string.Empty,
                Backtrace = string.Equals(await user.GetEvaluatedPropertyValueAsync("RustDebuggerBacktrace"), "true", StringComparison.OrdinalIgnoreCase),
            };

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dialog = new RustLaunchProfileDialog(profile);
            if (dialog.ShowModal() != true)
            {
                return;
            }
            RustLaunchProfile result = dialog.Result;

            await SetOrDeleteAsync(user, "RustDebuggerCommandArguments", result.Arguments);
            await SetOrDeleteAsync(user, "RustDebuggerWorkingDirectory", result.WorkingDirectory);
            await SetOrDeleteAsync(user, "RustDebuggerEnvironment", result.Environment.Replace("\r\n", "\n").Trim('\n').Replace("\n", "\r\n"));
            await user.SetPropertyValueAsync("RustDebuggerBacktrace", result.Backtrace ? "true" : "false");
        }

        private static Task SetOrDeleteAsync(IProjectProperties properties, string name, string value) =>
            string.IsNullOrWhiteSpace(value) ? properties.DeletePropertyAsync(name) : properties.SetPropertyValueAsync(name, value);
    }

    /// <summary>Resources page, "Create or open application resources": opens the project's .kbres file, or explains that none exists yet.</summary>
    [Export(typeof(ILinkActionHandler))]
    [ExportMetadata("CommandName", "KubunoCreateOrOpenKbres")]
    internal sealed class CreateOrOpenKbresLinkAction : ILinkActionHandler
    {
        public async Task HandleAsync(UnconfiguredProject project, IReadOnlyDictionary<string, string> editorMetadata)
        {
            if (RustLinkActions.GetHierarchy(project) is null)
            {
                return;
            }

            string directory = Path.GetDirectoryName(project.FullPath) ?? string.Empty;
            string? existing = await Task.Run(() => Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.kbres", SearchOption.AllDirectories)
                    .FirstOrDefault(f => f.IndexOf(Path.DirectorySeparatorChar + "target" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0)
                : null);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (existing != null)
            {
                VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, existing);
                return;
            }
            VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, PropertiesText.KbresNotAvailable, "Kubuno",
                OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}

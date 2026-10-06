using System.IO;
using Kubuno.Rust.Cargo.Naming;

namespace Kubuno.Rust.Logic.ProjectGeneration
{
    /// <summary>
    /// The name of a NEW solution created by "Generate Visual Studio Projects" (an existing one is always reused). A Kubuno
    /// solution is its repository (<see cref="ProjectNaming"/>): <c>Kubuno.Core.slnx</c> for the core repository
    /// (<c>server/</c> or <c>crates/kubuno-core</c>, <see cref="CoreRepository"/>; also a workspace inside it: <c>server/</c>,
    /// <c>common/</c>, <c>desktop/</c>), <c>Kubuno.Desktop.slnx</c> for the desktop repository (its <c>windows</c> workspace,
    /// <c>src/crates/kubuno-desktop-ui</c>; the solution itself lives at the repository root), <c>Kubuno.&lt;Module&gt;.slnx</c>
    /// for a module (<c>module.toml</c>); any other workspace is named after its folder.
    /// </summary>
    public static class SolutionNaming
    {
        public static string DefaultSolutionFileName(string workspaceRoot)
        {
            if (CoreRepository.IsCore(workspaceRoot) || CoreRepository.IsCore(Path.GetDirectoryName(workspaceRoot)))
            {
                return ProjectNaming.CoreSolutionFileName;
            }

            if (ProjectNaming.IsDesktopWindowsWorkspace(workspaceRoot) || ProjectNaming.IsDesktopRepository(workspaceRoot))
            {
                return ProjectNaming.DesktopSolutionFileName;
            }

            var moduleToml = Path.Combine(workspaceRoot, "module.toml");
            if (File.Exists(moduleToml) && ProjectNaming.ModuleId(File.ReadAllText(moduleToml)) is { } id)
            {
                return ProjectNaming.ModuleSolutionFileName(id);
            }

            return new DirectoryInfo(workspaceRoot).Name + ".slnx";
        }
    }
}

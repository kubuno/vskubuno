using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.VisualStudio.RustProjectSystem.ProjectProperties
{
    /// <summary>
    /// After the Project Properties editor changed a file MSBuild does not evaluate (Cargo.toml, rustfmt.toml,
    /// src/main.rs), asks CPS to re-evaluate the project: the editor refreshes its values when the project's
    /// query data version changes (<c>ConfiguredProject.ProjectVersion</c>, decompiled
    /// <c>ProjectQueryUtilities.GetQueryDataVersion</c>), and that version only moves with an evaluation. Marking the
    /// MSBuild project dirty under a write lock is the documented way to get one without touching the project file.
    /// </summary>
    internal static class ProjectReevaluation
    {
        public static async Task RequestAsync(ConfiguredProject configuredProject)
        {
            await TaskScheduler.Default;
            IProjectLockService? lockService = configuredProject.UnconfiguredProject.ProjectService.Services.ProjectLockService;
            if (lockService is null)
            {
                return;
            }

            await lockService.WriteLockAsync(async access =>
            {
                Microsoft.Build.Evaluation.Project project = await access.GetProjectAsync(configuredProject);
                project.MarkDirty();
                await access.ReleaseAsync();
            });
        }
    }
}

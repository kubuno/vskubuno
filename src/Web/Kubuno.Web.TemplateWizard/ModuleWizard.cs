using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EnvDTE;
using Kubuno.Web.Logic.Generation;
using Microsoft.VisualStudio.TemplateWizard;

namespace Kubuno.Web.TemplateWizard
{
    /// <summary>
    /// The wizard of the "Kubuno Core Web Module" template (docs/WEB.md, "The module template"). Before the files are
    /// written: the tokens (<see cref="ModuleTemplateTokens"/>). Once the backend project exists: its frontend
    /// <c>.esproj</c> is added to the solution, the backend is made to depend on it (F5 deploys the built bundle), a
    /// shared <c>.slnLaunch</c> profile starts both, and the solution gets its SDK feed. Best-effort: a template never
    /// fails over these conveniences.
    /// </summary>
    public sealed class ModuleWizard : IWizard
    {
        private DTE? _dte;
        private string? _solutionDirectory;
        private string? _moduleId;

        public void RunStarted(object automationObject, Dictionary<string, string> replacementsDictionary, WizardRunKind runKind, object[] customParams)
        {
            _dte = automationObject as DTE;
            if (replacementsDictionary is null)
            {
                return;
            }

            replacementsDictionary.TryGetValue("$safeprojectname$", out var name);
            replacementsDictionary.TryGetValue("$destinationdirectory$", out var destination);
            if (replacementsDictionary.TryGetValue("$solutiondirectory$", out var solutionDirectory) && !string.IsNullOrWhiteSpace(solutionDirectory))
            {
                _solutionDirectory = solutionDirectory;
            }

            var tokens = ModuleTemplateTokens.Build(name, ModuleTemplateTokens.FindCoreRepository(destination ?? _solutionDirectory));
            foreach (var pair in tokens)
            {
                replacementsDictionary[pair.Key] = pair.Value;
            }

            _moduleId = tokens["$moduleid$"];
        }

        public void ProjectFinishedGenerating(Project project)
        {
            try
            {
                if (_dte is null || project is null || _moduleId is null)
                {
                    return;
                }

                var projectDirectory = Path.GetDirectoryName(project.FullName)!;
                var esproj = Path.Combine(projectDirectory, "frontend", _moduleId + "-frontend.esproj");
                if (!File.Exists(esproj))
                {
                    return;
                }

                var frontend = _dte.Solution.AddFromFile(esproj, false);
                // The backend depends on the frontend: building the backend (F5) builds the bundle it deploys first.
                var dependency = _dte.Solution.SolutionBuild.BuildDependencies.Item(project.UniqueName);
                dependency?.AddProject(frontend.UniqueName);

                var solution = _dte.Solution.FullName;
                if (!string.IsNullOrEmpty(solution))
                {
                    WriteLaunchProfile(Path.ChangeExtension(solution, ".slnLaunch"), Path.GetDirectoryName(solution)!, project.FullName, esproj);
                }
            }
            catch (Exception)
            {
                // The frontend can still be added by hand (Add > Existing Project); never fail the template.
            }
        }

        public void RunFinished()
        {
            try
            {
                if (_solutionDirectory is null)
                {
                    return;
                }

                var extensionDirectory = Path.GetDirectoryName(typeof(ModuleWizard).Assembly.Location);
                Kubuno.Rust.Logic.ProjectGeneration.SdkFeedDistribution.EnsureUserRegistered(extensionDirectory, _ => { });
                Kubuno.Rust.Logic.ProjectGeneration.SdkFeedDistribution.EnsureSolutionLocal(_solutionDirectory, extensionDirectory, _ => { });
            }
            catch (Exception)
            {
                // Best-effort, see the class remarks.
            }
        }

        public void ProjectItemFinishedGenerating(ProjectItem projectItem)
        {
        }

        public bool ShouldAddProjectItem(string filePath) => true;

        public void BeforeOpeningFile(ProjectItem projectItem)
        {
        }

        /// <summary>A shared launch profile "&lt;id&gt; (Kubuno Core Web + navigateur)" (Visual Studio's own .slnLaunch format), unless one exists.</summary>
        private void WriteLaunchProfile(string path, string solutionDirectory, string backend, string frontend)
        {
            if (File.Exists(path))
            {
                return;
            }

            string Relative(string file) => file.StartsWith(solutionDirectory, StringComparison.OrdinalIgnoreCase)
                ? file.Substring(solutionDirectory.Length).TrimStart('\\').Replace("\\", "\\\\")
                : file.Replace("\\", "\\\\");

            var json = new StringBuilder()
                .Append("[\n  {\n    \"Name\": \"").Append(_moduleId).Append(" (Kubuno Core Web + navigateur)\",\n    \"Projects\": [\n")
                .Append("      {\n        \"Path\": \"").Append(Relative(backend)).Append("\",\n        \"Action\": \"Start\"\n      },\n")
                .Append("      {\n        \"Path\": \"").Append(Relative(frontend)).Append("\",\n        \"Action\": \"Start\"\n      }\n")
                .Append("    ]\n  }\n]\n");
            File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
        }
    }
}

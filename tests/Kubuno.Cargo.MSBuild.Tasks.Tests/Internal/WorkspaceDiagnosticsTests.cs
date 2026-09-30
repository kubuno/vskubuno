using System.Linq;
using Kubuno.Cargo.MSBuild.Tasks.Internal;
using Kubuno.Cargo.MSBuild.Tasks.Tests.Fakes;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Kubuno.Cargo.MSBuild.Tasks.Tests.Internal
{
    /// <summary>
    /// A workspace-wide build (docs/RSPROJ.md, "Cargo workspaces") hands the same diagnostics to every project of the
    /// workspace: each must reach the Error List once, under the project whose folder holds its file.
    /// </summary>
    public class WorkspaceDiagnosticsTests
    {
        private const string Root = @"Z:\ws";
        private const string Shell = @"Z:\ws\src\shell\shell.rsproj";
        private const string Chat = @"Z:\ws\src\chat\chat.rsproj";
        private const string Ui = @"Z:\ws\src\crates\ui\ui.rsproj";

        private static readonly string Solution =
            "<SolutionConfiguration>" +
            $"<ProjectConfiguration Project=\"{{1}}\" AbsolutePath=\"{Shell}\" BuildProjectInSolution=\"True\">Debug|x64</ProjectConfiguration>" +
            $"<ProjectConfiguration Project=\"{{2}}\" AbsolutePath=\"{Chat}\" BuildProjectInSolution=\"True\">Debug|x64</ProjectConfiguration>" +
            $"<ProjectConfiguration Project=\"{{3}}\" AbsolutePath=\"{Ui}\" BuildProjectInSolution=\"True\">Debug|x64</ProjectConfiguration>" +
            "<ProjectConfiguration Project=\"{4}\" AbsolutePath=\"Z:\\host\\host.csproj\" BuildProjectInSolution=\"True\">Debug|x64</ProjectConfiguration>" +
            "</SolutionConfiguration>";

        [Fact]
        public void The_solution_projects_come_from_the_solution_configuration()
        {
            var projects = WorkspaceDiagnosticOwnership.SolutionProjects(Solution, ".rsproj");

            Assert.Equal(new[] { Shell, Chat, Ui }, projects);
            Assert.Empty(WorkspaceDiagnosticOwnership.SolutionProjects(null, ".rsproj"));
            Assert.Empty(WorkspaceDiagnosticOwnership.SolutionProjects("not xml <", ".rsproj"));
        }

        [Theory]
        [InlineData(@"Z:\ws\src\shell\src\header.rs", Shell)]
        [InlineData(@"Z:\ws\src\crates\ui\src\lib.rs", Ui)]
        // No project holds it (a crate without a project, cargo's own output): the first project of the workspace by path.
        [InlineData(@"Z:\ws\src\crates\print\src\lib.rs", Chat)]
        [InlineData(@"Z:\common\sync\src\lib.rs", Chat)]
        [InlineData("", Chat)]
        public void Each_diagnostic_has_exactly_one_owner(string file, string owner)
        {
            var projects = WorkspaceDiagnosticOwnership.SolutionProjects(Solution, ".rsproj");

            var owners = new[] { Shell, Chat, Ui }.Where(project => WorkspaceDiagnosticOwnership.IsOwnedBy(file, project, projects, Root)).ToList();

            Assert.Equal(new[] { owner }, owners);
        }

        [Fact]
        public void A_project_built_alone_reports_everything()
        {
            Assert.True(WorkspaceDiagnosticOwnership.IsOwnedBy(@"Z:\ws\src\crates\ui\src\lib.rs", Shell, System.Array.Empty<string>(), Root));
        }

        private static TaskItem Diagnostic(string severity, string file, string message)
        {
            var item = new TaskItem("d");
            item.SetMetadata("Severity", severity);
            item.SetMetadata("Code", "E0063");
            item.SetMetadata("File", file);
            item.SetMetadata("Line", "175");
            item.SetMetadata("Column", "5");
            item.SetMetadata("Message", message);
            return item;
        }

        private static TaskItem Executable(string path, string bin)
        {
            var item = new TaskItem(path);
            item.SetMetadata("BinName", bin);
            return item;
        }

        private static (KubunoWorkspaceDiagnostics Task, RecordingBuildEngine Engine) NewTask(string project, string? bin, bool succeeded, params ITaskItem[] diagnostics)
        {
            var engine = new RecordingBuildEngine();
            var task = new KubunoWorkspaceDiagnostics
            {
                BuildEngine = engine,
                ProjectFullPath = project,
                WorkspaceRoot = Root,
                SolutionConfigurationContents = Solution,
                BinName = bin,
                BuildSucceeded = succeeded,
                Diagnostics = diagnostics,
                Executables = new ITaskItem[] { Executable(@"C:\t\debug\chat.exe", "chat") },
            };
            return (task, engine);
        }

        [Fact]
        public void The_owner_logs_the_error_with_its_location_and_fails()
        {
            var (task, engine) = NewTask(Shell, "shell", succeeded: false, Diagnostic("Error", @"Z:\ws\src\shell\src\header.rs", "missing field `drag`"));

            Assert.False(task.Execute());
            var error = Assert.Single(engine.Errors);
            Assert.Equal(@"Z:\ws\src\shell\src\header.rs", error.File);
            Assert.Equal(175, error.LineNumber);
            Assert.Equal(5, error.ColumnNumber);
            Assert.Equal("E0063", error.Code);
        }

        [Fact]
        public void Another_project_whose_executable_was_built_succeeds_and_picks_it()
        {
            var (task, engine) = NewTask(Chat, "chat", succeeded: false, Diagnostic("Error", @"Z:\ws\src\shell\src\header.rs", "missing field `drag`"));

            Assert.True(task.Execute());
            Assert.Empty(engine.Errors);
            Assert.Equal(@"C:\t\debug\chat.exe", task.ExecutablePath);
        }

        [Fact]
        public void A_project_whose_executable_was_not_built_says_why()
        {
            var (task, engine) = NewTask(Ui, "ui-tool", succeeded: false, Diagnostic("Error", @"Z:\ws\src\shell\src\header.rs", "missing field `drag`"));

            Assert.False(task.Execute());
            Assert.Contains("was not built", Assert.Single(engine.Errors).Message);
        }

        [Fact]
        public void A_library_without_errors_of_its_own_succeeds_and_warnings_stay_warnings()
        {
            var (task, engine) = NewTask(Ui, bin: null, succeeded: false,
                Diagnostic("Error", @"Z:\ws\src\shell\src\header.rs", "missing field `drag`"),
                Diagnostic("Warning", @"Z:\ws\src\crates\ui\src\lib.rs", "unused variable"));

            Assert.True(task.Execute());
            Assert.Empty(engine.Errors);
            Assert.Equal("unused variable", Assert.Single(engine.Warnings).Message);
        }
    }
}

using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kubuno.Shared;
using Kubuno.Shared.Logging;
using Kubuno.Web.Logic.Generation;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Web.Commands
{
    /// <summary>
    /// Tools > "Kubuno: Package Module (.kbpkg)" (docs/WEB.md, ".kbpkg"): a Release build of the module and its
    /// frontend, then <c>dist\&lt;id&gt;-&lt;version&gt;-windows-x86_64.kbpkg</c> - Kubuno.Web.Sdk's
    /// <c>KubunoPackModule</c> target, run by Visual Studio's own MSBuild so the command line gives the same package.
    /// </summary>
    internal static class PackModuleCommand
    {
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
#pragma warning disable VSTHRD010 // menu command handlers are always invoked on the UI thread.
            commandService.AddCommand(new OleMenuCommand((_, _) => Run(package), new CommandID(KubunoGuids.CommandSet, PackageIds.PackModuleCommand)));
#pragma warning restore VSTHRD010
        }

        private static void Run(AsyncPackage package)
        {
            _ = package.JoinableTaskFactory.RunAsync(async () =>
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync();
                var repository = WebUi.ResolveRepository(package, "Choose the Kubuno module repository");
                if (repository is null)
                {
                    return;
                }

                var project = repository.Kind == WebRepositoryKind.Module && repository.RunPackage is not null
                    ? repository.Members.Where(member => member.PackageName == repository.RunPackage)
                        .SelectMany(member => new[]
                        {
                            Path.Combine(member.Directory, WebSolutionGenerator.RsprojName(repository, member) + ".rsproj"),
                            // A project generated before the 2026-10 naming (named after the package).
                            Path.Combine(member.Directory, member.PackageName + ".rsproj"),
                        })
                        .FirstOrDefault(File.Exists)
                    : null;
                if (project is null)
                {
                    WebUi.ShowMessage(package, repository.Kind == WebRepositoryKind.Module
                        ? "The module has no generated .rsproj yet: run \"Kubuno Web: Generate Solution\" first."
                        : "\"Package Module\" packages a module; '" + repository.Name + "' is the core.", error: true);
                    return;
                }

                var msbuild = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!, @"..\..\MSBuild\Current\Bin\MSBuild.exe");
                KubunoLog.Activate();
                KubunoLog.WriteLine("Kubuno web: packaging " + repository.Id + " (Release)...");
                await TaskScheduler.Default;
                var exit = await RunAsync(Path.GetFullPath(msbuild), "\"" + project + "\" -restore -t:KubunoPackModule -p:Configuration=Release -v:m -nologo", repository.Root).ConfigureAwait(false);
                KubunoLog.WriteLine(exit == 0 ? "Kubuno web: package written under " + Path.Combine(repository.Root, "dist") + "." : "Kubuno web: packaging failed (MSBuild exit code " + exit + ").");
            });
        }

        private static Task<int> RunAsync(string fileName, string arguments, string workingDirectory)
        {
            var completion = new TaskCompletionSource<int>();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true,
            };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) KubunoLog.WriteLine("  " + e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) KubunoLog.WriteLine("  " + e.Data); };
            process.Exited += (_, _) => { process.WaitForExit(); completion.TrySetResult(process.ExitCode); process.Dispose(); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return completion.Task;
        }
    }
}

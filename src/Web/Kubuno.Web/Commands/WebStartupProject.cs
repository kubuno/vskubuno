using System;
using System.IO;
using Kubuno.Shared.Logging;
using Kubuno.Web.Logic.Generation;
using Microsoft.VisualStudio.Shell;
using SolutionEvents = Microsoft.VisualStudio.Shell.Events.SolutionEvents;

namespace Kubuno.Web.Commands
{
    /// <summary>
    /// Gives a Kubuno web solution opened for the first time on this machine (no <c>.suo</c> yet) the startup project F5
    /// is for - <c>kubuno-core</c> or the module's backend - instead of the one Visual Studio picks by itself (found live:
    /// the frontend <c>.esproj</c> of a module, whose F5 starts no core). See <see cref="StartupProjectPolicy"/>. Every
    /// opening also mirrors the frontends' committed <c>.kubuno\launch.json</c> to <c>.vscode\launch.json</c>, the only one
    /// Visual Studio's script debugger reads (<see cref="LaunchJsonMirror"/>).
    /// </summary>
    internal static class WebStartupProject
    {
        private static string? _openingSolution;
        private static bool _hadUserOptions = true;

        public static void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SolutionEvents.OnBeforeOpenSolution += OnBeforeOpenSolution;
            SolutionEvents.OnAfterBackgroundSolutionLoadComplete += OnAfterLoadComplete;
            if (CurrentSolution() is not null)
            {
                // The package loaded after the solution: decide now (a later load-complete event decides again, identically).
                OnAfterLoadComplete(null!, EventArgs.Empty);
            }
        }

        private static void OnBeforeOpenSolution(object sender, Microsoft.VisualStudio.Shell.Events.BeforeOpenSolutionEventArgs args)
        {
            _openingSolution = args.SolutionFilename;
            MirrorLaunchJson(_openingSolution);
            _hadUserOptions = string.IsNullOrEmpty(_openingSolution) || HasUserOptions(_openingSolution);
        }

        private static void OnAfterLoadComplete(object sender, EventArgs args)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var solution = _openingSolution;
            var hadUserOptions = _hadUserOptions;
            if (solution is null)
            {
                // The package loaded after the solution started opening (Visual Studio started with a solution): Visual
                // Studio writes the .suo when the solution closes, so its absence still means a first opening.
                solution = CurrentSolution();
                hadUserOptions = solution is null || HasUserOptions(solution);
            }

            MirrorLaunchJson(solution);
            _openingSolution = null;
            _hadUserOptions = true;
            if (solution is null || hadUserOptions)
            {
                return;
            }

            string? startup;
            try
            {
                startup = StartupProjectPolicy.ForFreshSolution(solution, hadUserOptions);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is FormatException)
            {
                KubunoLog.WriteLine("Kubuno web: could not read " + solution + " to choose its startup project (" + exception.Message + ").");
                return;
            }

            if (startup is null || Package.GetGlobalService(typeof(EnvDTE.DTE)) is not EnvDTE.DTE dte)
            {
                return;
            }

            try
            {
                // An array of relative paths: a plain string is refused (E_INVALIDARG) for a CPS project.
                dte.Solution.SolutionBuild.StartupProjects = new object[] { startup };
                KubunoLog.WriteLine("Kubuno web: first opening of " + Path.GetFileName(solution) + " on this machine - startup project " + startup + ".");
            }
            catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException || exception is ArgumentException)
            {
                KubunoLog.WriteLine("Kubuno web: could not make " + startup + " the startup project (" + exception.Message + "); set it from Solution Explorer.");
            }
        }

        /// <summary>Writes the frontends' <c>.vscode\launch.json</c> from their committed <c>.kubuno\launch.json</c> (<see cref="LaunchJsonMirror"/>). Best effort.</summary>
        private static void MirrorLaunchJson(string? solution)
        {
            if (string.IsNullOrEmpty(solution))
            {
                return;
            }

            try
            {
                foreach (var written in LaunchJsonMirror.EnsureForSolution(solution!))
                {
                    KubunoLog.WriteLine("Kubuno web: " + written + " written from the project's LaunchJsonFolder (Visual Studio's script debugger only reads .vscode\\launch.json).");
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                KubunoLog.WriteLine("Kubuno web: could not write the frontends' .vscode\\launch.json (" +exception.Message + ").");
            }
        }

        private static bool HasUserOptions(string solution)
        {
            try
            {
                return File.Exists(StartupProjectPolicy.UserOptionsFile(solution));
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is NotSupportedException)
            {
                return true;
            }
        }

        private static string? CurrentSolution()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return Package.GetGlobalService(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte && dte.Solution is { IsOpen: true } open && !string.IsNullOrEmpty(open.FullName)
                    ? open.FullName
                    : null;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return null;
            }
        }
    }
}

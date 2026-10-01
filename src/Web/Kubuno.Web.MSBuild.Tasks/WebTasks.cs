using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Kubuno.Web.Logic.DevCore;
using Kubuno.Web.Logic.DevDatabase;
using Kubuno.Web.Logic.Modules;
using Kubuno.Web.Logic.Node;
using Microsoft.Build.Framework;
using MSBuildTask = Microsoft.Build.Utilities.Task;

namespace Kubuno.Web.MSBuild.Tasks
{
    /// <summary>
    /// Prepares the node environment of a frontend (docs/WEB.md, "Frontends") and writes
    /// <c>obj\kubuno-npm.cmd</c>, through which the build, F5 and Test Explorer run npm: Node.js from Visual Studio
    /// when none is on PATH, and - when node_modules was installed by another operating system - the overlay of
    /// Windows native packages (NODE_PATH) and the <c>.cmd</c> shims of the <c>.bin</c> commands. Never runs
    /// <c>npm install</c> in the project itself.
    /// </summary>
    public sealed class KubunoPrepareNode : MSBuildTask
    {
        [Required]
        public string PackageJsonDirectory { get; set; } = string.Empty;

        /// <summary>The folder receiving <c>kubuno-npm.cmd</c> (the project's obj folder).</summary>
        [Required]
        public string IntermediateDirectory { get; set; } = string.Empty;

        /// <summary>Visual Studio's own Node.js (<c>MSBuild\Microsoft\VisualStudio\NodeJs</c>), used when PATH has none.</summary>
        public string? FallbackNodeDirectory { get; set; }

        /// <summary>Overlay base folder (default <c>%LOCALAPPDATA%\Kubuno\node-overlay</c>).</summary>
        public string? OverlayBaseDirectory { get; set; }

        [Output]
        public string NpmCommand { get; set; } = string.Empty;

        [Output]
        public bool IsForeign { get; set; }

        [Output]
        public bool NodeModulesExists { get; set; }

        public override bool Execute()
        {
            try
            {
                var nodeDirectory = NodeModulesPlatform.FindOnPath("node.exe", Environment.GetEnvironmentVariable("PATH"));
                string? addedNodeDirectory = null;
                if (nodeDirectory is null)
                {
                    if (string.IsNullOrEmpty(FallbackNodeDirectory) || !File.Exists(Path.Combine(FallbackNodeDirectory!, "node.exe")))
                    {
                        Log.LogError("Node.js was not found on PATH nor in Visual Studio (" + (FallbackNodeDirectory ?? "?") + "). Install Node.js (https://nodejs.org) or Visual Studio's \"Node.js development\" component, then restart Visual Studio.");
                        return false;
                    }

                    addedNodeDirectory = FallbackNodeDirectory!.TrimEnd('\\');
                    nodeDirectory = addedNodeDirectory;
                    Log.LogMessage(MessageImportance.Normal, "Kubuno: Node.js from Visual Studio: " + addedNodeDirectory);
                }

                var state = NodeModulesPlatform.Inspect(PackageJsonDirectory);
                NodeModulesExists = state.Exists;
                IsForeign = state.IsForeign;
                string? shimDirectory = null;
                string? nodePath = null;
                if (state.IsForeign)
                {
                    var overlay = NodeModulesPlatform.OverlayDirectory(string.IsNullOrEmpty(OverlayBaseDirectory) ? NodeModulesPlatform.DefaultOverlayBase() : OverlayBaseDirectory!, state.MissingWindowsPackages);
                    Log.LogMessage(MessageImportance.High, "Kubuno: node_modules was installed by another operating system ("
                        + string.Join(", ", state.MissingWindowsPackages.Select(package => package.ForeignName)) + "); left untouched, Windows packages from " + overlay + ".");
                    Directory.CreateDirectory(overlay);
                    var missing = state.MissingWindowsPackages.Where(package => !NodeModulesPlatform.IsInstalled(overlay, package)).ToList();
                    if (missing.Count > 0)
                    {
                        Log.LogMessage(MessageImportance.High, "Kubuno: installing " + string.Join(", ", missing.Select(package => package.Name + "@" + package.Version)) + " into the overlay");
                        WriteIfChanged(Path.Combine(overlay, "package.json"), NodeModulesPlatform.OverlayPackageJson(state.MissingWindowsPackages));
                        var exit = RunNpm(nodeDirectory, overlay, NodeModulesPlatform.NpmInstallArguments);
                        if (exit != 0)
                        {
                            Log.LogWarning("Kubuno: npm could not install the overlay (exit code " + exit + "); the build says what is really missing.");
                        }

                        foreach (var package in missing.Where(package => !NodeModulesPlatform.IsInstalled(overlay, package)))
                        {
                            // Not every native package has a Windows build (optional dependency skipped by npm).
                            Log.LogMessage(MessageImportance.Normal, "Kubuno: no Windows build of " + package.Name + "@" + package.Version + ".");
                        }
                    }

                    shimDirectory = Path.Combine(overlay, "bin");
                    WriteShims(state.NodeModules, shimDirectory);
                    nodePath = Path.Combine(overlay, "node_modules");
                }

                Directory.CreateDirectory(IntermediateDirectory);
                NpmCommand = Path.Combine(Path.GetFullPath(IntermediateDirectory), "kubuno-npm.cmd");
                var script = new StringBuilder();
                script.Append("@echo off\r\n");
                script.Append("rem Generated by Kubuno.Web.Sdk (docs/WEB.md, \"Frontends\"): npm with this machine's node environment.\r\n");
                var pathParts = new[] { shimDirectory, addedNodeDirectory }.Where(part => !string.IsNullOrEmpty(part)).ToList();
                if (pathParts.Count > 0)
                {
                    script.Append("set \"PATH=").Append(string.Join(";", pathParts)).Append(";%PATH%\"\r\n");
                }

                if (nodePath is not null)
                {
                    script.Append("set \"NODE_PATH=").Append(nodePath).Append("\"\r\n");
                }

                script.Append("npm %*\r\n");
                WriteIfChanged(NpmCommand, script.ToString());
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception)
            {
                Log.LogErrorFromException(exception, showStackTrace: false);
                return false;
            }
        }

        private void WriteShims(string nodeModules, string shimDirectory)
        {
            // The .bin commands only change when npm installs: keyed by its hidden lock file's time stamp.
            var hiddenLock = Path.Combine(nodeModules, ".package-lock.json");
            var stamp = (File.Exists(hiddenLock) ? File.GetLastWriteTimeUtc(hiddenLock).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none") + "|" + nodeModules;
            var stampFile = Path.Combine(shimDirectory, ".kubuno-stamp");
            if (File.Exists(stampFile) && File.ReadAllText(stampFile) == stamp)
            {
                return;
            }

            Directory.CreateDirectory(shimDirectory);
            var commands = NodeModulesPlatform.BinCommands(nodeModules);
            foreach (var command in commands)
            {
                WriteIfChanged(Path.Combine(shimDirectory, command.Key + ".cmd"), NodeModulesPlatform.ShimText(command.Value));
            }

            File.WriteAllText(stampFile, stamp);
            Log.LogMessage(MessageImportance.Normal, "Kubuno: " + commands.Count + " command shims in " + shimDirectory);
        }

        private int RunNpm(string nodeDirectory, string workingDirectory, string arguments)
        {
            var start = new ProcessStartInfo("cmd.exe", "/d /c npm " + arguments)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.EnvironmentVariables["PATH"] = nodeDirectory + ";" + Environment.GetEnvironmentVariable("PATH");
            using var process = Process.Start(start) ?? throw new InvalidOperationException("npm could not be started.");
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) Log.LogMessage(MessageImportance.Low, e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Log.LogMessage(MessageImportance.Normal, e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            return process.ExitCode;
        }

        private static void WriteIfChanged(string path, string content)
        {
            if (!File.Exists(path) || File.ReadAllText(path) != content)
            {
                File.WriteAllText(path, content, new UTF8Encoding(false));
            }
        }
    }

    /// <summary>
    /// Copies a built module into the dev core (the Windows <c>deploy_local.sh</c>, docs/WEB.md "F5 on a module"),
    /// after stopping the copies of it a previous debug session left running there.
    /// </summary>
    public sealed class KubunoDeployModule : MSBuildTask
    {
        [Required]
        public string ModuleDirectory { get; set; } = string.Empty;

        [Required]
        public string Executable { get; set; } = string.Empty;

        /// <summary>The dev core folder (empty: <c>KUBUNO_DEV_CORE_ROOT</c>, else <c>%LOCALAPPDATA%\Kubuno\dev-core</c>).</summary>
        public string? DevCoreRoot { get; set; }

        [Output]
        public string DeployedDirectory { get; set; } = string.Empty;

        public override bool Execute()
        {
            try
            {
                var manifest = ModuleManifest.Load(Path.Combine(ModuleDirectory, "module.toml"));
                var layout = string.IsNullOrEmpty(DevCoreRoot) ? DevCoreLayout.Default() : new DevCoreLayout(DevCoreRoot!);
                layout.EnsureCreated();
                DeployedDirectory = layout.ModuleDirectory(manifest.Id);
                var stopped = ModuleDeployment.StopProcessesUnder(DeployedDirectory, Path.GetFileNameWithoutExtension(manifest.WindowsExecutableName));
                if (stopped.Count > 0)
                {
                    Log.LogMessage(MessageImportance.High, "Kubuno: stopped " + stopped.Count + " running copy(ies) of " + manifest.Id + " in the dev core.");
                }

                var copied = ModuleDeployment.Execute(ModuleDeployment.Plan(ModuleDirectory, Executable, DeployedDirectory, manifest));
                Log.LogMessage(MessageImportance.High, "Kubuno: " + manifest.Id + " deployed to " + DeployedDirectory + " (" + copied + " file(s) updated).");
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ModuleManifestException || exception is Kubuno.Rust.Cargo.Toml.TomlParseException)
            {
                Log.LogErrorFromException(exception, showStackTrace: false);
                return false;
            }
        }
    }

    /// <summary>Packages a built module as <c>dist\&lt;id&gt;-&lt;version&gt;-windows-x86_64.kbpkg</c> (docs/WEB.md, ".kbpkg").</summary>
    public sealed class KubunoPackModule : MSBuildTask
    {
        [Required]
        public string ModuleDirectory { get; set; } = string.Empty;

        [Required]
        public string Executable { get; set; } = string.Empty;

        [Required]
        public string OutputDirectory { get; set; } = string.Empty;

        [Output]
        public string Package { get; set; } = string.Empty;

        public override bool Execute()
        {
            try
            {
                var manifest = ModuleManifest.Load(Path.Combine(ModuleDirectory, "module.toml"));
                Package = KbpkgPackager.Pack(ModuleDirectory, Executable, manifest, OutputDirectory);
                Log.LogMessage(MessageImportance.High, "Kubuno: " + Package);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is ModuleManifestException)
            {
                Log.LogErrorFromException(exception, showStackTrace: false);
                return false;
            }
        }
    }

    /// <summary>
    /// The development database guard from the command line (docs/WEB.md, "The development database"): the same rule
    /// as F5, so <c>msbuild kubuno-core.rsproj -t:KubunoCheckDevDatabase</c> says what F5 would. Never prints the password.
    /// </summary>
    public sealed class KubunoCheckDevDatabase : MSBuildTask
    {
        /// <summary>Overrides the environment variable (the project's per-user debug environment).</summary>
        public string? DatabaseUrl { get; set; }

        public string? AllowAnyDatabase { get; set; }

        public override bool Execute()
        {
            var url = string.IsNullOrEmpty(DatabaseUrl) ? Environment.GetEnvironmentVariable(DevDatabaseGuard.UrlVariable) : DatabaseUrl;
            var allow = string.IsNullOrEmpty(AllowAnyDatabase) ? Environment.GetEnvironmentVariable(DevDatabaseGuard.AllowAnyVariable) : AllowAnyDatabase;
            var check = DevDatabaseGuard.Check(url, allow);
            if (!check.IsAccepted)
            {
                Log.LogError(null, "KUBUNO0001", null, null, 0, 0, 0, 0, check.Message);
                return false;
            }

            Log.LogMessage(MessageImportance.High, "Kubuno: development database " + check.Url!.Redacted
                + (check.Verdict == DevDatabaseVerdict.Overridden ? " (accepted by " + DevDatabaseGuard.AllowAnyVariable + ")" : " accepted"));
            return true;
        }
    }

    /// <summary>Sets a variable in the build process for the tools it starts (SQLX_OFFLINE when a .sqlx cache exists).</summary>
    public sealed class KubunoSetProcessEnvironment : MSBuildTask
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        public override bool Execute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Name)))
            {
                Environment.SetEnvironmentVariable(Name, Value);
                Log.LogMessage(MessageImportance.Normal, "Kubuno: " + Name + "=" + Value);
            }

            return true;
        }
    }
}

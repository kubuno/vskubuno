using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Shared.Logging;
using Kubuno.Shared.Logic.Remote;
using Kubuno.Shared.Remote;
using Kubuno.Rust.Launch;
using Kubuno.Rust.ProjectSystem;
using Kubuno.Web.Logic.DevCore;
using Kubuno.Web.Logic.DevDatabase;
using Kubuno.Web.Logic.Modules;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Web.ProjectSystem
{
    /// <summary>
    /// F5/Ctrl+F5 of a Kubuno core or module <c>.rsproj</c> (docs/WEB.md, "F5"). CPS picks it by the project's
    /// <c>DebuggerFlavor</c>, which Kubuno.Web.Sdk sets to <see cref="DebuggerName"/> for <c>KubunoWebRole</c> Core and
    /// Module. Both start a DEV CORE (<see cref="DevCoreLayout"/>) under the native debugger:
    /// <list type="number">
    /// <item>the development database guard (<see cref="DevDatabaseGuard"/>): no <c>KUBUNO_DEV_DATABASE_URL</c>, or a
    /// database whose name does not look like a development one, refuses the launch with a message - the core runs
    /// its migrations at startup;</item>
    /// <item>when that URL points at the local end of the development database tunnel (<c>localhost:55432</c>), the SSH
    /// tunnel to the remote Linux host is opened or reused (<see cref="SshTunnels"/>); a failure cancels the launch with
    /// an info bar (<see cref="RemoteHostInfoBar"/>), never a dialog;</item>
    /// <item>a module is deployed into the dev core first (<see cref="ModuleDeployment"/>, the Windows
    /// <c>deploy_local.sh</c>), after its copies left running by a previous session are stopped;</item>
    /// <item>the core runs with the <c>KV__…</c> environment of <see cref="DevCoreEnvironment"/> (no configuration file,
    /// the database URL only in the environment);</item>
    /// <item>for a module, the debugger attaches to every module process the core starts (the core supervises and
    /// restarts it), and the browser opens on the module once the core answers.</item>
    /// </list>
    /// </summary>
    [ExportDebugger(DebuggerName)]
    [AppliesTo(RustProjectCapabilities.RustProjectSystem)]
    internal sealed class KubunoWebDebugLaunchProvider : DebugLaunchProviderBase
    {
        public const string DebuggerName = "KubunoWebDebugger";

        /// <summary>The Visual Studio native engine (CPS's DebuggerEngines.NativeOnlyEngine).</summary>
        private static readonly Guid NativeEngine = DebuggerEngines.NativeOnlyEngine;

        private LaunchPlan? _pending;

        [ImportingConstructor]
        public KubunoWebDebugLaunchProvider(ConfiguredProject configuredProject)
            : base(configuredProject)
        {
        }

        public override Task<bool> CanLaunchAsync(DebugLaunchOptions launchOptions) => Task.FromResult(true);

        public override async Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(DebugLaunchOptions launchOptions)
        {
            var properties = ConfiguredProject.Services.ProjectPropertiesProvider!.GetCommonProperties();
            async Task<string> Read(string name) => await properties.GetEvaluatedPropertyValueAsync(name).ConfigureAwait(false) ?? string.Empty;

            // Lines for the "Kubuno" pane, written once on the UI thread: CPS waits for this method synchronously on
            // the UI thread (F5), so writing to the Output pane from a pool thread here deadlocks Visual Studio.
            var log = new List<string>();
            var role = await Read("KubunoWebRole").ConfigureAwait(false);
            var isModule = string.Equals(role, "Module", StringComparison.OrdinalIgnoreCase);
            var extraEnvironment = DebugEnvironmentText.Parse(await Read("KubunoDebugEnvironment").ConfigureAwait(false));

            // 1. The development database guard - before anything is deployed or started.
            extraEnvironment.TryGetValue(DevDatabaseGuard.UrlVariable, out var urlFromProject);
            extraEnvironment.TryGetValue(DevDatabaseGuard.AllowAnyVariable, out var allowFromProject);
            var check = DevDatabaseGuard.Check(
                string.IsNullOrEmpty(urlFromProject) ? Environment.GetEnvironmentVariable(DevDatabaseGuard.UrlVariable) : urlFromProject,
                string.IsNullOrEmpty(allowFromProject) ? Environment.GetEnvironmentVariable(DevDatabaseGuard.AllowAnyVariable) : allowFromProject);
            if (!check.IsAccepted)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                KubunoLog.WriteLine("Kubuno web: F5 refused - " + check.Message);
                throw new InvalidOperationException(check.Message);
            }

            // 1b. The SSH tunnel to the remote host's PostgreSQL (Tools > Options > Kubuno > Remote Linux host), when the
            // accepted URL points at its local end. A failure is an info bar and a cancelled launch - never a dialog.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var remote = RemoteHostOptionsPage.Current;
            var remoteSettings = remote.ToSettings();
            var tunnelMode = remote.DevDatabaseTunnel;
            var tunnelLocalPort = remote.EffectiveTunnelLocalPort;
            var tunnelRemotePort = remote.EffectiveRemoteDatabasePort;
            var tunnelNeeded = DatabaseTunnelPolicy.IsNeeded(tunnelMode, check.Url!.Host, check.Url.Port, tunnelLocalPort);
            KubunoLog.WriteLine("Kubuno web: development database " + check.Url.Redacted + "; SSH tunnel "
                + (tunnelNeeded ? "needed" : "not used") + " (mode " + tunnelMode + ", local port " + tunnelLocalPort + ").");
            await TaskScheduler.Default;
            if (tunnelNeeded)
            {
                var tunnel = await SshTunnels.EnsureAsync(remoteSettings, tunnelLocalPort, tunnelRemotePort).ConfigureAwait(false);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (tunnel.CommandLine.Length > 0)
                {
                    KubunoLog.WriteLine("Kubuno remote: ssh " + tunnel.CommandLine);
                }

                if (!tunnel.IsOpen)
                {
                    KubunoLog.WriteLine("Kubuno web: F5 cancelled - the development database tunnel could not be opened. " + tunnel.Message);
                    if (tunnel.ErrorOutput.Trim() is { Length: > 0 } sshErrors)
                    {
                        KubunoLog.WriteLine("Kubuno remote: ssh said: " + sshErrors.Replace(Environment.NewLine, " | "));
                    }

                    RemoteHostInfoBar.ShowFailure(tunnel, remoteSettings);
                    throw new LaunchCancelledException("Kubuno: the development database tunnel could not be opened (see the info bar and the Kubuno Output pane).");
                }

                RemoteHostInfoBar.Close();
                log.Add("Kubuno remote: " + tunnel.Message);
                await TaskScheduler.Default;
            }

            var rootText = await Read("KubunoDevCoreRoot").ConfigureAwait(false);
            var layout = string.IsNullOrWhiteSpace(rootText) ? DevCoreLayout.Default() : new DevCoreLayout(rootText);
            layout.EnsureCreated();
            var port = int.TryParse(await Read("KubunoDevCorePort").ConfigureAwait(false), out var parsedPort) && parsedPort > 0 ? parsedPort : DevCoreLayout.DefaultPort;

            var targetPath = await ResolveTargetPathAsync(properties).ConfigureAwait(false);
            string coreExecutable;
            string? coreRepository;
            ModuleManifest? manifest = null;
            string? deployedExecutable = null;
            if (isModule)
            {
                // 2. Deploy the module (its frontend was built first: BuildDependency in the generated solution).
                var moduleDirectory = await Read("KubunoModuleDirectory").ConfigureAwait(false);
                if (string.IsNullOrEmpty(moduleDirectory))
                {
                    moduleDirectory = Path.GetDirectoryName(await Read("CargoManifestPath").ConfigureAwait(false))!;
                }

                manifest = ModuleManifest.Load(Path.Combine(moduleDirectory, "module.toml"));
                var deployedDirectory = layout.ModuleDirectory(manifest.Id);
                var stopped = ModuleDeployment.StopProcessesUnder(deployedDirectory, Path.GetFileNameWithoutExtension(manifest.WindowsExecutableName));
                var copied = ModuleDeployment.Execute(ModuleDeployment.Plan(moduleDirectory, targetPath, deployedDirectory, manifest));
                deployedExecutable = Path.Combine(deployedDirectory, manifest.WindowsExecutableName);
                log.Add("Kubuno web: " + manifest.Id + " deployed to " + deployedDirectory + " (" + copied + " file(s) updated"
                    + (stopped.Count > 0 ? ", " + stopped.Count + " running copy(ies) stopped" : string.Empty) + ").");

                var explicitCore = await Read("KubunoDevCoreExecutable").ConfigureAwait(false);
                var cargoTargetDirectory = await Read("CargoTargetDir").ConfigureAwait(false);
                coreExecutable = DevCoreLocator.FindCoreExecutable(moduleDirectory, explicitCore, string.IsNullOrEmpty(cargoTargetDirectory) ? Environment.GetEnvironmentVariable("CARGO_TARGET_DIR") : cargoTargetDirectory)
                    ?? throw new FileNotFoundException(
                        "No Kubuno core to start the module in. Build the core (open core\\Kubuno.Core.Web.slnx, or the multi-repository solution, and build it), "
                        + "install Kubuno on this machine, or set \"Core executable\" on this project's Debug page. Looked at: "
                        + string.Join(", ", DevCoreLocator.Candidates(moduleDirectory, explicitCore, cargoTargetDirectory)));
                // The core repository next to the module, else the one of the last core F5 (a module checked out elsewhere).
                coreRepository = DevCoreLocator.SiblingCoreRepository(moduleDirectory) ?? layout.RememberedCoreRepository();
            }
            else
            {
                coreExecutable = targetPath;
                coreRepository = await Read("CargoWorkspaceRoot").ConfigureAwait(false);
                if (string.IsNullOrEmpty(coreRepository))
                {
                    coreRepository = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(await Read("CargoManifestPath").ConfigureAwait(false))!, "..", ".."));
                }

                layout.RememberCoreRepository(coreRepository);
            }

            // 3. The dev core's environment: KV__ settings, generated secrets, the accepted database URL.
            layout.SeedThemes(coreRepository is null ? null : Path.Combine(coreRepository, "themes"));
            var frontendDist = DevCoreLocator.FindFrontendDist(coreRepository, coreExecutable);
            if (frontendDist is null)
            {
                log.Add("Kubuno web: no built host frontend (core\\frontend\\dist) - the core serves its API only. Build the kubuno-frontend project, or use the Vite dev server.");
            }

            var environment = DevCoreEnvironment.Build(layout, check.Url!.Original, layout.LoadOrCreateSecrets(), frontendDist, port).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RUST_LOG")))
            {
                environment["RUST_LOG"] = "info";
            }

            environment["RUST_BACKTRACE"] = "1";
            foreach (var pair in extraEnvironment.Where(pair => pair.Key != DevDatabaseGuard.UrlVariable && pair.Key != DevDatabaseGuard.AllowAnyVariable))
            {
                environment[pair.Key] = pair.Value;
            }

            log.Add("Kubuno web: dev core " + coreExecutable + " on http://localhost:" + port + "/, data in " + layout.Root
                + ", database " + check.Url.Redacted + (check.Verdict == DevDatabaseVerdict.Overridden ? " (accepted by " + DevDatabaseGuard.AllowAnyVariable + ")" : string.Empty) + ".");
            if (!File.Exists(layout.InitialPasswordFile))
            {
                log.Add("Kubuno web: on a fresh database the core creates the first administrator; its password is then written to " + layout.InitialPasswordFile + ".");
            }

            RustDebuggerSettings.EnsureDebuggerFilesInstalled();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            foreach (var line in log)
            {
                KubunoLog.WriteLine(line);
            }

            RustDebuggerSettings.EnsurePanicExceptionSetting();
            var launchBrowser = !string.Equals(await Read("KubunoLaunchBrowser").ConfigureAwait(true), "false", StringComparison.OrdinalIgnoreCase)
                && !FrontendProjectIsStarting();
            await TaskScheduler.Default;

            var url = await Read("KubunoLaunchUrl").ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(url))
            {
                var path = manifest?.SidebarPath ?? (manifest is null ? "/" : "/" + manifest.Id);
                url = "http://localhost:" + port + (path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path);
            }

            var attach = isModule && (launchOptions & DebugLaunchOptions.NoDebug) == 0
                && !string.Equals(await Read("KubunoAttachToModule").ConfigureAwait(false), "false", StringComparison.OrdinalIgnoreCase);
            // The core's console goes to a file the Output pane follows (CoreConsoleTail); the previous session's is replaced.
            var consoleLog = Path.Combine(layout.Root, "logs", "core-console.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(consoleLog)!);
                File.Delete(consoleLog);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // Still held by a core that has not exited yet: the tail starts from the end of what is there.
            }

            _pending = new LaunchPlan(coreExecutable, port, launchBrowser ? url : null, attach ? deployedExecutable : null, consoleLog, DatabaseSecrets(check.Url.Original), deployedExecutable);

            var settings = new DebugLaunchSettings(launchOptions)
            {
                LaunchOperation = DebugLaunchOperation.CreateProcess,
                LaunchDebugEngineGuid = NativeEngine,
                Executable = coreExecutable,
                // Redirection understood by the native debugger's process launch (not passed to the core).
                Arguments = "> \"" + consoleLog + "\" 2>&1",
                CurrentDirectory = layout.Root,
            };

            // The whole environment is given (not merged): Visual Studio's own variables plus the dev core's.
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                settings.Environment[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
            }

            foreach (var pair in environment)
            {
                settings.Environment[pair.Key] = pair.Value;
            }

            return new IDebugLaunchSettings[] { settings };
        }

        public override async Task LaunchAsync(DebugLaunchOptions launchOptions)
        {
            try
            {
                await base.LaunchAsync(launchOptions).ConfigureAwait(true);
            }
            catch (LaunchCancelledException)
            {
                // Already explained by an info bar and the Kubuno pane: no modal message box on top.
                _pending = null;
                return;
            }

            var plan = _pending;
            _pending = null;
            if (plan is null)
            {
                return;
            }

            var started = DateTime.Now.AddSeconds(-5);
            _ = Task.Run(() => FollowAsync(plan, started));
        }

        /// <summary>Waits for the core to answer (then opens the browser) and attaches to the module processes until the core exits.</summary>
        private static async Task FollowAsync(LaunchPlan plan, DateTime started)
        {
            try
            {
                var attached = new HashSet<int>();
                var browserOpened = plan.BrowserUrl is null;
                var deadline = DateTime.UtcNow.AddMinutes(10);
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var coreSeen = false;
                var console = new CoreConsoleTail(plan.ConsoleLog, plan.Masks);
                while (DateTime.UtcNow < deadline || attached.Count > 0 || coreSeen)
                {
                    var coreAlive = ProcessesAt(plan.CoreExecutable).Any(process => SafeStartTime(process) >= started);
                    coreSeen |= coreAlive;
                    await console.PumpAsync(final: coreSeen && !coreAlive).ConfigureAwait(false);
                    if (coreSeen && !coreAlive)
                    {
                        StopModuleCopies(plan);
                        return; // The debug session ended.
                    }

                    if (!browserOpened && await AnswersAsync(client, "http://127.0.0.1:" + plan.Port + "/").ConfigureAwait(false))
                    {
                        browserOpened = true;
                        KubunoLog.WriteLine("Kubuno web: the dev core answers - opening " + plan.BrowserUrl);
                        Process.Start(new ProcessStartInfo(plan.BrowserUrl!) { UseShellExecute = true })?.Dispose();
                    }

                    if (plan.ModuleExecutable is not null)
                    {
                        foreach (var process in ProcessesAt(plan.ModuleExecutable))
                        {
                            if (attached.Add(process.Id))
                            {
                                await AttachAsync(process.Id, plan.ModuleExecutable).ConfigureAwait(false);
                            }
                        }
                    }

                    if (!coreSeen && DateTime.UtcNow > deadline)
                    {
                        return;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is HttpRequestException || exception is System.ComponentModel.Win32Exception)
            {
                KubunoLog.WriteLine("Kubuno web: " + exception.Message);
            }
        }

        private static async Task AttachAsync(int processId, string executable)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Through the automation model first: IVsDebugger4.LaunchDebugTargets4 with DLO_AlreadyRunning was found live to
            // refuse a process the core started while the core itself is being debugged (E_INVALIDARG, "operation not
            // supported"); Process2.Attach2 with the native engine attaches it into the same session.
            try
            {
                if (Package.GetGlobalService(typeof(EnvDTE.DTE)) is EnvDTE80.DTE2 dte)
                {
                    foreach (EnvDTE.Process process in dte.Debugger.LocalProcesses)
                    {
                        if (process.ProcessID == processId)
                        {
                            if (process is EnvDTE80.Process2 process2)
                            {
                                process2.Attach2(NativeEngine.ToString("B"));
                            }
                            else
                            {
                                process.Attach();
                            }

                            KubunoLog.WriteLine("Kubuno web: debugger attached to " + Path.GetFileName(executable) + " (process " + processId + ").");
                            return;
                        }
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException exception)
            {
                KubunoLog.WriteLine("Kubuno web: attaching through the automation model failed (" + exception.Message + "); trying the debugger service.");
            }

            if (Package.GetGlobalService(typeof(SVsShellDebugger)) is not IVsDebugger4 debugger)
            {
                return;
            }

            var target = new VsDebugTargetInfo4
            {
                dlo = (uint)DEBUG_LAUNCH_OPERATION.DLO_AlreadyRunning,
                dwProcessId = (uint)processId,
                bstrExe = executable,
                guidLaunchDebugEngine = NativeEngine,
            };
            var results = new VsDebugTargetProcessInfo[1];
            try
            {
                debugger.LaunchDebugTargets4(1, new[] { target }, results);
                KubunoLog.WriteLine("Kubuno web: debugger attached to " + Path.GetFileName(executable) + " (process " + processId + ").");
            }
            catch (System.Runtime.InteropServices.COMException exception)
            {
                KubunoLog.WriteLine("Kubuno web: could not attach to process " + processId + ": " + exception.Message);
            }
        }

        private static async Task<bool> AnswersAsync(HttpClient client, string url)
        {
            try
            {
                using var response = await client.GetAsync(url).ConfigureAwait(false);
                return true;
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException)
            {
                return false;
            }
        }

        private static IEnumerable<Process> ProcessesAt(string executable)
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
            {
                string? path;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception)
                {
                    continue;
                }

                if (string.Equals(path, executable, StringComparison.OrdinalIgnoreCase))
                {
                    yield return process;
                }
            }
        }

        private static DateTime SafeStartTime(Process process)
        {
            try
            {
                return process.StartTime;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception)
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>Whether a JavaScript project is among the startup projects (it opens its own browser, with the script debugger).</summary>
        private static bool FrontendProjectIsStarting()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (Package.GetGlobalService(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte && dte.Solution?.SolutionBuild?.StartupProjects is object[] startup)
                {
                    return startup.OfType<string>().Any(name => name.EndsWith(".esproj", StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // No solution build manager yet: open the browser.
            }

            return false;
        }

        /// <summary>
        /// <c>$(TargetPath)</c>, or the same file under <c>CARGO_TARGET_DIR</c> when the evaluated path assumed the
        /// workspace's own target folder (the workspace build reports its executable during the build only).
        /// </summary>
        private static async Task<string> ResolveTargetPathAsync(IProjectProperties properties)
        {
            var targetPath = await properties.GetEvaluatedPropertyValueAsync("TargetPath").ConfigureAwait(false) ?? string.Empty;
            if (File.Exists(targetPath))
            {
                return targetPath;
            }

            var targetDirectory = Environment.GetEnvironmentVariable("CARGO_TARGET_DIR");
            var profile = Path.GetFileName(Path.GetDirectoryName(targetPath) ?? string.Empty);
            if (!string.IsNullOrEmpty(targetDirectory) && !string.IsNullOrEmpty(profile))
            {
                var candidate = Path.Combine(targetDirectory, profile, Path.GetFileName(targetPath));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException("The executable '" + targetPath + "' does not exist. Build the project first.", targetPath);
        }

        /// <summary>
        /// Ends the copies of the deployed module the core left behind: Stop Debugging terminates the core but only
        /// detaches from the module process it started, which would keep running (and hold its port and database
        /// connections) without its core. Only processes whose image lies in the deployed folder are touched.
        /// </summary>
        private static void StopModuleCopies(LaunchPlan plan)
        {
            if (plan.DeployedModuleExecutable is not { } executable)
            {
                return;
            }

            var stopped = ModuleDeployment.StopProcessesUnder(Path.GetDirectoryName(executable)!, Path.GetFileNameWithoutExtension(executable));
            if (stopped.Count > 0)
            {
                KubunoLog.WriteLine("Kubuno web: the dev core ended - " + Path.GetFileName(executable) + " stopped (process " + string.Join(", ", stopped) + ").");
            }
        }

        /// <summary>The database URL and its password (raw and decoded): masked in the core's console pane.</summary>
        private static IReadOnlyList<string> DatabaseSecrets(string url)
        {
            var secrets = new List<string> { url };
            var scheme = url.IndexOf("://", StringComparison.Ordinal);
            var at = url.LastIndexOf('@');
            if (scheme >= 0 && at > scheme)
            {
                var userInfo = url.Substring(scheme + 3, at - scheme - 3);
                var colon = userInfo.IndexOf(':');
                if (colon >= 0 && colon < userInfo.Length - 1)
                {
                    var password = userInfo.Substring(colon + 1);
                    secrets.Add(password);
                    secrets.Add(Uri.UnescapeDataString(password));
                }
            }

            return secrets;
        }

        /// <summary>A launch stopped on purpose after an info bar said why: <see cref="LaunchAsync"/> ends it without CPS's message box.</summary>
        private sealed class LaunchCancelledException : Exception
        {
            public LaunchCancelledException(string message)
                : base(message)
            {
            }
        }

        private sealed class LaunchPlan
        {
            public LaunchPlan(string coreExecutable, int port, string? browserUrl, string? moduleExecutable, string consoleLog, IReadOnlyList<string> masks, string? deployedModuleExecutable)
            {
                DeployedModuleExecutable = deployedModuleExecutable;
                ConsoleLog = consoleLog;
                Masks = masks;
                CoreExecutable = coreExecutable;
                Port = port;
                BrowserUrl = browserUrl;
                ModuleExecutable = moduleExecutable;
            }

            public string CoreExecutable { get; }

            public int Port { get; }

            public string? BrowserUrl { get; }

            public string? ModuleExecutable { get; }

            public string ConsoleLog { get; }

            /// <summary>Strings never shown in the Output pane (the database password).</summary>
            public IReadOnlyList<string> Masks { get; }

            /// <summary>The module deployed for this session (stopped with the core), whether or not it is debugged.</summary>
            public string? DeployedModuleExecutable { get; }
        }
    }
}

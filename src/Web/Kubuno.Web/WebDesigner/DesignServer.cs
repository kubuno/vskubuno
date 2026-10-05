using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Shared.Logging;
using Kubuno.Web.Logic.WebDesigner;

namespace Kubuno.Web.WebDesigner
{
    /// <summary>
    /// The project's own dev server, as the web design surface uses it (docs/WEB-VIEWS.md section 4.2): found through the
    /// discovery file the <c>@kubuno/views-compiler</c> Vite plugin writes (<see cref="DesignServerDiscovery"/>) and probed
    /// over HTTP (<c>project.json</c> beside the design page), or started on request (« Démarrer le serveur de
    /// développement ») with the project's own npm (<c>npm run dev</c>; through <c>obj\kubuno-npm.cmd</c> when
    /// <c>Kubuno.Web.Sdk</c> prepared this machine's Node environment, docs/WEB.md). A started server runs in a job object
    /// that ends with Visual Studio, so nothing outlives the IDE. One state per project root, shared by every pane.
    /// </summary>
    internal static class DesignServer
    {
        private static readonly HttpClient s_http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        private static readonly ConcurrentDictionary<string, Process> s_started = new ConcurrentDictionary<string, Process>(StringComparer.OrdinalIgnoreCase);
        private static IntPtr s_job;

        /// <summary>The design page of the project's running dev server, or null when none answers.</summary>
        public static async Task<Uri?> FindAsync(string projectRoot, CancellationToken cancellationToken)
        {
            string? discovery;
            try
            {
                var file = DesignServerDiscovery.FilePath(projectRoot);
                discovery = File.Exists(file) ? File.ReadAllText(file) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                discovery = null;
            }

            foreach (var page in DesignServerDiscovery.CandidateUrls(discovery))
            {
                if (await IsAliveAsync(page, cancellationToken).ConfigureAwait(false))
                {
                    return page;
                }
            }

            return null;
        }

        /// <summary>Whether the dev server serving <paramref name="designPage"/> answers (its <c>project.json</c>).</summary>
        public static async Task<bool> IsAliveAsync(Uri designPage, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await s_http.GetAsync(DesignServerDiscovery.ProjectInfoUrl(designPage), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>Whether this Visual Studio started a dev server for the project that is still running.</summary>
        public static bool IsStarting(string projectRoot) =>
            s_started.TryGetValue(projectRoot, out var process) && !HasExited(process);

        /// <summary>
        /// Starts <c>npm run dev</c> in the project root (hidden; its output goes to the Kubuno Output pane). Null when it
        /// started (or already runs), else why not.
        /// </summary>
        public static string? Start(string projectRoot)
        {
            if (IsStarting(projectRoot))
            {
                return null;
            }

            var npm = Path.Combine(projectRoot, "obj", "kubuno-npm.cmd");
            var info = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } comspec ? comspec : "cmd.exe",
                // chcp 65001: the server's output reaches the Output pane as UTF-8.
                Arguments = File.Exists(npm) ? "/d /s /c \"chcp 65001 >nul & \"" + npm + "\" run dev\"" : "/d /s /c \"chcp 65001 >nul & npm run dev\"",
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            info.EnvironmentVariables["BROWSER"] = "none";
            info.EnvironmentVariables["FORCE_COLOR"] = "0";
            if (IsNetworkPath(projectRoot))
            {
                // Node's fs.watch fails on a network share (UNKNOWN, errno -4094, measured on Z: from a Linux checkout):
                // Vite's file watcher polls instead.
                info.EnvironmentVariables["CHOKIDAR_USEPOLLING"] = "1";
                info.EnvironmentVariables["CHOKIDAR_INTERVAL"] = "1000";
            }

            if (VsNodeFolder() is { } node)
            {
                // Visual Studio's own Node.js after the machine's (docs/WEB.md, "Frontends": this machine may have no other);
                // obj\kubuno-npm.cmd only adds the native-package overlay, it expects npm on the PATH.
                info.EnvironmentVariables["PATH"] = Environment.GetEnvironmentVariable("PATH") + ";" + node;
            }

            try
            {
                var process = new Process { StartInfo = info, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, e) => Log(e.Data);
                process.ErrorDataReceived += (_, e) => Log(e.Data);
                process.Exited += (_, _) => KubunoLog.WriteLine($"[web-designer] dev server of {projectRoot} exited ({SafeExitCode(process)}).");
                if (!process.Start())
                {
                    return "the process did not start";
                }

                AssignToJob(process);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                s_started[projectRoot] = process;
                KubunoLog.WriteLine($"[web-designer] dev server starting in {projectRoot}: {info.FileName} {info.Arguments} (pid {process.Id}).");
                return null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                KubunoLog.WriteException("[web-designer] starting the dev server failed", ex);
                return ex.Message;
            }
        }

        /// <summary>Stops the dev server this Visual Studio started for the project (the whole process tree).</summary>
        public static void Stop(string projectRoot)
        {
            if (!s_started.TryRemove(projectRoot, out var process) || HasExited(process))
            {
                return;
            }

            try
            {
                // cmd → npm → node: end the tree, not only cmd.
                using var kill = Process.Start(new ProcessStartInfo("taskkill", "/T /F /PID " + process.Id) { UseShellExecute = false, CreateNoWindow = true });
                kill?.WaitForExit(5000);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                KubunoLog.WriteException("[web-designer] stopping the dev server failed", ex);
            }
        }

        private static bool IsNetworkPath(string path)
        {
            try
            {
                if (path.StartsWith(@"\\", StringComparison.Ordinal))
                {
                    return true;
                }

                var root = Path.GetPathRoot(Path.GetFullPath(path));
                return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return false;
            }
        }

        private static void Log(string? line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                KubunoLog.WriteLine("[web-designer] dev server: " + line);
            }
        }

        private static bool HasExited(Process process)
        {
            try
            {
                return process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        private static string SafeExitCode(Process process)
        {
            try
            {
                return "exit " + process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                return "exit unknown";
            }
        }

        private static string? VsNodeFolder()
        {
            try
            {
                var ide = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty);
                var node = ide is null ? null : Path.GetFullPath(Path.Combine(ide, "..", "..", "MSBuild", "Microsoft", "VisualStudio", "NodeJs"));
                return node is not null && File.Exists(Path.Combine(node, "node.exe")) ? node : null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }

        // ---- job object: started servers end with Visual Studio ----

        private static void AssignToJob(Process process)
        {
            try
            {
                if (s_job == IntPtr.Zero)
                {
                    var job = CreateJobObject(IntPtr.Zero, null);
                    var limits = new JobObjectExtendedLimitInformation { BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose } };
                    var size = Marshal.SizeOf(typeof(JobObjectExtendedLimitInformation));
                    var pointer = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.StructureToPtr(limits, pointer, false);
                        SetInformationJobObject(job, JobObjectExtendedLimitInformationClass, pointer, (uint)size);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pointer);
                    }

                    s_job = job;
                }

                AssignProcessToJobObject(s_job, process.Handle);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                KubunoLog.WriteException("[web-designer] the dev server could not be tied to Visual Studio's lifetime", ex);
            }
        }

        private const uint JobObjectLimitKillOnJobClose = 0x2000;
        private const int JobObjectExtendedLimitInformationClass = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    }
}

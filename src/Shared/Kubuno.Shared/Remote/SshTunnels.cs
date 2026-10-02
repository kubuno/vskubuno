using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Shared.Logging;
using Kubuno.Shared.Logic.Remote;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Shared.Remote
{
    /// <summary>The outcome of <see cref="SshTunnels.EnsureAsync"/>.</summary>
    public sealed class SshTunnelResult
    {
        private SshTunnelResult(bool isOpen, string message, SshFailureKind failure, string errorOutput)
        {
            IsOpen = isOpen;
            Message = message;
            Failure = failure;
            ErrorOutput = errorOutput;
        }

        /// <summary>The local port answers: the tunnel is up (just opened, reused, or opened outside Visual Studio).</summary>
        public bool IsOpen { get; }

        /// <summary>What happened, for the Output pane and the info bar.</summary>
        public string Message { get; }

        public SshFailureKind Failure { get; }

        /// <summary>ssh's own error output (empty on success).</summary>
        public string ErrorOutput { get; }

        /// <summary>The ssh command line when this call started ssh (empty when it reused a tunnel or did not get that far), for the caller's log.</summary>
        public string CommandLine { get; private set; } = string.Empty;

        internal SshTunnelResult Ran(string commandLine)
        {
            CommandLine = commandLine;
            return this;
        }

        internal static SshTunnelResult Open(string message) => new(true, message, SshFailureKind.Other, string.Empty);

        internal static SshTunnelResult Failed(SshFailureKind failure, string message, string errorOutput) => new(false, message, failure, errorOutput);
    }

    /// <summary>
    /// The SSH tunnels Visual Studio keeps open to the remote Linux host (docs/WEB.md, "The development database"): one
    /// <c>ssh -N -L</c> process per local port, run with Windows' OpenSSH in batch mode (<see cref="SshCommandLine"/>).
    /// A tunnel is reused by the next launches while its process lives and its port answers; it ends with Visual Studio -
    /// <see cref="StopAll"/> at the package's disposal, and a kill-on-close job object if Visual Studio ends otherwise.
    /// Never prompts: errors come back as a <see cref="SshTunnelResult"/> (shown by <see cref="RemoteHostInfoBar"/>).
    /// </summary>
    public static class SshTunnels
    {
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(25);
        private static readonly object SyncRoot = new();
        private static readonly Dictionary<int, Tunnel> ByLocalPort = new();
        private static readonly SemaphoreSlim StartLock = new(1, 1);
        private static IntPtr _job;

        /// <summary>Opens (or reuses) the tunnel <c>localhost:&lt;localPort&gt;</c> → <c>localhost:&lt;remotePort&gt;</c> on the host. Never throws for an ssh failure.</summary>
        public static async Task<SshTunnelResult> EnsureAsync(RemoteHostSettings settings, int localPort, int remotePort, CancellationToken cancellationToken = default)
        {
            var problem = settings.Validate();
            if (problem is not null)
            {
                return SshTunnelResult.Failed(SshFailureKind.Other, "Remote Linux host settings: " + problem + ".", string.Empty);
            }

            var arguments = SshCommandLine.TunnelArguments(settings, localPort, remotePort);
            var identity = string.Join("\u001f", arguments);
            await StartLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Tunnel? existing;
                lock (SyncRoot)
                {
                    ByLocalPort.TryGetValue(localPort, out existing);
                }

                if (existing is not null)
                {
                    if (!existing.HasExited && existing.Identity == identity && await AnswersAsync(localPort).ConfigureAwait(false))
                    {
                        return SshTunnelResult.Open("SSH tunnel localhost:" + localPort + " → " + settings.Destination + " reused (ssh process " + existing.ProcessId + ").");
                    }

                    Stop(localPort);
                }
                else if (await AnswersAsync(localPort).ConfigureAwait(false))
                {
                    return SshTunnelResult.Open("localhost:" + localPort + " already answers (a tunnel opened outside Visual Studio?): used as is.");
                }

                if (!File.Exists(settings.PrivateKeyPath))
                {
                    var kind = SshFailureKind.KeyFileMissing;
                    return SshTunnelResult.Failed(kind, SshFailure.Describe(kind, settings, localPort, null), string.Empty);
                }

                var ssh = SshCommandLine.DefaultSshExecutable;
                if (!File.Exists(ssh))
                {
                    return SshTunnelResult.Failed(SshFailureKind.Other, "Windows' OpenSSH client (" + ssh + ") is not installed: add the optional feature \"OpenSSH Client\" (Settings > System > Optional features).", string.Empty);
                }

                var tunnel = Tunnel.Start(ssh, arguments, identity);
                lock (SyncRoot)
                {
                    ByLocalPort[localPort] = tunnel;
                }

                // Not logged from here: F5 waits for this on the UI thread, and the Output pane is an STA object (a write from
                // this thread would deadlock Visual Studio). The callers log CommandLine once back on the UI thread.
                var commandLine = SshCommandLine.ToCommandLine(arguments);
                var deadline = DateTime.UtcNow + StartTimeout;
                while (DateTime.UtcNow < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (tunnel.HasExited)
                    {
                        Forget(localPort, tunnel);
                        var errors = tunnel.ErrorOutput;
                        var kind = SshFailure.Classify(errors);
                        return SshTunnelResult.Failed(kind, SshFailure.Describe(kind, settings, localPort, errors), errors).Ran(commandLine);
                    }

                    if (await AnswersAsync(localPort).ConfigureAwait(false))
                    {
                        tunnel.WatchExit(localPort);
                        return SshTunnelResult.Open("SSH tunnel localhost:" + localPort + " → " + settings.Destination + " (localhost:" + remotePort + ") open (ssh process " + tunnel.ProcessId + ").").Ran(commandLine);
                    }

                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }

                Stop(localPort);
                var timeoutErrors = tunnel.ErrorOutput;
                var timeoutKind = SshFailure.Classify(timeoutErrors);
                return SshTunnelResult.Failed(
                    timeoutKind == SshFailureKind.Other ? SshFailureKind.HostUnreachable : timeoutKind,
                    SshFailure.Describe(timeoutKind == SshFailureKind.Other ? SshFailureKind.HostUnreachable : timeoutKind, settings, localPort, timeoutErrors),
                    timeoutErrors).Ran(commandLine);
            }
            finally
            {
                StartLock.Release();
            }
        }

        /// <summary>Whether Visual Studio has a live tunnel on this local port.</summary>
        public static bool IsRunning(int localPort)
        {
            lock (SyncRoot)
            {
                return ByLocalPort.TryGetValue(localPort, out var tunnel) && !tunnel.HasExited;
            }
        }

        /// <summary>Ends the tunnel of this local port (no-op when there is none).</summary>
        public static void Stop(int localPort)
        {
            Tunnel? tunnel;
            lock (SyncRoot)
            {
                ByLocalPort.TryGetValue(localPort, out tunnel);
                ByLocalPort.Remove(localPort);
            }

            tunnel?.Kill();
        }

        /// <summary>Ends every tunnel (the package is being disposed: Visual Studio closes).</summary>
        public static void StopAll()
        {
            List<Tunnel> tunnels;
            lock (SyncRoot)
            {
                tunnels = ByLocalPort.Values.ToList();
                ByLocalPort.Clear();
            }

            foreach (var tunnel in tunnels)
            {
                tunnel.Kill();
            }
        }

        private static void Forget(int localPort, Tunnel tunnel)
        {
            lock (SyncRoot)
            {
                if (ByLocalPort.TryGetValue(localPort, out var current) && ReferenceEquals(current, tunnel))
                {
                    ByLocalPort.Remove(localPort);
                }
            }
        }

        private static async Task<bool> AnswersAsync(int port)
        {
            using var client = new TcpClient();
            try
            {
                var connect = client.ConnectAsync("127.0.0.1", port);
                var finished = await Task.WhenAny(connect, Task.Delay(400)).ConfigureAwait(false);
                if (finished != connect)
                {
                    return false;
                }

                await connect.ConfigureAwait(false);
                return client.Connected;
            }
            catch (Exception exception) when (exception is SocketException || exception is ObjectDisposedException || exception is InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>A kill-on-close job object: the ssh processes end with Visual Studio even when the package is not disposed.</summary>
        private static void AddToJob(Process process)
        {
            lock (SyncRoot)
            {
                if (_job == IntPtr.Zero)
                {
                    var job = NativeMethods.CreateJobObject(IntPtr.Zero, null);
                    if (job == IntPtr.Zero)
                    {
                        return;
                    }

                    var info = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                    var length = Marshal.SizeOf(typeof(NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                    var pointer = Marshal.AllocHGlobal(length);
                    try
                    {
                        Marshal.StructureToPtr(info, pointer, false);
                        if (!NativeMethods.SetInformationJobObject(job, NativeMethods.JobObjectExtendedLimitInformation, pointer, (uint)length))
                        {
                            NativeMethods.CloseHandle(job);
                            return;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pointer);
                    }

                    _job = job;
                }

                try
                {
                    NativeMethods.AssignProcessToJobObject(_job, process.Handle);
                }
                catch (InvalidOperationException)
                {
                    // Already exited.
                }
            }
        }

        private sealed class Tunnel
        {
            private readonly Process _process;
            private readonly StringBuilder _errors = new();
            private int _killed;

            private Tunnel(Process process, string identity)
            {
                _process = process;
                Identity = identity;
            }

            public string Identity { get; }

            public int ProcessId { get; private set; }

            public bool HasExited
            {
                get
                {
                    try
                    {
                        return _process.HasExited;
                    }
                    catch (InvalidOperationException)
                    {
                        return true;
                    }
                }
            }

            public string ErrorOutput
            {
                get
                {
                    lock (_errors)
                    {
                        return _errors.ToString();
                    }
                }
            }

            public static Tunnel Start(string ssh, IReadOnlyList<string> arguments, string identity)
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo(ssh, SshCommandLine.ToCommandLine(arguments))
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = Path.GetTempPath(),
                    },
                    EnableRaisingEvents = true,
                };
                var tunnel = new Tunnel(process, identity);
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data is { } line)
                    {
                        lock (tunnel._errors)
                        {
                            if (tunnel._errors.Length < 16384)
                            {
                                tunnel._errors.AppendLine(line);
                            }
                        }
                    }
                };
                process.OutputDataReceived += (_, _) => { };
                process.Start();
                tunnel.ProcessId = process.Id;
                AddToJob(process);
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
                return tunnel;
            }

            /// <summary>Logs the end of a tunnel that was open (host gone, network lost) - not the ends Kubuno asked for.</summary>
            public void WatchExit(int localPort)
            {
                _process.Exited += (_, _) =>
                {
                    if (Volatile.Read(ref _killed) != 0)
                    {
                        return;
                    }

                    Forget(localPort, this);
                    var detail = ErrorOutput.Trim();
                    var message = "Kubuno remote: the SSH tunnel localhost:" + localPort + " closed" + (detail.Length > 0 ? ": " + detail.Replace(Environment.NewLine, " | ") : ".") + " The next launch opens it again.";

                    // Written from the UI thread, never from this one (the pane is an STA object; see EnsureAsync).
                    KubunoHost.JoinableTaskFactory.RunAsync(async () =>
                    {
                        await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        KubunoLog.WriteLine(message);
                    }).Task.Forget();
                };
            }

            public void Kill()
            {
                Interlocked.Exchange(ref _killed, 1);
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill();
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception)
                {
                    // Already gone.
                }

                _process.Dispose();
            }
        }

        private static class NativeMethods
        {
            public const int JobObjectExtendedLimitInformation = 9;
            public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CloseHandle(IntPtr handle);

            [StructLayout(LayoutKind.Sequential)]
            public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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
            public struct IO_COUNTERS
            {
                public ulong ReadOperationCount;
                public ulong WriteOperationCount;
                public ulong OtherOperationCount;
                public ulong ReadTransferCount;
                public ulong WriteTransferCount;
                public ulong OtherTransferCount;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
                public IO_COUNTERS IoInfo;
                public UIntPtr ProcessMemoryLimit;
                public UIntPtr JobMemoryLimit;
                public UIntPtr PeakProcessMemoryUsed;
                public UIntPtr PeakJobMemoryUsed;
            }
        }
    }
}

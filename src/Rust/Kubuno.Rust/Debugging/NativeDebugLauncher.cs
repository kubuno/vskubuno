using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Kubuno.Rust.Launch;
using Kubuno.Core.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.Rust.Debugging
{
    /// <summary>
    /// Launches a <see cref="LaunchDescription"/> under Visual Studio's native (MSVC/PDB) debug
    /// engine via <c>IVsDebugger4.LaunchDebugTargets4</c> - the same shell-level API real-world
    /// VS extensions for other native/foreign toolchains use to start a process under the
    /// debugger without a project system. Used by <c>DebugRustTestAtCursorCommand</c>, where a
    /// specific, just-built test binary must be launched immediately (a static
    /// <c>launch.vs.json</c> entry, as used for ordinary bin/example targets - see
    /// <see cref="RustLaunchTargetsGenerator"/> - isn't a fit: the mangled test binary path is
    /// only known after `cargo test --no-run`, per build).
    /// </summary>
    internal static class NativeDebugLauncher
    {
        public static async Task LaunchAsync(LaunchDescription description, AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (await package.GetServiceAsync(typeof(SVsShellDebugger)) is not IVsDebugger4 debugger)
            {
                KubunoLog.WriteLine("Kubuno: could not get IVsDebugger4 (SVsShellDebugger); cannot launch under the debugger.");
                return;
            }

            var info = new VsDebugTargetInfo4
            {
                dlo = (uint)DEBUG_LAUNCH_OPERATION.DLO_CreateProcess,
                bstrExe = description.ExecutablePath,
                bstrArg = BuildArgumentString(description.Arguments),
                bstrCurDir = description.WorkingDirectory,
                bstrEnv = BuildEnvironmentBlock(description.EnvironmentVariables),
                guidLaunchDebugEngine = VSConstants.DebugEnginesGuids.NativeOnly_guid,
                LaunchFlags = (uint)__VSDBGLAUNCHFLAGS.DBGLAUNCH_StopDebuggingOnEnd,
            };

            var targets = new[] { info };
            var results = new VsDebugTargetProcessInfo[1];

            // Just My Code and step filters for Rust (docs/DEBUGGING.md), read at the start of the session.
            Kubuno.Rust.ProjectSystem.RustDebuggerSettings.EnsureDebuggerFilesInstalled(KubunoLog.WriteLine);
            Kubuno.Rust.ProjectSystem.RustDebuggerSettings.EnsurePanicExceptionSetting(KubunoLog.WriteLine);

            KubunoLog.WriteLine($"Kubuno: launching '{description.ExecutablePath}' {info.bstrArg} under the native debugger (cwd: {description.WorkingDirectory}).");
            try
            {
                debugger.LaunchDebugTargets4(1, targets, results);
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno: LaunchDebugTargets4 failed", exception);
            }
        }

        private static string BuildArgumentString(IReadOnlyList<string> arguments)
        {
            if (arguments.Count == 0)
            {
                return string.Empty;
            }

            // Reuses the same Windows command-line quoting rules Kubuno.Rust.Cargo already applies to
            // its own commands (CommandLineToArgvW-compatible); duplicated here in miniature
            // rather than taking a Kubuno.Rust.Cargo dependency from Kubuno.Rust.Launch's call site just
            // for this one helper.
            var builder = new StringBuilder();
            for (var i = 0; i < arguments.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                var argument = arguments[i];
                if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
                {
                    builder.Append(argument);
                }
                else
                {
                    builder.Append('"').Append(argument.Replace("\"", "\\\"")).Append('"');
                }
            }

            return builder.ToString();
        }

        private static string BuildEnvironmentBlock(IReadOnlyDictionary<string, string> environmentVariables)
        {
            // VsDebugTargetInfo4.bstrEnv is a double-NUL-terminated block of "NAME=value\0"
            // entries, mirroring the Win32 CreateProcess environment block - and, like
            // CreateProcess's own lpEnvironment, a *non-empty* block REPLACES the child's
            // environment entirely rather than being layered on top of it. LaunchDescription's
            // own EnvironmentVariables deliberately contains only PATH/RUST_BACKTRACE/explicit
            // overrides (Kubuno.Rust.Launch stays pure and never reads the ambient environment - see
            // RustDebugEnvironment's remarks) - passing just that would start the debuggee
            // missing SystemRoot/TEMP/USERPROFILE/etc. and likely fail outright. So this merges
            // it on top of the *current* (devenv's own) environment here, at the one call site
            // that both has this LaunchDescription and is allowed to read Environment.
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                merged[(string)entry.Key] = (string)(entry.Value ?? string.Empty);
            }
            foreach (var pair in environmentVariables)
            {
                merged[pair.Key] = pair.Value;
            }

            var builder = new StringBuilder();
            foreach (var pair in merged)
            {
                builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
            }

            builder.Append('\0');
            return builder.ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kubuno.Rust.Launch
{
    /// <summary>
    /// The result of asking the toolchain for its sysroot.
    /// </summary>
    public readonly struct SysrootResult
    {
        private SysrootResult(bool succeeded, string? sysroot, string? error)
        {
            Succeeded = succeeded;
            Sysroot = sysroot;
            Error = error;
        }

        public bool Succeeded { get; }

        /// <summary>The sysroot path (e.g. `C:\Users\me\.rustup\toolchains\stable-x86_64-pc-windows-msvc`), when <see cref="Succeeded"/>.</summary>
        public string? Sysroot { get; }

        /// <summary>A diagnostic message, when not <see cref="Succeeded"/>.</summary>
        public string? Error { get; }

        public static SysrootResult Success(string sysroot) => new(true, sysroot, null);

        public static SysrootResult Failure(string error) => new(false, null, error);
    }

    /// <summary>
    /// The result of asking the toolchain for its host triple.
    /// </summary>
    public readonly struct HostTripleResult
    {
        private HostTripleResult(bool succeeded, string? hostTriple, string? error)
        {
            Succeeded = succeeded;
            HostTriple = hostTriple;
            Error = error;
        }

        public bool Succeeded { get; }

        /// <summary>The host triple (e.g. `x86_64-pc-windows-msvc`), when <see cref="Succeeded"/>.</summary>
        public string? HostTriple { get; }

        /// <summary>A diagnostic message, when not <see cref="Succeeded"/>.</summary>
        public string? Error { get; }

        public static HostTripleResult Success(string hostTriple) => new(true, hostTriple, null);

        public static HostTripleResult Failure(string error) => new(false, null, error);
    }

    /// <summary>
    /// Resolves facts about the active Rust toolchain that phase 1c needs: the sysroot
    /// (for the dylib search path and natvis files) and the host triple's std library
    /// directory.
    /// </summary>
    public static class RustToolchain
    {
        /// <summary>
        /// Runs `rustc --print sysroot` through the given <paramref name="processRunner"/>
        /// and returns the trimmed path it prints. Takes the process runner as a parameter
        /// (rather than constructing one) so this stays testable with a fake.
        /// </summary>
        /// <param name="processRunner">How to invoke `rustc`.</param>
        /// <param name="workingDirectory">
        /// The directory to run `rustc` from, so rustup's directory/`rust-toolchain.toml`
        /// override picks the right toolchain. Pass the workspace or package root.
        /// </param>
        public static SysrootResult GetSysroot(IProcessRunner processRunner, string? workingDirectory = null)
        {
            if (processRunner is null)
            {
                throw new ArgumentNullException(nameof(processRunner));
            }

            ProcessRunResult result;
            try
            {
                result = processRunner.Run("rustc", "--print sysroot", workingDirectory);
            }
            catch (Exception ex)
            {
                return SysrootResult.Failure($"Failed to run 'rustc --print sysroot': {ex.Message}");
            }

            if (!result.Succeeded)
            {
                var stderr = result.StandardError.Trim();
                return SysrootResult.Failure(
                    string.IsNullOrEmpty(stderr)
                        ? $"'rustc --print sysroot' exited with code {result.ExitCode}."
                        : $"'rustc --print sysroot' exited with code {result.ExitCode}: {stderr}");
            }

            var sysroot = result.StandardOutput.Trim();
            if (sysroot.Length == 0)
            {
                return SysrootResult.Failure("'rustc --print sysroot' produced no output.");
            }

            return SysrootResult.Success(sysroot);
        }

        /// <summary>
        /// Runs `rustc -vV` (verbose version) through the given <paramref name="processRunner"/>
        /// and extracts the `host: &lt;triple&gt;` line — the triple `RustDebugEnvironment` needs to
        /// locate the host standard library's dylib directory (see
        /// <see cref="GetTargetLibDir"/>) when the target isn't cross-compiled. Kept as a
        /// separate call from <see cref="GetSysroot"/> (rather than folded into one `rustc`
        /// invocation) because the two outputs are parsed independently and callers may only
        /// need one of them.
        /// </summary>
        /// <param name="processRunner">How to invoke `rustc`.</param>
        /// <param name="workingDirectory">
        /// The directory to run `rustc` from, so rustup's directory/`rust-toolchain.toml`
        /// override picks the right toolchain. Pass the workspace or package root.
        /// </param>
        public static HostTripleResult GetHostTriple(IProcessRunner processRunner, string? workingDirectory = null)
        {
            if (processRunner is null)
            {
                throw new ArgumentNullException(nameof(processRunner));
            }

            ProcessRunResult result;
            try
            {
                result = processRunner.Run("rustc", "-vV", workingDirectory);
            }
            catch (Exception ex)
            {
                return HostTripleResult.Failure($"Failed to run 'rustc -vV': {ex.Message}");
            }

            if (!result.Succeeded)
            {
                var stderr = result.StandardError.Trim();
                return HostTripleResult.Failure(
                    string.IsNullOrEmpty(stderr)
                        ? $"'rustc -vV' exited with code {result.ExitCode}."
                        : $"'rustc -vV' exited with code {result.ExitCode}: {stderr}");
            }

            var hostTriple = ParseHostTriple(result.StandardOutput);
            if (hostTriple is null)
            {
                return HostTripleResult.Failure("'rustc -vV' output did not contain a 'host:' line.");
            }

            return HostTripleResult.Success(hostTriple);
        }

        /// <summary>
        /// Extracts the triple from a `host: &lt;triple&gt;` line in `rustc -vV`'s output (which
        /// also includes `rustc`/`binary`/`commit-hash`/`release`/`LLVM version` lines this
        /// doesn't care about). Pure string parsing, split out from <see cref="GetHostTriple"/>
        /// so it's directly testable against captured `rustc -vV` output with no process runner
        /// involved. Returns null when no `host:` line is present.
        /// </summary>
        public static string? ParseHostTriple(string rustcVerboseVersionOutput)
        {
            if (string.IsNullOrEmpty(rustcVerboseVersionOutput))
            {
                return null;
            }

            const string prefix = "host:";
            foreach (var rawLine in rustcVerboseVersionOutput.Split('\n'))
            {
                var line = rawLine.Trim('\r', ' ', '\t');
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    var triple = line.Substring(prefix.Length).Trim();
                    if (triple.Length > 0)
                    {
                        return triple;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// The std/core rlib directory for a given host/target triple under a sysroot:
        /// `&lt;sysroot&gt;/lib/rustlib/&lt;triple&gt;/lib`. This is where a `-C prefer-dynamic`
        /// build's `std-*.dll` lives, so it must be on PATH for the debuggee to start.
        /// </summary>
        public static string GetTargetLibDir(string sysroot, string triple)
        {
            if (string.IsNullOrEmpty(sysroot))
            {
                throw new ArgumentException("Sysroot must not be null or empty.", nameof(sysroot));
            }

            if (string.IsNullOrEmpty(triple))
            {
                throw new ArgumentException("Triple must not be null or empty.", nameof(triple));
            }

            return Path.Combine(sysroot, "lib", "rustlib", triple, "lib");
        }

        /// <summary>
        /// The directory rustup/rustc ships the standard library's `.natvis` files in:
        /// `&lt;sysroot&gt;/lib/rustlib/etc`. Doesn't touch disk — see
        /// <see cref="FindNatvisFiles"/> to actually list what's there.
        /// </summary>
        public static string GetNatvisDirectory(string sysroot)
        {
            if (string.IsNullOrEmpty(sysroot))
            {
                throw new ArgumentException("Sysroot must not be null or empty.", nameof(sysroot));
            }

            return Path.Combine(sysroot, "lib", "rustlib", "etc");
        }

        /// <summary>
        /// Lists the `.natvis` files actually present under <see cref="GetNatvisDirectory"/>
        /// (e.g. `liballoc.natvis`, `libcore.natvis`, `libstd.natvis`, `intrinsic.natvis` on
        /// a current stable toolchain). Returns an empty list if the directory doesn't
        /// exist rather than throwing, since natvis is a debugging nicety, not something
        /// that should block a launch.
        /// </summary>
        public static IReadOnlyList<string> FindNatvisFiles(string sysroot)
        {
            var directory = GetNatvisDirectory(sysroot);

            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(directory, "*.natvis").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}

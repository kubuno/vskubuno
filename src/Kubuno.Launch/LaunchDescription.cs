using System;
using System.Collections.Generic;

namespace Kubuno.Launch
{
    /// <summary>
    /// A fully-resolved description of how to launch and debug one Rust target, independent
    /// of how the VSIX ends up starting it (Open Folder's `launch.vs.json`, or a direct
    /// `IVsDebugger4` launch — see `docs/ARCHITECTURE.md` phase 1c). Deliberately a plain
    /// mutable class with a parameterless constructor and settable properties, not a
    /// record: the VSIX will (de)serialize this with whatever JSON stack it already uses
    /// (VS SDK ships Newtonsoft.Json; a future MCP server layer may use
    /// System.Text.Json) — a plain POCO round-trips through either with no extra attributes.
    /// </summary>
    public sealed class LaunchDescription
    {
        /// <summary>Display name for the debug target (e.g. the Cargo target name).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The full path to the executable to debug.</summary>
        public string ExecutablePath { get; set; } = string.Empty;

        /// <summary>Command-line arguments passed to the executable.</summary>
        public IReadOnlyList<string> Arguments { get; set; } = Array.Empty<string>();

        /// <summary>The debuggee's working directory.</summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>Environment variables to set for the debuggee (merged over the inherited environment by the launcher).</summary>
        public IReadOnlyDictionary<string, string> EnvironmentVariables { get; set; } =
            new Dictionary<string, string>();

        /// <summary>
        /// Natvis files the debugger should load for this session (see
        /// <see cref="RustToolchain.FindNatvisFiles"/>). Informational for
        /// `launch.vs.json` generation — see the remarks on <see cref="LaunchVsJsonWriter"/>
        /// for why this list isn't emitted as a `launch.vs.json` field — but consumed
        /// directly by a VSIX-driven `IVsDebugger4` launch, which can register them with
        /// the debug engine itself.
        /// </summary>
        public IReadOnlyList<string> NatvisFiles { get; set; } = Array.Empty<string>();

        /// <summary>Always "native" for phase 1c: the VS native (MSVC/PDB) debug engine, never a managed or script engine.</summary>
        public string Debugger { get; set; } = "native";
    }
}

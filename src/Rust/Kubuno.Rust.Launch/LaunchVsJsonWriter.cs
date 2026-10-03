using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Kubuno.Rust.Launch
{
    /// <summary>
    /// Generates `launch.vs.json` configuration text for Visual Studio's Open Folder native
    /// debugger, so a target can be debugged with plain VS (no extension) as well as via
    /// the VSIX's own `IVsDebugger4` launch path.
    ///
    /// Schema facts this relies on (see the launch report for full sourcing):
    /// - Right-clicking an .exe in Solution Explorer → "Add Debug Configuration" generates
    ///   `{ "version": "0.2.1", "defaults": {}, "configurations": [ { "type": "default",
    ///   "project": "bin\\hello.exe", "projectTarget": "", "name": "hello.exe" } ] }` — the
    ///   `"type": "default"` configuration is VS's own native-exe-launch shape for a
    ///   project-less Open Folder codebase (source: "Create build and debug tasks with JSON
    ///   files", learn.microsoft.com/visualstudio/ide/customize-build-and-debug-tasks-in-visual-studio).
    /// - The "Default properties" table of the `launch.vs.json` schema reference documents
    ///   `args` (array), `currentDir` (string — local working directory; auto-detected
    ///   unless set), `env`, `name`, `project`, `projectTarget` (source:
    ///   learn.microsoft.com/cpp/build/launch-vs-schema-reference-cpp, "Default properties").
    ///   `cwd` is documented separately and is for *remote* debugging only, so this writer
    ///   uses `currentDir`, not `cwd`, for the local working directory.
    /// - `env` for a `"type": "default"` configuration is a plain `{ "VAR": "value", ... }`
    ///   object, not an array - confirmed live the hard way: an earlier version of this writer
    ///   emitted the array-of-`{name, value}` shape the same schema page's C++ Linux
    ///   `environment` property (and real-world CMake `launch.vs.json` files) use, which VS's
    ///   native debug engine silently ignores for `"type": "default"` (no error, no warning -
    ///   the configuration parses fine, PATH is just never extended, and the launched exe dies
    ///   immediately with a `STATUS_DLL_NOT_FOUND` dialog for a `-C prefer-dynamic` build's
    ///   `std-*.dll`). `${env.VAR}` interpolation (e.g. `"PATH": "C:\\a;C:\\b;${env.PATH}"`) is
    ///   how a value is appended to whatever the debuggee would otherwise inherit - see
    ///   <see cref="RustDebugEnvironment"/>'s own remarks on why the VSIX's launch.vs.json
    ///   generator passes that literal token as `existingPath` rather than a snapshot of the
    ///   extension's own process environment.
    /// - No `natvisFile`/`visualizerFile`-equivalent field is documented for a local Windows
    ///   `"type": "default"` configuration: `visualizerFile` only appears in that schema
    ///   page's "C++ Linux properties" table (gdb/lldb remote debugging), not in "Default
    ///   properties". So this writer does not emit a natvis field — see
    ///   <see cref="LaunchDescription.NatvisFiles"/> and the launch report for the two real
    ///   mechanisms (PDB-embedded std natvis; dropping files into VS's own Natvis
    ///   directories, or shipping them as VSIX-registered assets, documented at
    ///   learn.microsoft.com/visualstudio/debugger/create-custom-views-of-native-objects).
    /// </summary>
    public static class LaunchVsJsonWriter
    {
        private const string SchemaVersion = "0.2.1";

        /// <summary>
        /// Writes one `configurations[]` entry (a standalone JSON object, not wrapped in a
        /// file) for the given <paramref name="description"/>.
        /// </summary>
        /// <param name="description">The resolved launch description.</param>
        /// <param name="projectPath">
        /// The `project` field: VS's own generator uses a path relative to the workspace
        /// root (e.g. `bin\hello.exe`); pass <see cref="LaunchDescription.ExecutablePath"/>
        /// itself if a relative path isn't available — an absolute path works too.
        /// </param>
        /// <param name="indent">The base indentation, in spaces, of the returned object (0 = flush left).</param>
        public static string WriteConfigurationEntry(LaunchDescription description, string projectPath, int indent = 0)
        {
            if (description is null)
            {
                throw new ArgumentNullException(nameof(description));
            }

            if (string.IsNullOrEmpty(projectPath))
            {
                throw new ArgumentException("Project path must not be null or empty.", nameof(projectPath));
            }

            var sb = new StringBuilder();
            WriteConfigurationObject(sb, description, projectPath, indent);
            return sb.ToString();
        }

        /// <summary>
        /// Writes a complete `launch.vs.json` file with one configuration per target.
        /// </summary>
        public static string WriteFile(IEnumerable<(LaunchDescription Description, string ProjectPath)> targets)
        {
            if (targets is null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            var list = targets.ToList();

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"version\": ").Append(JsonString(SchemaVersion)).Append(",\n");
            sb.Append("  \"defaults\": {},\n");
            sb.Append("  \"configurations\": [\n");

            for (var i = 0; i < list.Count; i++)
            {
                WriteConfigurationObject(sb, list[i].Description, list[i].ProjectPath, indent: 4);
                sb.Append(i < list.Count - 1 ? ",\n" : "\n");
            }

            sb.Append("  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static void WriteConfigurationObject(StringBuilder sb, LaunchDescription description, string projectPath, int indent)
        {
            var pad = new string(' ', indent);
            var innerPad = new string(' ', indent + 2);

            sb.Append(pad).Append("{\n");
            sb.Append(innerPad).Append("\"type\": \"default\",\n");
            sb.Append(innerPad).Append("\"project\": ").Append(JsonString(projectPath)).Append(",\n");
            sb.Append(innerPad).Append("\"projectTarget\": \"\",\n");
            sb.Append(innerPad).Append("\"name\": ").Append(JsonString(description.Name));

            if (description.Arguments.Count > 0)
            {
                sb.Append(",\n").Append(innerPad).Append("\"args\": ");
                WriteStringArray(sb, description.Arguments, indent + 2);
            }

            sb.Append(",\n").Append(innerPad).Append("\"currentDir\": ").Append(JsonString(description.WorkingDirectory));

            if (description.EnvironmentVariables.Count > 0)
            {
                sb.Append(",\n").Append(innerPad).Append("\"env\": {\n");
                var entries = description.EnvironmentVariables.ToList();
                var entryPad = new string(' ', indent + 4);
                for (var i = 0; i < entries.Count; i++)
                {
                    sb.Append(entryPad).Append(JsonString(entries[i].Key)).Append(": ").Append(JsonString(entries[i].Value));
                    sb.Append(i < entries.Count - 1 ? ",\n" : "\n");
                }
                sb.Append(innerPad).Append('}');
            }

            sb.Append('\n').Append(pad).Append('}');
        }

        private static void WriteStringArray(StringBuilder sb, IReadOnlyList<string> values, int indent)
        {
            if (values.Count == 0)
            {
                sb.Append("[]");
                return;
            }

            sb.Append("[ ");
            for (var i = 0; i < values.Count; i++)
            {
                sb.Append(JsonString(values[i]));
                if (i < values.Count - 1)
                {
                    sb.Append(", ");
                }
            }
            sb.Append(" ]");
        }

        private static string JsonString(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}

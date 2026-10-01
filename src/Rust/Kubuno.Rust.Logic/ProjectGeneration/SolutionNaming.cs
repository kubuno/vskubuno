using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kubuno.Rust.Logic.ProjectGeneration
{
    /// <summary>
    /// The name of a NEW solution created by "Generate Visual Studio Projects" (an existing one is always reused). The
    /// Kubuno repositories follow one rule so the two cores are never confused: <c>Kubuno.Core.Web.slnx</c> for the web
    /// server (the <c>core</c> repository, <c>crates/kubuno-core</c>), <c>Kubuno.Core.Desktop.slnx</c> for the desktop
    /// workspace (<c>desktop/windows</c>, <c>src/crates/kubuno-ui</c>), <c>Kubuno.&lt;Module&gt;.slnx</c> for a module
    /// (<c>module.toml</c>); any other workspace is named after its folder.
    /// </summary>
    public static class SolutionNaming
    {
        public static string DefaultSolutionFileName(string workspaceRoot)
        {
            if (File.Exists(Path.Combine(workspaceRoot, "crates", "kubuno-core", "Cargo.toml")))
            {
                return "Kubuno.Core.Web.slnx";
            }

            if (File.Exists(Path.Combine(workspaceRoot, "src", "crates", "kubuno-ui", "Cargo.toml")))
            {
                return "Kubuno.Core.Desktop.slnx";
            }

            var moduleToml = Path.Combine(workspaceRoot, "module.toml");
            if (File.Exists(moduleToml))
            {
                var id = Regex.Match(File.ReadAllText(moduleToml), "^\\s*id\\s*=\\s*\"(?<id>[^\"]+)\"", RegexOptions.Multiline).Groups["id"].Value;
                if (id.Length > 0)
                {
                    return "Kubuno." + string.Concat(id.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1))) + ".slnx";
                }
            }

            return new DirectoryInfo(workspaceRoot).Name + ".slnx";
        }
    }
}

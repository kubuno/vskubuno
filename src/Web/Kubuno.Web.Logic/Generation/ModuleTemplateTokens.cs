using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Kubuno.Web.Logic.Generation
{
    /// <summary>
    /// The tokens of the "Kubuno Web Module" template (docs/WEB.md, "The module template"), computed by
    /// Kubuno.Web.TemplateWizard (which compiles this file as source) and by tools/test-templates.ps1 (same rules).
    /// </summary>
#if KUBUNO_SHARED_AS_SOURCE
    internal
#else
    public
#endif
    static class ModuleTemplateTokens
    {
        /// <summary>The published <c>@kubuno/*</c> versions when this extension was built (core/frontend/packages).</summary>
        public const string UiVersion = "0.1.12";

        public const string SdkVersion = "0.1.10";

        public const string DriveVersion = "0.1.7";

        /// <summary>The latest shared crate tags of the core repository when this extension was built.</summary>
        public const string SeccompTag = "seccomp-v0.1.1";

        public const string DbTag = "db-v0.9.0";

        /// <summary>
        /// The module id from the project name: lower case letters and digits only (the id is a folder name, a
        /// PostgreSQL schema, a URL segment and part of the executable's name), starting with a letter.
        /// <c>Inventory</c> gives <c>inventory</c>, <c>My Notes 2</c> <c>mynotes2</c>.
        /// </summary>
        public static string ModuleId(string? projectName)
        {
            var builder = new StringBuilder();
            foreach (var c in (projectName ?? string.Empty).ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    builder.Append(c);
                }
            }

            var id = builder.ToString();
            if (id.Length == 0)
            {
                return "module";
            }

            return id[0] >= 'a' && id[0] <= 'z' ? id : "m" + id;
        }

        /// <summary>All the tokens. <paramref name="coreRepository"/>: a core checkout to read the current <c>@kubuno/*</c> versions from (optional).</summary>
        public static Dictionary<string, string> Build(string? projectName, string? coreRepository)
        {
            var id = ModuleId(projectName);
            var title = string.IsNullOrWhiteSpace(projectName) ? id : projectName!.Trim();
            return new Dictionary<string, string>
            {
                ["$moduleid$"] = id,
                ["$cratename$"] = "kubuno-" + id,
                ["$moduletitle$"] = Regex.Replace(title, "[\"\\\\<>&$]", string.Empty),
                ["$kubunouiversion$"] = PackageVersion(coreRepository, "ui") ?? UiVersion,
                ["$kubunosdkversion$"] = PackageVersion(coreRepository, "sdk") ?? SdkVersion,
                ["$kubunodriveversion$"] = PackageVersion(coreRepository, "drive") ?? DriveVersion,
                ["$seccomptag$"] = SeccompTag,
                ["$dbtag$"] = DbTag,
            };
        }

        /// <summary>The core repository next to <paramref name="directory"/> or one of its parents (the polyrepo layout), or null.</summary>
        public static string? FindCoreRepository(string? directory)
        {
            for (var current = directory; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            {
                var core = Path.Combine(current!, "core");
                if (File.Exists(Path.Combine(core, "frontend", "packages", "sdk", "package.json")))
                {
                    return core;
                }
            }

            return null;
        }

        private static string? PackageVersion(string? coreRepository, string library)
        {
            if (string.IsNullOrEmpty(coreRepository))
            {
                return null;
            }

            try
            {
                var path = Path.Combine(coreRepository!, "frontend", "packages", library, "package.json");
                if (!File.Exists(path))
                {
                    return null;
                }

                var match = Regex.Match(File.ReadAllText(path), "\"version\"\\s*:\\s*\"(?<v>\\d+\\.\\d+\\.\\d+)\"");
                return match.Success ? match.Groups["v"].Value : null;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}

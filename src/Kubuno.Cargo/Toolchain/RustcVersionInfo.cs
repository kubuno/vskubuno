using System;
using System.Collections.Generic;

namespace Kubuno.Cargo.Toolchain
{
    /// <summary>
    /// The toolchain a package builds with, from <c>rustc -vV</c> (run in the package directory, so a
    /// <c>rust-toolchain.toml</c> override applies) - what the Dependencies node shows under
    /// "Toolchain", the Cargo analogue of a .NET project's "Frameworks".
    /// </summary>
    public sealed class RustcVersionInfo
    {
        /// <summary>e.g. <c>1.98.1</c>, <c>1.99.0-beta.3</c>, <c>1.100.0-nightly</c>.</summary>
        public string Release { get; set; } = string.Empty;

        /// <summary>The target triple rustc builds for by default, e.g. <c>x86_64-pc-windows-msvc</c>.</summary>
        public string Host { get; set; } = string.Empty;

        public string? CommitHash { get; set; }

        public string? CommitDate { get; set; }

        public string? LlvmVersion { get; set; }

        /// <summary>The sysroot (<c>rustc --print sysroot</c>), filled in separately; <see langword="null"/> when unknown.</summary>
        public string? Sysroot { get; set; }

        /// <summary><c>stable</c>, <c>beta</c>, <c>nightly</c> or <c>dev</c>, from the release string.</summary>
        public string Channel
        {
            get
            {
                if (Release.IndexOf("-nightly", StringComparison.Ordinal) >= 0)
                {
                    return "nightly";
                }

                if (Release.IndexOf("-beta", StringComparison.Ordinal) >= 0)
                {
                    return "beta";
                }

                return Release.IndexOf("-dev", StringComparison.Ordinal) >= 0 ? "dev" : "stable";
            }
        }

        /// <summary>The release without its pre-release suffix: <c>1.100.0</c> for <c>1.100.0-nightly</c>.</summary>
        public string Version
        {
            get
            {
                var dash = Release.IndexOf('-');
                return dash < 0 ? Release : Release.Substring(0, dash);
            }
        }

        /// <summary>
        /// Parses <c>rustc -vV</c>: a <c>rustc 1.98.1 (48a229cea 2026-09-01)</c> banner, then
        /// <c>key: value</c> lines (<c>host</c>, <c>release</c>, <c>commit-hash</c>, <c>commit-date</c>,
        /// <c>LLVM version</c>). Returns <see langword="null"/> when neither the release nor the host is found.
        /// </summary>
        public static RustcVersionInfo? Parse(IEnumerable<string> lines)
        {
            if (lines is null)
            {
                throw new ArgumentNullException(nameof(lines));
            }

            var info = new RustcVersionInfo();
            foreach (var raw in lines)
            {
                var line = raw?.Trim() ?? string.Empty;
                if (line.StartsWith("rustc ", StringComparison.Ordinal) && string.IsNullOrEmpty(info.Release))
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                    {
                        info.Release = parts[1];
                    }

                    continue;
                }

                var colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, colon).Trim();
                var value = line.Substring(colon + 1).Trim();
                switch (key)
                {
                    case "release":
                        info.Release = value;
                        break;
                    case "host":
                        info.Host = value;
                        break;
                    case "commit-hash":
                        info.CommitHash = value == "unknown" ? null : value;
                        break;
                    case "commit-date":
                        info.CommitDate = value == "unknown" ? null : value;
                        break;
                    case "LLVM version":
                        info.LlvmVersion = value;
                        break;
                }
            }

            return string.IsNullOrEmpty(info.Release) && string.IsNullOrEmpty(info.Host) ? null : info;
        }
    }
}

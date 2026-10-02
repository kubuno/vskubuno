using System;
using System.Collections.Generic;
using System.IO;
using Kubuno.Views.Locating;

namespace Kubuno.Views.Infrastructure
{
    /// <summary>
    /// The real, VS-independent implementation of <see cref="IKubunoViewsLanguageServerEnvironment"/>:
    /// actual file system checks, the actual PATH variable, and the two fixed local dev build output
    /// folders (see CLAUDE.md's <c>CARGO_TARGET_DIR=C:\kubuno-build\desktop-target</c> convention for
    /// the desktop workspace, and <c>kubuno-views-ls</c>'s own dev build target). Kept separate from
    /// <see cref="KubunoViewsLanguageServerLocator"/> so that locator's decision logic stays
    /// unit-testable against a fake instead of the real machine.
    /// </summary>
    public sealed class RealKubunoViewsLanguageServerEnvironment : IKubunoViewsLanguageServerEnvironment
    {
        private static readonly string[] FixedDevBuildDirectories =
        {
            @"C:\kubuno-build\desktop-target\debug",
            @"C:\kubuno-build\agent-views-ls\debug",
        };

        public IEnumerable<string> PathDirectories
        {
            get
            {
                var pathVariable = Environment.GetEnvironmentVariable("PATH");
                if (string.IsNullOrEmpty(pathVariable))
                {
                    yield break;
                }

                foreach (var directory in pathVariable!.Split(Path.PathSeparator))
                {
                    yield return directory;
                }
            }
        }

        public IEnumerable<string> DevBuildDirectories => FixedDevBuildDirectories;

        public bool FileExists(string path)
        {
            try
            {
                return File.Exists(path);
            }
            catch (Exception exception) when (exception is ArgumentException or PathTooLongException or NotSupportedException)
            {
                // A malformed candidate path (illegal characters coming from a PATH entry, for
                // example) must not abort the search; treat it as "not found" and move on.
                return false;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kubuno.Cargo.Diagnostics;
using Kubuno.Cargo.Metadata;

namespace Kubuno.Cargo.DesignSurface
{
    /// <summary>
    /// What the design surface is compiled from, picked out of the project build's
    /// <c>compiler-artifact</c> messages (docs/DESIGNER.md section 15): the project's own
    /// <c>kubuno_ui.dll</c>, the <c>kubuno_views</c>/<c>kubuno_controls</c> rlibs its graph built (the
    /// exact variants - a <c>deps</c> folder can hold several hashes of the same crate), the surface
    /// source that ships with that very <c>kubuno-views</c> (<c>examples/view_embed.rs</c>), and the
    /// <c>deps</c> folder for every transitive crate.
    /// </summary>
    public sealed class DesignSurfaceInputs
    {
        public const string UiCrate = "kubuno_ui";
        public const string ViewsCrate = "kubuno_views";
        public const string ControlsCrate = "kubuno_controls";

        /// <summary>The surface's source, relative to the <c>kubuno-views</c> package folder.</summary>
        public static readonly string SurfaceSourceRelativePath = Path.Combine("examples", "view_embed.rs");

        private DesignSurfaceInputs(string uiDll, string viewsRlib, string controlsRlib, string depsDirectory, string surfaceSource)
        {
            UiDll = uiDll;
            ViewsRlib = viewsRlib;
            ControlsRlib = controlsRlib;
            DepsDirectory = depsDirectory;
            SurfaceSource = surfaceSource;
        }

        /// <summary>The project's <c>kubuno_ui.dll</c> (its <c>deps</c> copy when present: the file rustc linked).</summary>
        public string UiDll { get; }

        public string ViewsRlib { get; }

        public string ControlsRlib { get; }

        /// <summary><c>&lt;target dir&gt;\&lt;profile&gt;\deps</c>.</summary>
        public string DepsDirectory { get; }

        /// <summary><c>&lt;target dir&gt;\&lt;profile&gt;</c>.</summary>
        public string ProfileDirectory => Path.GetDirectoryName(DepsDirectory) ?? DepsDirectory;

        /// <summary><c>&lt;target dir&gt;</c>.</summary>
        public string TargetDirectory => Path.GetDirectoryName(ProfileDirectory) ?? ProfileDirectory;

        public string SurfaceSource { get; }

        /// <summary>The <c>--extern</c> crates the surface uses, in a stable order.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Externs => new[]
        {
            new KeyValuePair<string, string>(ControlsCrate, ControlsRlib),
            new KeyValuePair<string, string>(UiCrate, UiDll),
            new KeyValuePair<string, string>(ViewsCrate, ViewsRlib),
        };

        /// <summary>
        /// Picks the inputs out of <paramref name="artifacts"/>. <see langword="null"/> with a user-facing
        /// reason when the project does not use <c>kubuno-views</c>, an input was not built, or this
        /// <c>kubuno-views</c> ships no design surface. <paramref name="fileExists"/> is injectable for tests.
        /// </summary>
        public static DesignSurfaceInputs? From(IEnumerable<CargoArtifact> artifacts, Func<string, bool> fileExists, out string? reason)
        {
            if (artifacts is null)
            {
                throw new ArgumentNullException(nameof(artifacts));
            }

            var list = artifacts.ToList();
            var views = Library(list, ViewsCrate, CargoTargetKind.Lib, ".rlib");
            if (views is null)
            {
                reason = "the project does not depend on kubuno-views (or it was not built)";
                return null;
            }

            var controls = Library(list, ControlsCrate, CargoTargetKind.Lib, ".rlib");
            var ui = Library(list, UiCrate, "dylib", ".dll");
            if (controls is null || ui is null)
            {
                reason = "kubuno-ui or kubuno-controls was not built with the project";
                return null;
            }

            var depsDirectory = Path.GetDirectoryName(views.Value.File)!;
            // Cargo reports the dylib's uplifted copy (<profile>\kubuno_ui.dll); rustc linked the deps one.
            var depsUi = Path.Combine(depsDirectory, Path.GetFileName(ui.Value.File));
            var uiDll = fileExists(depsUi) ? depsUi : ui.Value.File;

            var viewsPackageDirectory = Path.GetDirectoryName(Path.GetDirectoryName(views.Value.Target.SrcPath) ?? string.Empty);
            if (string.IsNullOrEmpty(viewsPackageDirectory))
            {
                reason = "kubuno-views' source folder is unknown";
                return null;
            }

            var source = Path.Combine(viewsPackageDirectory, SurfaceSourceRelativePath);
            if (!fileExists(source))
            {
                reason = $"this kubuno-views has no design surface ({source} is missing)";
                return null;
            }

            reason = null;
            return new DesignSurfaceInputs(uiDll, views.Value.File, controls.Value.File, depsDirectory, source);
        }

        private static (string File, CargoTarget Target)? Library(List<CargoArtifact> artifacts, string crateName, string kind, string extension)
        {
            (string File, CargoTarget Target)? found = null;
            foreach (var artifact in artifacts)
            {
                if (!string.Equals(artifact.Target.Name, crateName, StringComparison.Ordinal) || !artifact.Target.IsKind(kind))
                {
                    continue;
                }

                var file = artifact.Filenames.FirstOrDefault(f => f.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
                if (file is not null)
                {
                    // The last report wins (a unit is reported once per build; later lines are newer).
                    found = (file, artifact.Target);
                }
            }

            return found;
        }
    }
}

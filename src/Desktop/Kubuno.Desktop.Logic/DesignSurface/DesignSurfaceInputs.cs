using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kubuno.Rust.Cargo.Diagnostics;
using Kubuno.Rust.Cargo.Metadata;

namespace Kubuno.Desktop.Logic.DesignSurface
{
    /// <summary>
    /// What the design surface is compiled from, picked out of the project build's
    /// <c>compiler-artifact</c> messages (docs/DESIGNER.md section 15): the <c>kubuno_ui</c>,
    /// <c>kubuno_views</c> and <c>kubuno_controls</c> rlibs the project's graph built (the exact variants -
    /// a <c>deps</c> folder can hold several hashes of the same crate), the surface source that ships with
    /// that very <c>kubuno-views</c> (<c>examples/view_embed.rs</c>), and the <c>deps</c> folder for every
    /// transitive crate. Everything is linked statically into the surface (docs/DESIGNER.md section 16).
    /// </summary>
    public sealed class DesignSurfaceInputs
    {
        public const string UiCrate = "kubuno_ui";
        public const string ViewsCrate = "kubuno_views";
        public const string ControlsCrate = "kubuno_controls";

        /// <summary>The surface's source, relative to the <c>kubuno-views</c> package folder.</summary>
        public static readonly string SurfaceSourceRelativePath = Path.Combine("examples", "view_embed.rs");

        private DesignSurfaceInputs(string uiRlib, string viewsRlib, string controlsRlib, string depsDirectory, string surfaceSource)
        {
            UiRlib = uiRlib;
            ViewsRlib = viewsRlib;
            ControlsRlib = controlsRlib;
            DepsDirectory = depsDirectory;
            SurfaceSource = surfaceSource;
        }

        /// <summary>The project's <c>libkubuno_ui-&lt;hash&gt;.rlib</c>.</summary>
        public string UiRlib { get; }

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
            new KeyValuePair<string, string>(UiCrate, UiRlib),
            new KeyValuePair<string, string>(ViewsCrate, ViewsRlib),
        };

        /// <summary>
        /// Picks the inputs out of <paramref name="artifacts"/>. <see langword="null"/> with a user-facing
        /// reason when the project does not use <c>kubuno-views</c>, an input was not built, the project's
        /// <c>kubuno-ui</c> is still a Rust dylib (a desktop checkout older than the static-linking change),
        /// or this <c>kubuno-views</c> ships no design surface. <paramref name="fileExists"/> is injectable for tests.
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
            var ui = Library(list, UiCrate, CargoTargetKind.Lib, ".rlib");
            if (ui is null && Library(list, UiCrate, CargoTargetKind.Dylib, ".dll") is not null)
            {
                reason = "this kubuno-ui is built as a Rust dylib (kubuno_ui.dll), which the designer no longer supports: update the desktop checkout (kubuno-ui is linked statically since 2026-10-03)";
                return null;
            }

            if (controls is null || ui is null)
            {
                reason = "kubuno-ui or kubuno-controls was not built with the project";
                return null;
            }

            var depsDirectory = Path.GetDirectoryName(views.Value.File)!;
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
            return new DesignSurfaceInputs(ui.Value.File, views.Value.File, controls.Value.File, depsDirectory, source);
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

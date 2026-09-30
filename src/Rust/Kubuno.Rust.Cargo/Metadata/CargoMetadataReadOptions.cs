using System;

namespace Kubuno.Rust.Cargo.Metadata
{
    /// <summary>What <see cref="CargoMetadataReader"/> asks <c>cargo metadata</c> for.</summary>
    [Flags]
    public enum CargoMetadataReadOptions
    {
        /// <summary><c>--no-deps</c>: the workspace's own packages only, no resolve graph (fast, never touches the network).</summary>
        NoDependencies = 0,

        /// <summary>The full package list and the resolve graph (<see cref="CargoMetadata.Resolve"/>).</summary>
        IncludeDependencies = 1,

        /// <summary><c>--offline</c>: fail rather than update the index or download missing packages.</summary>
        Offline = 2,
    }
}

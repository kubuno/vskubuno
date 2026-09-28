namespace Kubuno.Cargo.Metadata
{
    /// <summary>
    /// One entry of a package's <c>dependencies</c> array in <c>cargo metadata</c> - what the
    /// package's own <c>Cargo.toml</c> declares (<c>[dependencies]</c>, <c>[dev-dependencies]</c>,
    /// <c>[build-dependencies]</c>, target-specific tables included), not the resolved graph, so it
    /// is available from <c>--no-deps</c> output.
    /// </summary>
    public sealed class CargoDependency
    {
        /// <summary>The depended-on package's name (its real name, even when renamed).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The version requirement, e.g. <c>"^1.0"</c>; <c>"*"</c> for a bare path dependency.</summary>
        public string Req { get; set; } = string.Empty;

        /// <summary><see langword="null"/> for a normal dependency, else <c>"dev"</c> or <c>"build"</c>.</summary>
        public string? Kind { get; set; }

        /// <summary>The local name the package is imported under, when renamed with <c>package = "..."</c>.</summary>
        public string? Rename { get; set; }

        public bool Optional { get; set; }

        /// <summary>The <c>cfg(...)</c>/triple of a target-specific dependency table, if any.</summary>
        public string? Target { get; set; }

        /// <summary>Absolute path of a path dependency, else <see langword="null"/>.</summary>
        public string? Path { get; set; }

        /// <summary>Registry/git source, e.g. <c>"registry+https://github.com/rust-lang/crates.io-index"</c>; <see langword="null"/> for a path dependency.</summary>
        public string? Source { get; set; }

        public override string ToString() => $"{Name} {Req}";
    }
}

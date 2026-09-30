namespace Kubuno.Rust.TestAdapter
{
    /// <summary>Identifiers shared between discovery, execution and container discovery.</summary>
    public static class KubunoTestAdapterConstants
    {
        /// <summary>
        /// This adapter's executor URI. Registered on <see cref="Execution.KubunoTestExecutor"/>
        /// via <c>ExtensionUriAttribute</c> and on <see cref="Discovery.KubunoTestDiscoverer"/>
        /// via <c>DefaultExecutorUriAttribute</c> - every <c>TestCase</c> this adapter produces
        /// carries it as <c>TestCase.ExecutorUri</c>, which is how VSTest routes a run back to
        /// <see cref="Execution.KubunoTestExecutor"/>.
        /// </summary>
        public const string ExecutorUriString = "executor://kubuno.testadapter/v1";

        /// <summary>
        /// The file extension <see cref="Discovery.KubunoTestDiscoverer"/> is registered for.
        /// Containers are Cargo.toml manifest paths (see docs in Containers/), so this is the
        /// only extension the adapter ever needs to claim.
        /// </summary>
        public const string ManifestFileExtension = ".toml";
    }
}

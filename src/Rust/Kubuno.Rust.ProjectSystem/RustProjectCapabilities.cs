namespace Kubuno.Rust.ProjectSystem
{
    /// <summary>
    /// Project capabilities of a <c>.rsproj</c>, declared as <c>ProjectCapability</c> items by
    /// Kubuno.Rust.Sdk's <c>Sdk.targets</c> (docs/RSPROJ.md §3).
    /// </summary>
    public static class RustProjectCapabilities
    {
        /// <summary>
        /// Identifies a <c>.rsproj</c> (the counterpart of the JavaScript project system's
        /// <c>JSProjectSystem</c>); every CPS export of this assembly applies only to it.
        /// </summary>
        public const string RustProjectSystem = "RustProjectSystem";
    }
}

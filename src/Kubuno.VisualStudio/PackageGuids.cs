namespace Kubuno.VisualStudio
{
    /// <summary>
    /// GUIDs the package's registration attributes need that belong to no single layer. The layers' own identifiers
    /// (tool windows, command IDs, UI contexts) are in <c>Kubuno.Shared.KubunoGuids</c>, <c>Kubuno.Rust.PackageGuids</c>
    /// and <c>Kubuno.Desktop.PackageGuids</c>.
    /// </summary>
    internal static class PackageGuids
    {
        /// <summary>
        /// The only context <see cref="KubunoPackage"/> auto-loads on (a <c>ProvideUIContextRule</c>): a solution
        /// with a <c>.rsproj</c>, a Rust or <c>.kbview</c> editor, or an Open Folder Cargo workspace
        /// (<see cref="Kubuno.Rust.PackageGuids.CargoFolderUIContextString"/>). Never active for a C#-only solution or the
        /// start window.
        /// </summary>
        public const string KubunoActivationUIContextString = "d0f63dbb-a2e8-47da-9211-127cf0f4c953";
    }
}

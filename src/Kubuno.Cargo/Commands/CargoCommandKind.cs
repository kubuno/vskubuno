namespace Kubuno.Cargo.Commands
{
    /// <summary>Cargo subcommands the extension drives.</summary>
    public enum CargoCommandKind
    {
        Build,
        Check,
        Test,
        Clean,
        Run,
        Fetch,

        /// <summary>`cargo add`: the "Référence de projet.../Dépendance Cargo (crate)..." Add-submenu commands (Kubuno.VisualStudio.Commands).</summary>
        Add,

        /// <summary>`cargo remove`: the Dependencies node's own "Supprimer" command.</summary>
        Remove,
    }
}

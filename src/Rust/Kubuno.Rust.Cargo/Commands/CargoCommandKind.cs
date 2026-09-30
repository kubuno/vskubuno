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

        /// <summary>`cargo update`: the Dependencies node's "Update crates" and a dependency's "Update" (with <c>-p</c>).</summary>
        Update,

        /// <summary>`cargo doc`: a local dependency's "Open documentation" (with <c>--open -p</c>).</summary>
        Doc,

        /// <summary>`cargo rustc`: a build whose final crate gets extra rustc arguments after "--" (the .rsproj SDK's embedded Win32 resources).</summary>
        Rustc,

        /// <summary>`cargo clippy`: the linter, run after a build when a .rsproj enables "Run Clippy on build".</summary>
        Clippy,
    }
}

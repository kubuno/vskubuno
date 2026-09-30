namespace Kubuno.Rust.Logic.ProjectGeneration
{
    /// <summary>
    /// What <see cref="RsprojGenerationPlanner"/> decided for one workspace member's <c>.rsproj</c>.
    /// A <c>.rsproj</c> is developer-owned once it exists (docs/RSPROJ.md work package 5's
    /// idempotency requirement, the same "never regenerate what the developer owns" rule
    /// <c>CLAUDE.md</c> already applies to Kubuno view XML): it is only ever created, never
    /// overwritten.
    /// </summary>
    public enum RsprojPlanAction
    {
        /// <summary>No file exists at the planned path yet - the caller should write <see cref="RsprojProjectPlanItem.Content"/>.</summary>
        Create,

        /// <summary>A file already exists at the planned path - the caller must leave it untouched and report it as skipped.</summary>
        SkipExisting,
    }
}

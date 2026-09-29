using Kubuno.Cargo.Commands;

namespace Kubuno.Cargo.MSBuild.Tasks
{
    /// <summary>
    /// <c>cargo clippy</c>, run after the build by <c>Kubuno.Rust.Sdk</c>'s <c>KubunoClippy</c> target when a
    /// <c>.rsproj</c> sets <c>KubunoClippyOnBuild</c> ("Run Clippy on build", Code Analysis page). Its lints
    /// arrive in the same JSON message format as compiler diagnostics, so they reach the Error List through
    /// the same parsing as <see cref="CargoBuild"/>; the lint levels themselves come from Cargo.toml's
    /// <c>[lints]</c> table.
    /// </summary>
    public sealed class CargoClippy : CargoBuildTaskBase
    {
        /// <summary>Restricts the lint run to one <c>[[bin]]</c> target, emitted as <c>--bin &lt;value&gt;</c>.</summary>
        public string? Bin { get; set; }

        protected override CargoCommand CreateCommand()
        {
            CargoCommand command = CargoCommand.Clippy();
            if (!string.IsNullOrEmpty(Bin))
            {
                command.WithTarget(CargoTargetSelector.Bin(Bin!));
            }
            return command;
        }
    }
}

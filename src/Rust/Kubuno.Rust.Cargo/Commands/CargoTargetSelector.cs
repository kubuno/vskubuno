using System.Collections.Generic;

namespace Kubuno.Rust.Cargo.Commands
{
    /// <summary>
    /// Which target(s) within a package a Cargo command applies to: a single named
    /// <c>--bin</c>/<c>--example</c>/<c>--test</c>/<c>--bench</c>, the package's <c>--lib</c>,
    /// or one of the "--all-*" shorthands.
    /// </summary>
    public sealed class CargoTargetSelector
    {
        private CargoTargetSelector(string flag, string? name)
        {
            Flag = flag;
            Name = name;
        }

        public string Flag { get; }

        public string? Name { get; }

        public static CargoTargetSelector Bin(string name) => new CargoTargetSelector("--bin", name);

        public static CargoTargetSelector Example(string name) => new CargoTargetSelector("--example", name);

        public static CargoTargetSelector Test(string name) => new CargoTargetSelector("--test", name);

        public static CargoTargetSelector Bench(string name) => new CargoTargetSelector("--bench", name);

        public static readonly CargoTargetSelector Lib = new CargoTargetSelector("--lib", null);

        public static readonly CargoTargetSelector AllBins = new CargoTargetSelector("--bins", null);

        public static readonly CargoTargetSelector AllExamples = new CargoTargetSelector("--examples", null);

        public static readonly CargoTargetSelector AllTests = new CargoTargetSelector("--tests", null);

        public static readonly CargoTargetSelector AllBenches = new CargoTargetSelector("--benches", null);

        public static readonly CargoTargetSelector AllTargets = new CargoTargetSelector("--all-targets", null);

        internal void AppendTo(List<string> args)
        {
            args.Add(Flag);
            if (Name is not null)
            {
                args.Add(Name);
            }
        }

        public override string ToString() => Name is null ? Flag : $"{Flag} {Name}";
    }
}

namespace Kubuno.Rust.Cargo.Metadata
{
    /// <summary>Well-known values found in <see cref="CargoTarget.Kind"/>.</summary>
    public static class CargoTargetKind
    {
        public const string Lib = "lib";
        public const string Bin = "bin";
        public const string Example = "example";
        public const string Test = "test";
        public const string Bench = "bench";
        public const string CustomBuild = "custom-build";
        public const string ProcMacro = "proc-macro";
        public const string Cdylib = "cdylib";
        public const string Dylib = "dylib";
        public const string Staticlib = "staticlib";
        public const string Rlib = "rlib";
    }
}

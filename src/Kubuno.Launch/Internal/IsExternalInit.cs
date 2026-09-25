namespace System.Runtime.CompilerServices
{
    // netstandard2.0 predates C# 9 `init` accessors: the compiler only emits them when this
    // marker type exists, and the BCL doesn't ship it below net5.0. This polyfill (the
    // standard workaround for records/init-only properties on older target frameworks) lets
    // us use `record` types with `init` properties while still targeting netstandard2.0.
    internal static class IsExternalInit
    {
    }
}

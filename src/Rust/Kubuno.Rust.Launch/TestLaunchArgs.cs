using System;
using System.Collections.Generic;

namespace Kubuno.Rust.Launch
{
    /// <summary>
    /// Builds the argument list passed directly to a `cargo test --no-run` test binary
    /// (not to `cargo test` itself — the debugger launches the binary, so there is no
    /// `cargo` process in between) to run a single test or a module filter.
    /// </summary>
    public static class TestLaunchArgs
    {
        /// <summary>
        /// Builds `&lt;filter&gt; [--exact] [--nocapture] [--test-threads=1]`.
        /// </summary>
        /// <param name="filter">
        /// A single test's fully-qualified name (e.g. "module::tests::it_works") for an
        /// exact match, or a substring/module path (e.g. "module::tests::") to run every
        /// test whose name contains it.
        /// </param>
        /// <param name="exact">
        /// Append `--exact`, requiring <paramref name="filter"/> to match the whole test
        /// name rather than a substring. Only meaningful for a single-test filter — pass
        /// false for a module filter.
        /// </param>
        /// <param name="noCapture">Append `--nocapture`, so `println!`/`eprintln!` output isn't swallowed.</param>
        /// <param name="singleThreaded">
        /// Append `--test-threads=1`. Debugging a multi-threaded test run makes breakpoint
        /// hits and stepping non-deterministic across the harness's worker threads, so this
        /// defaults to true here (it's the "for debugging" case the caller is building for).
        /// </param>
        public static IReadOnlyList<string> Build(
            string filter,
            bool exact = true,
            bool noCapture = true,
            bool singleThreaded = true)
        {
            if (string.IsNullOrEmpty(filter))
            {
                throw new ArgumentException("Filter must not be null or empty.", nameof(filter));
            }

            var args = new List<string> { filter };

            if (exact)
            {
                args.Add("--exact");
            }

            if (noCapture)
            {
                args.Add("--nocapture");
            }

            if (singleThreaded)
            {
                args.Add("--test-threads=1");
            }

            return args;
        }
    }
}

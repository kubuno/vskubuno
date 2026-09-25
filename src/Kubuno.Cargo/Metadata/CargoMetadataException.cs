using System;

namespace Kubuno.Cargo.Metadata
{
    /// <summary>Thrown when `cargo metadata` exits with a non-zero code or produces no usable output.</summary>
    public sealed class CargoMetadataException : Exception
    {
        public CargoMetadataException(int exitCode, string standardError)
            : base(BuildMessage(exitCode, standardError))
        {
            ExitCode = exitCode;
            StandardError = standardError;
        }

        public int ExitCode { get; }

        public string StandardError { get; }

        private static string BuildMessage(int exitCode, string standardError) =>
            string.IsNullOrWhiteSpace(standardError)
                ? $"`cargo metadata` exited with code {exitCode}."
                : $"`cargo metadata` exited with code {exitCode}:{Environment.NewLine}{standardError}";
    }
}

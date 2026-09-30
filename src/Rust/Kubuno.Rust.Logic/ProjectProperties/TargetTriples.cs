using System;

namespace Kubuno.Rust.Logic.ProjectProperties
{
    /// <summary>
    /// Human-readable descriptions of Rust target triples (<c>arch-vendor-os[-env]</c>) for the Application page's
    /// "Target OS" (docs/RSPROJ.md, "Project properties like .NET").
    /// </summary>
    public static class TargetTriples
    {
        /// <summary>The platform a <c>.rsproj</c> builds for when no triple is set (x64 only, see Kubuno.Rust.Sdk's Sdk.props).</summary>
        public const string Host = "x86_64-pc-windows-msvc";

        /// <summary>E.g. "Windows (x86_64, MSVC)" for <c>x86_64-pc-windows-msvc</c>; the host platform when empty.</summary>
        public static string Describe(string? triple)
        {
            string value = string.IsNullOrWhiteSpace(triple) ? Host : triple!.Trim();
            string[] parts = value.Split('-');
            if (parts.Length < 2)
            {
                return value;
            }

            // arch-vendor-os[-env], or arch-os[-env] when the vendor is omitted (thumbv7em-none-eabihf, wasm32-wasip1).
            string arch = parts[0];
            bool hasVendor = parts.Length >= 3 && IsVendor(parts[1]);
            string os = hasVendor ? parts[2] : parts[1];
            string? env = hasVendor ? (parts.Length >= 4 ? parts[3] : null) : (parts.Length >= 3 ? parts[2] : null);

            string osName = os switch
            {
                "windows" => "Windows",
                "linux" => "Linux",
                "darwin" => "macOS",
                "ios" => "iOS",
                "android" or "androideabi" => "Android",
                "freebsd" => "FreeBSD",
                "netbsd" => "NetBSD",
                "openbsd" => "OpenBSD",
                "wasi" or "wasip1" or "wasip2" => "WASI",
                "unknown" when arch.StartsWith("wasm", StringComparison.Ordinal) => "WebAssembly",
                "none" => "bare metal",
                "uefi" => "UEFI",
                _ => os,
            };

            string abi = env switch
            {
                null => string.Empty,
                "msvc" => ", MSVC",
                "gnu" => ", GNU",
                "gnullvm" => ", GNU (LLVM)",
                "musl" => ", musl",
                "gnueabihf" => ", GNU hard-float",
                _ => ", " + env,
            };

            return $"{osName} ({arch}{abi})";
        }

        private static bool IsVendor(string part) =>
            part is "pc" or "unknown" or "apple" or "uwp" or "wrs" or "sun" or "nvidia" or "fortanix" or "esp" or "sony" or "nintendo" or "kmc" or "linux";
    }
}

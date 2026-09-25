using System;

namespace Kubuno.VisualStudio
{
    /// <summary>GUIDs referenced from attributes (which require compile-time constants) and from code.</summary>
    internal static class PackageGuidStrings
    {
        public const string Package = "5f3a9b2e-9e0c-4f0a-9a7a-6f0f3c9c9d10";
    }

    internal static class PackageGuids
    {
        public static readonly Guid KubunoOutputPane = new("8e4b2a2f-1a6d-4f6a-9a2e-5b7a8c9d0e1f");
    }
}

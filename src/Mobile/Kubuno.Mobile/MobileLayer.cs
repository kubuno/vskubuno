using Kubuno.Core.Extensibility;

namespace Kubuno.Mobile
{
    /// <summary>
    /// The mobile layer's part of the package (docs/ARCHITECTURE.md, "Roadmap - Kubuno mobile apps in the same VSIX" and
    /// "Roadmap - Kubuno view rendering engine for mobile"): registered with the package today, with no feature yet. The
    /// hooks of <see cref="KubunoLayer"/> are where the mobile features will start (see README.md): the device picker
    /// and Logcat commands in <see cref="KubunoLayer.InitializeOnUIThread"/>, the Pair-to-Mac connection in
    /// <see cref="KubunoLayer.InitializeDeferredAsync"/>...
    /// </summary>
    public sealed class MobileLayer : KubunoLayer
    {
        public override string Name => "Mobile";
    }
}

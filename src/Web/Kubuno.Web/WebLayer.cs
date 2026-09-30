using Kubuno.Core.Extensibility;

namespace Kubuno.Web
{
    /// <summary>
    /// The web layer's part of the package (docs/ARCHITECTURE.md, "Roadmap - Kubuno web modules in the same VSIX"):
    /// registered with the package today, with no feature yet. The hooks of <see cref="KubunoLayer"/> are where the web
    /// module features will start (see README.md): the <c>module.toml</c> tooling and the <c>.kbpkg</c> commands in
    /// <see cref="KubunoLayer.InitializeOnUIThread"/>, the local core deployment in
    /// <see cref="KubunoLayer.InitializeDeferredAsync"/>...
    /// </summary>
    public sealed class WebLayer : KubunoLayer
    {
        public override string Name => "Web";
    }
}

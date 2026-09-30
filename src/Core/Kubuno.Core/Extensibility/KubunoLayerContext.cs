using System;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Core.Extensibility
{
    /// <summary>
    /// What the package gives each <see cref="KubunoLayer"/> hook: the package itself, its services and the few
    /// package-only operations a layer needs (registering an editor factory, proffering a service).
    /// </summary>
    public sealed class KubunoLayerContext
    {
        private readonly Action<IVsEditorFactory> _registerEditorFactory;

        internal KubunoLayerContext(AsyncPackage package, Action<IVsEditorFactory> registerEditorFactory)
        {
            Package = package;
            _registerEditorFactory = registerEditorFactory;
        }

        /// <summary>The Kubuno package.</summary>
        public AsyncPackage Package { get; }

        /// <summary>The package's <see cref="Microsoft.VisualStudio.Threading.JoinableTaskFactory"/>.</summary>
        public JoinableTaskFactory JoinableTaskFactory => Package.JoinableTaskFactory;

        /// <summary>Cancelled when the package is disposed (Visual Studio closing).</summary>
        public CancellationToken DisposalToken => Package.DisposalToken;

        /// <summary>The extension's install directory (<see cref="KubunoExtension.InstallDirectory"/>).</summary>
        public string? ExtensionDirectory => KubunoExtension.InstallDirectory;

        /// <summary>The menu command service, for the layer's commands (set before <see cref="KubunoLayer.InitializeOnUIThread"/>).</summary>
        public OleMenuCommandService? CommandService { get; internal set; }

        /// <summary>The MEF component model (set before <see cref="KubunoLayer.InitializeOnIdle"/>).</summary>
        public IComponentModel? ComponentModel { get; internal set; }

        /// <summary>True when the deferred steps waited for a solution load in progress.</summary>
        public bool WaitedForSolutionLoad { get; internal set; }

        /// <summary>Hands a live editor factory to Visual Studio (its registration is the package's <c>[ProvideEditorFactory]</c>).</summary>
        public void RegisterEditorFactory(IVsEditorFactory factory) => _registerEditorFactory(factory);

        /// <summary>An options page of the package (declared by the package's <c>[ProvideOptionPage]</c>).</summary>
        public T GetDialogPage<T>()
            where T : DialogPage => (T)Package.GetDialogPage(typeof(T));

        /// <summary>Proffers a service from the package (<paramref name="promote"/>: to Visual Studio's global services too).</summary>
        public void AddService(Type serviceType, object instance, bool promote) =>
            ((IServiceContainer)Package).AddService(serviceType, instance, promote);

        /// <summary>A Visual Studio service, from any thread.</summary>
        public Task<object?> GetServiceAsync(Type serviceType) => Package.GetServiceAsync(serviceType)!;
    }
}

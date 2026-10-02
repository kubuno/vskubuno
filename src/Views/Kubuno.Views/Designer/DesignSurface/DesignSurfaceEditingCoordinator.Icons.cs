using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;
using Kubuno.Views.Designer.Icons;
using Kubuno.Views.Logging;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>
    /// The icon editors' server side (docs/ICONS.md): the Kubuno icon set (<c>kubuno/icons</c>, read once per Visual Studio
    /// session) and icon values rendered by the runtime's own code (<c>kubuno/renderIcon</c>, kept a few seconds so a grid
    /// repaint does not ask again).
    /// </summary>
    internal sealed partial class DesignSurfaceEditingCoordinator : IKbviewIconServices
    {
        private static readonly TimeSpan IconTimeout = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan RenderKeep = TimeSpan.FromSeconds(5);
        private static IconCatalog? s_catalog;
        private readonly Dictionary<string, (DateTime At, ImageSource? Image)> _renders = new Dictionary<string, (DateTime, ImageSource?)>(StringComparer.Ordinal);

        public IconCatalog GetIconCatalog()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (s_catalog is { IsEmpty: false } cached)
            {
                return cached;
            }

            var json = Call("kubuno/icons", new { });
            var catalog = IconCatalog.FromJson(json?.ToString(Newtonsoft.Json.Formatting.None));
            if (!catalog.IsEmpty)
            {
                s_catalog = catalog;
            }

            return catalog;
        }

        public ImageSource? RenderIcon(string value, int size, Color color)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var uri = TryGetDocumentUri();
            var hex = $"#{color.R:x2}{color.G:x2}{color.B:x2}";
            var key = $"{uri}|{value}|{size}|{hex}";
            if (_renders.TryGetValue(key, out var kept) && DateTime.UtcNow - kept.At < RenderKeep)
            {
                return kept.Image;
            }

            var result = Call("kubuno/renderIcon", new { value, uri, size, color = hex });
            ImageSource? image = null;
            if (result?["bgra"] is { } bgra && (int?)result["width"] is int w && (int?)result["height"] is int h)
            {
                try
                {
                    image = IconDrawing.FromBgra(w, h, Convert.FromBase64String((string?)bgra ?? string.Empty));
                }
                catch (FormatException)
                {
                }
            }

            _renders[key] = (DateTime.UtcNow, image);
            return image;
        }

        /// <summary>A synchronous call bounded by <see cref="IconTimeout"/> (the editors ask from the UI thread, like the binding editor); null on failure.</summary>
        private JToken? Call(string method, object parameters)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var rpc = _resolveLanguageClient()?.ReadyRpc;
            if (rpc is null)
            {
                return null;
            }

#pragma warning disable VSTHRD104 // the icon editors ask synchronously; bounded by IconTimeout.
            return ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                var call = rpc.InvokeWithParameterObjectAsync<JToken?>(method, parameters);
                if (await Task.WhenAny(call, Task.Delay(IconTimeout)) != call)
                {
                    KubunoViewsLogHost.Current.WriteLine($"[designer] {method} did not answer in time.");
                    return null;
                }

                try
                {
                    return await call;
                }
                catch (Exception ex) when (ex is RemoteInvocationException or ConnectionLostException or OperationCanceledException)
                {
                    KubunoViewsLogHost.Current.WriteException($"[designer] {method} failed", ex);
                    return null;
                }
            });
#pragma warning restore VSTHRD104
        }
    }
}

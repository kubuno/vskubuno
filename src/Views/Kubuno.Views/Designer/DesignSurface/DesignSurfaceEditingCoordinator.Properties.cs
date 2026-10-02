using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kubuno.Views.Designer.Editing;
using Kubuno.Views.Designer.Editing.Infrastructure;
using Kubuno.Views.Designer.PropertyBrowser;
using Kubuno.Views.Logging;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>
    /// What the Properties window's rich editors need from the pane (docs/EVENTS.md, "WinForms-rich property sets"): the
    /// view file (image paths are relative to it), the view model's bindable paths (<c>kubuno/bindingPaths</c>) and a
    /// text-level edit applied as one undo unit (the collection and list editors).
    /// </summary>
    internal sealed partial class DesignSurfaceEditingCoordinator : IKbviewDesignServices
    {
        private const string BindingPathsMethod = "kubuno/bindingPaths";

        private static readonly TimeSpan BindingPathsTimeout = TimeSpan.FromSeconds(2);

        public string? ViewFilePath
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var uri = TryGetDocumentUri();
                try
                {
                    return uri is null ? null : new Uri(uri).LocalPath;
                }
                catch (UriFormatException)
                {
                    return null;
                }
            }
        }

        public IReadOnlyList<string> GetBindingPaths()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var rpc = _resolveLanguageClient()?.ReadyRpc;
            var uri = TryGetDocumentUri();
            if (rpc is null || uri is null)
            {
                return Array.Empty<string>();
            }

            var openFiles = OpenCodeBehindTexts(uri);
#pragma warning disable VSTHRD104 // the binding editor asks synchronously; bounded by BindingPathsTimeout.
            return ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                var call = rpc.InvokeWithParameterObjectAsync<JToken?>(BindingPathsMethod, new { uri, openFiles });
                if (await Task.WhenAny(call, Task.Delay(BindingPathsTimeout)) != call)
                {
                    KubunoViewsLogHost.Current.WriteLine($"[designer] {BindingPathsMethod} did not answer in time.");
                    return (IReadOnlyList<string>)Array.Empty<string>();
                }

                try
                {
                    return BindingText.ParsePaths((await call)?.ToString(Newtonsoft.Json.Formatting.None));
                }
                catch (Exception ex) when (ex is RemoteInvocationException or ConnectionLostException or OperationCanceledException)
                {
                    KubunoViewsLogHost.Current.WriteException($"[designer] {BindingPathsMethod} failed", ex);
                    return Array.Empty<string>();
                }
            });
#pragma warning restore VSTHRD104
        }

        public void ApplyTextEdits(int version, IReadOnlyList<TextReplacement> edits, string description)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (edits.Count == 0)
            {
                return;
            }

            var text = GetCurrentText();
            if (CurrentVersion != version)
            {
                KubunoViewsLogHost.Current.WriteLine($"[designer] '{description}' not applied: the view changed meanwhile.");
                return;
            }

            var dtos = edits.Select(e => new TextEditDto(new LspRange(LspPositionMapper.FromOffset(text, e.Start), LspPositionMapper.FromOffset(text, e.Start + e.Length)), e.NewText)).ToList();
            var result = new BufferEditApplier(_buffer).Apply(new ApplyEditRequest(version, dtos));
            if (!result.Succeeded)
            {
                KubunoViewsLogHost.Current.WriteLine($"[designer] '{description}' could not be applied: {result.Outcome}.");
            }
        }
    }
}

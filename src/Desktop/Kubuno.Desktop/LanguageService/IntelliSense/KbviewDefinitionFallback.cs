using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Desktop.Logic.IntelliSense;
using Kubuno.Views.Designer.Handlers.Infrastructure;
using Kubuno.Shared.Logging;
using Kubuno.Rust.LanguageService;
using Kubuno.Rust.LanguageService.IntelliSense;
using Kubuno.Views;
using Kubuno.Views.LanguageService;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.Desktop.LanguageService.IntelliSense
{
    /// <summary>
    /// Go To Definition (F12) / Peek (Alt+F12) from a <c>.kbview</c> into Rust where kubuno-views-ls has no answer
    /// (it already goes from an event attribute to its handler): from a control's tag to the Rust type of the control
    /// (rust-analyzer's workspace symbols, dependencies included, so <c>&lt;Button&gt;</c> opens kubuno_ui's
    /// <c>Button</c> and a user control opens its own struct), and from a binding path (<c>{Binding Status}</c>) to the
    /// <c>"Status" =&gt;</c> arm of the view model's <c>fn get</c>. Installed with the first <c>.kbview</c> editor.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType(KbviewConstants.ContentType)]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class KbviewDefinitionFallback : IWpfTextViewCreationListener
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        public void TextViewCreated(IWpfTextView textView)
        {
            HoverSuppressionMiddleLayer.DefinitionFallback ??= ResolveAsync;
        }

        private static async Task<JToken?> ResolveAsync(JToken request)
        {
            try
            {
                var uri = (string?)request["textDocument"]?["uri"];
                var position = request["position"];
                if (uri is null || position?["line"] is null || position["character"] is null)
                {
                    return null;
                }

                var path = new Uri(Uri.UnescapeDataString(uri)).LocalPath;
                var folder = Path.GetDirectoryName(path) ?? string.Empty;
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var fileHost = new VsWorkspaceFileHost(ServiceProvider.GlobalProvider);
                var openViews = fileHost.OpenTexts(folder, Kubuno.Views.Logic.ViewFiles.Extensions);
                var openRust = fileHost.OpenTexts(folder, ".rs");
                await TaskScheduler.Default;

                var text = Lookup(openViews, path) ?? (File.Exists(path) ? File.ReadAllText(path) : null);
                if (text is null)
                {
                    return null;
                }

                int offset = Offset(text, (int)position["line"]!, (int)position["character"]!);
                var element = KbviewNavigation.ElementNameAt(text, offset);
                if (element != null)
                {
                    return await TypeLocationsAsync(element, ProjectRoot(path)).ConfigureAwait(false);
                }

                var bindingPath = KbviewNavigation.BindingPathAt(text, offset);
                if (bindingPath != null)
                {
                    return BindingLocation(path, bindingPath, openRust);
                }

                return null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UriFormatException or ArgumentException or InvalidOperationException)
            {
                KubunoLog.WriteLine("Views: Go To Definition could not reach the Rust side: " + exception.Message);
                return null;
            }
        }

        /// <summary>The Rust types named <paramref name="name"/> (rust-analyzer's workspace symbols, dependencies included).</summary>
        private static async Task<JToken?> TypeLocationsAsync(string name, string? projectRoot)
        {
            if (RustLanguageClient.Instance?.Rpc is not { } rpc)
            {
                return null;
            }

            // rust-analyzer searches either the local crates (the project, and the Kubuno crates it takes by path) or the
            // libraries (crates.io, the standard library): the local ones first.
            foreach (var scope in new[] { "workspace", "workspaceAndDependencies" })
            {
                using var timeout = new CancellationTokenSource(Timeout);
                JToken? symbols;
                try
                {
                    symbols = await rpc.InvokeWithParameterObjectAsync<JToken?>(
                        "workspace/symbol",
                        new JObject { ["query"] = name, ["searchScope"] = scope, ["searchKind"] = "onlyTypes" },
                        timeout.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is RemoteInvocationException or ConnectionLostException or OperationCanceledException or ObjectDisposedException)
                {
                    KubunoLog.WriteLine("Views: workspace/symbol failed: " + exception.Message);
                    return null;
                }

                // The exact name only; one answer, like F12 on a XAML tag: the project's own type (a user control) first,
                // then kubuno_ui's control, then the other Kubuno crates.
                var best = (symbols as JArray ?? new JArray())
                    .OfType<JObject>()
                    .Where(s => (string?)s["name"] == name && s["location"]?["range"] != null)
                    .OrderBy(s => CratePriority((string?)s["location"]?["uri"], projectRoot))
                    .ThenBy(s => (int?)s["kind"] == 23 ? 0 : 1)
                    .FirstOrDefault();
                if (best != null)
                {
                    return new JArray(best["location"]!.DeepClone());
                }
            }

            return null;
        }

        private static int CratePriority(string? uri, string? projectRoot)
        {
            var path = Uri.UnescapeDataString(uri ?? string.Empty).Replace('\\', '/');
            if (projectRoot != null && path.IndexOf(projectRoot.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // A user control of the project itself.
                return 0;
            }

            // The framework's crates, under their names since the 2026-10 rename or before it.
            if (path.IndexOf("/kubuno-desktop-ui/", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("/kubuno-ui/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 1;
            }

            if (path.IndexOf("/kubuno-desktop-controls/", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("/kubuno-controls/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 2;
            }

            return path.IndexOf("/crates/kubuno-", StringComparison.OrdinalIgnoreCase) >= 0 ? 3 : 4;
        }

        /// <summary>The folder of the Cargo package the view belongs to (its nearest Cargo.toml), or null.</summary>
        private static string? ProjectRoot(string viewPath)
        {
            for (var dir = Path.GetDirectoryName(viewPath); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
            {
                if (File.Exists(Path.Combine(dir, "Cargo.toml")))
                {
                    return dir + Path.DirectorySeparatorChar;
                }
            }

            return null;
        }

        /// <summary>The <c>"path" =&gt;</c> arm in the view's code-behind (same stem first, then the folder's other .rs files).</summary>
        private static JToken? BindingLocation(string viewPath, string bindingPath, IDictionary<string, string> openRust)
        {
            var folder = Path.GetDirectoryName(viewPath) ?? string.Empty;
            var sameStem = Path.ChangeExtension(viewPath, ".rs");
            var candidates = new[] { sameStem }.Concat(Directory.EnumerateFiles(folder, "*.rs").Where(f => !string.Equals(f, sameStem, StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            foreach (var candidate in candidates)
            {
                var rust = Lookup(openRust, candidate) ?? (File.Exists(candidate) ? File.ReadAllText(candidate) : null);
                if (rust is null)
                {
                    continue;
                }

                int at = KbviewNavigation.FindBindingArm(rust, bindingPath);
                if (at >= 0)
                {
                    var (line, character) = Position(rust, at);
                    var range = new JObject
                    {
                        ["start"] = new JObject { ["line"] = line, ["character"] = character },
                        ["end"] = new JObject { ["line"] = line, ["character"] = character + bindingPath.Length },
                    };
                    return new JArray(new JObject { ["uri"] = new Uri(candidate).AbsoluteUri, ["range"] = range });
                }
            }

            return null;
        }

        private static string? Lookup(IDictionary<string, string> openFiles, string path)
        {
            foreach (var pair in openFiles)
            {
                if (Uri.TryCreate(pair.Key, UriKind.Absolute, out var key) && string.Equals(Path.GetFullPath(key.LocalPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }

            return null;
        }

        private static int Offset(string text, int line, int character)
        {
            int offset = 0;
            for (int l = 0; l < line && offset < text.Length; l++)
            {
                int next = text.IndexOf('\n', offset);
                if (next < 0)
                {
                    return text.Length;
                }

                offset = next + 1;
            }

            return Math.Min(text.Length, offset + character);
        }

        private static (int Line, int Character) Position(string text, int offset)
        {
            int line = 0;
            int lineStart = 0;
            for (int i = 0; i < offset && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }

            return (line, offset - lineStart);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Kubuno.Views.Logic.Resources;
using Kubuno.Views.Logging;
using Microsoft.VisualStudio.Shell;
using Kubuno.Views.Designer;
using Kubuno.Views.Designer.DesignSurface;

namespace Kubuno.Desktop.Designer.DesignSurface
{
    /// <summary>
    /// The project's resources on the design surface (docs/RESOURCES.md): the surface is sent every <c>.kbres</c> set of
    /// the view's project with the design-time language (<c>setResources</c>,
    /// <c>kubuno_desktop_views::protocol::HostMessage::SetResources</c>) when it starts, when the language picker of the designer
    /// changes, and when a resource file or a picture of the project changes on disk - so <c>{Res …}</c> values and
    /// localised pictures show as they will at run time, in the culture chosen.
    /// </summary>
    public sealed partial class RustDesignSurfaceHost
    {
        private string _designCulture = string.Empty;
        // Until the language picker is used, the design culture follows Windows' UI culture when the project has resources
        // in it (what the application shows at run time); "(Default)" stays selectable.
        private bool _designCultureChosen;
        private string? _lastSentResources;
        private FileSystemWatcher? _resourceWatcher;
        private System.Threading.Timer? _resourceDebounce;

        /// <summary>Raised (on any thread) when the project's resource files change: the cultures offered may have changed.</summary>
        public event EventHandler? ResourcesChanged;

        /// <summary>The design-time culture (<c>""</c> = "(Default)": the neutral values).</summary>
        public string DesignCulture => _designCultureChosen ? _designCulture : DesignSurfaceResourcesProtocol.DefaultDesignCulture(ProjectCultures(), System.Globalization.CultureInfo.CurrentUICulture);

        /// <summary>Shows the view in <paramref name="culture"/> (<c>""</c>: the neutral values); the application is not changed.</summary>
        public void SetDesignCulture(string culture)
        {
            _designCulture = culture ?? string.Empty;
            _designCultureChosen = true;
            SendResources();
        }

        /// <summary>The cultures of the project's resource files, sorted.</summary>
        public IReadOnlyList<string> ProjectCultures()
        {
            var root = ResourceProjectRoot;
            if (root is null)
            {
                return Array.Empty<string>();
            }

            return ProjectResources.NeutralFiles(root).SelectMany(n => ResourceNames.Satellites(n).Select(s => s.Culture)).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToList();
        }

        private string? ResourceProjectRoot => BaseDirectory is { Length: > 0 } dir ? ProjectResources.ProjectRoot(dir) : null;

        /// <summary>Sends the project's resource sets and the design culture to the surface (best effort, like every message).</summary>
        /// <remarks>
        /// Nothing is sent when nothing changed since the last message (a file watcher event for an unchanged file,
        /// a language picked again), nor for a project without resource files and the default language: every
        /// message repaints the surface. <paramref name="force"/>: a freshly (re)started surface.
        /// </remarks>
        private void SendResources(bool force = false)
        {
            var root = ResourceProjectRoot;
            if (root is null)
            {
                return;
            }

            try
            {
                var files = ProjectResources.NeutralFiles(root);
                WatchResources(root);
                if (files.Count == 0 && DesignCulture.Length == 0 && _lastSentResources is null)
                {
                    return;
                }

                var message = DesignSurfaceResourcesProtocol.EncodeSetResources(DesignCulture, files);
                if (!force && message == _lastSentResources)
                {
                    return;
                }

                _lastSentResources = message;
                SendLine(message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                KubunoViewsLogHost.Current.WriteLine("[designer] resources not sent: " + ex.Message);
            }
        }

        private void WatchResources(string root)
        {
            if (_resourceWatcher is not null && string.Equals(_resourceWatcher.Path, root, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _resourceWatcher?.Dispose();
            var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            FileSystemEventHandler changed = (_, e) =>
            {
                if (IsResourceFile(e.FullPath))
                {
                    // Debounced: a save writes several times; the surface reloads once.
                    _resourceDebounce?.Dispose();
                    _resourceDebounce = new System.Threading.Timer(_ => OnResourceFilesChanged(), null, 400, System.Threading.Timeout.Infinite);
                }
            };
            watcher.Changed += changed;
            watcher.Created += changed;
            watcher.Deleted += changed;
            watcher.Renamed += (s, e) => changed(s, e);
            watcher.EnableRaisingEvents = true;
            _resourceWatcher = watcher;
        }

        private static bool IsResourceFile(string path)
        {
            // Build output and tool folders change during builds: never a resource of the project.
            foreach (var skipped in new[] { "target", "obj", "bin", ".vs", ".git", "node_modules" })
            {
                if (path.IndexOf(Path.DirectorySeparatorChar + skipped + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }

            var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            return ext == "kbres" || KbresFile.ImageFormats.Contains(ext) || KbresFile.AudioFormats.Contains(ext);
        }

        private void OnResourceFilesChanged()
        {
#pragma warning disable VSSDK007 // fire-and-forget from a file watcher; FileAndForget reports faults.
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_surface != null)
                {
                    SendResources();
                }

                ResourcesChanged?.Invoke(this, EventArgs.Empty);
            }).FileAndForget("kubuno/designer/resourcesChanged");
#pragma warning restore VSSDK007
        }

        /// <summary>Stops watching the project's resource files (the pane closes).</summary>
        private void DisposeResources()
        {
            _resourceDebounce?.Dispose();
            _resourceDebounce = null;
            _resourceWatcher?.Dispose();
            _resourceWatcher = null;
        }
    }
}

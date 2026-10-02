using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Kubuno.Desktop.Designer.Outline;
using Kubuno.Desktop.Designer.Registry;
using Kubuno.Desktop.Designer.Registry.Infrastructure;
using Kubuno.Desktop.Designer.Toolbox;
using Kubuno.Desktop.Designer.UI;
using Kubuno.Desktop.Views.Logging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json.Linq;

namespace Kubuno.Desktop.Designer.DesignSurface
{
    /// <summary>
    /// The project's own controls in the designer (docs/EVENTS.md EVT-7b):
    /// <list type="bullet">
    /// <item><b>registry refresh</b> - kubuno-views-ls reads the project's <c>#[derive(Component)]</c> classes from
    /// its sources; the registry's version is polled and, when it changed, the registry is fetched again (the
    /// Properties window, the menus and the Toolbox follow) and its project entries are sent to the design surface
    /// (<c>projectComponents</c>), which registers those it does not link as labelled placeholders;</item>
    /// <item><b>the Toolbox's "&lt;Project&gt; Composants" tab</b> - after a design build, the project controls the
    /// surface links (its <c>--export-registry</c>), like WinForms' AutoToolboxPopulate, plus the controls of other
    /// crates chosen with "Choisir des éléments…";</item>
    /// <item><b>the component tray</b> - the view's non-visual components (<c>&lt;Timer&gt;</c>…), listed under the
    /// surface: select, double-click (default event handler), Delete.</item>
    /// </list>
    /// </summary>
    internal sealed partial class DesignSurfaceEditingCoordinator
    {
        private static readonly TimeSpan RegistryPollInterval = TimeSpan.FromSeconds(3);

        private DispatcherTimer? _registryPoll;
        private bool _registryRefreshing;
        private IDesignSurfaceRuntimeSource? _runtimeSource;
        private ComponentTray? _componentTray;
        private string _sentProjectComponents = "[]";
        private string? _loggedProjectToolbox;

        /// <summary>Called once the registry and the language server are live (<see cref="AttachNativeWindows"/>).</summary>
        private void StartProjectComponentSync()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_host is IDesignSurfaceRuntimeAware aware)
            {
                _runtimeSource = aware.RuntimeSource;
                _runtimeSource.Changed += OnRuntimeSourceChanged;
            }

            UpdateProjectToolbox();
            SendProjectComponents();
            _registryPoll = new DispatcherTimer(DispatcherPriority.Background) { Interval = RegistryPollInterval };
            _registryPoll.Tick += OnRegistryPollTick;
            _registryPoll.Start();
        }

        private void StopProjectComponentSync()
        {
            if (_registryPoll is not null)
            {
                _registryPoll.Stop();
                _registryPoll.Tick -= OnRegistryPollTick;
                _registryPoll = null;
            }

            if (_runtimeSource is not null)
            {
                _runtimeSource.Changed -= OnRuntimeSourceChanged;
                _runtimeSource = null;
            }

            if (_componentTray is not null)
            {
                _componentTray.ItemSelected -= OnTrayItemSelected;
                _componentTray.ItemActivated -= OnTrayItemActivated;
                _componentTray.DeleteRequested -= OnTrayDeleteRequested;
            }
        }

        private void AttachComponentTray(ComponentTray? tray)
        {
            _componentTray = tray;
            if (tray is not null)
            {
                tray.ItemSelected += OnTrayItemSelected;
                tray.ItemActivated += OnTrayItemActivated;
                tray.DeleteRequested += OnTrayDeleteRequested;
            }
        }

        private void OnRuntimeSourceChanged(object? sender, EventArgs e)
        {
#pragma warning disable VSTHRD001, VSTHRD110 // raised on any thread; a plain dispatcher hop, as in DesignerSplitView.
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (_disposed)
                {
                    return;
                }

                UpdateProjectToolbox();
                // A build may have changed the project's controls: look again now rather than at the next poll.
                OnRegistryPollTick(this, EventArgs.Empty);
            }));
#pragma warning restore VSTHRD001, VSTHRD110
        }

        private void OnRegistryPollTick(object? sender, EventArgs e)
        {
            if (_disposed || _registryRefreshing)
            {
                return;
            }

#pragma warning disable VSSDK007 // see the constructor's own identical precedent/comment.
            ThreadHelper.JoinableTaskFactory.RunAsync(RefreshRegistryAsync).FileAndForget("Kubuno/Designer/RegistryRefresh");
#pragma warning restore VSSDK007
        }

        /// <summary>Fetches the registry again when its version changed (the project's controls were edited and saved).</summary>
        private async Task RefreshRegistryAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_disposed || _registryRefreshing || _resolveLanguageClient()?.ReadyRpc is not { } rpc)
            {
                return;
            }

            _registryRefreshing = true;
            try
            {
                var version = await JsonRpcRegistryClient.FetchVersionAsync(rpc, CancellationToken.None);
                if (version is null || string.Equals(version, _registry?.Version, StringComparison.Ordinal))
                {
                    return;
                }

                var registry = await JsonRpcRegistryClient.FetchAsync(rpc, CancellationToken.None);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_disposed || registry.Components.Count == 0)
                {
                    return;
                }

                KubunoViewsLogHost.Current.WriteLine($"[designer] the component registry changed ({registry.ProjectComponents.Count()} project control(s)).");
                ApplyRegistry(registry);
            }
            finally
            {
                _registryRefreshing = false;
            }
        }

        /// <summary>Uses <paramref name="registry"/> from now on: selection sync, the Properties window, the Toolbox, the surface.</summary>
        private void ApplyRegistry(ComponentRegistry registry)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _registry = registry;
            _selectionSync?.UpdateRegistry(registry);
            NativeToolboxInstaller.EnsureInstalled(registry);
            SendProjectComponents();
            UpdateProjectToolbox();
            UpdateComponentTray();
            PublishSelection(_selectionSync?.CurrentElementId);
        }

        /// <summary>Sends the project's controls to the design surface (placeholders for those it does not link), when they changed.</summary>
        private void SendProjectComponents()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var json = _registry?.ProjectComponentsJson ?? "[]";
            if (_host is not RustDesignSurfaceHost rustHost || string.Equals(json, _sentProjectComponents, StringComparison.Ordinal))
            {
                return;
            }

            _sentProjectComponents = json;
            rustHost.SetProjectComponents(json);
        }

        /// <summary>The Toolbox's project tab, from the last design build's registry (see the class doc).</summary>
        private void UpdateProjectToolbox()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var runtime = _runtimeSource?.Current;
            if (runtime is null || !runtime.IsProjectRuntime || runtime.RegistryPath is not { } path)
            {
                LogProjectToolbox($"no project runtime yet ({(_runtimeSource is null ? "no runtime source" : runtime?.IsProjectRuntime == true ? "no registry" : "bundled runtime")})");
                NativeToolboxInstaller.SetProjectComponents(null, Array.Empty<ComponentMeta>());
                return;
            }

            var linked = ProjectComponentsFile.ReadLinked(path);
            var chosen = ToolboxChoices.Read(runtime.ProjectKey);
            // The application's own control libraries (its path dependencies) are listed like its own controls.
            var appCrates = ProjectComponentsFile.ApplicationDependencyCrates(runtime.ProjectKey);
            var shown = linked
                .Where(c => string.Equals(c.CrateName, runtime.ProjectCrate, StringComparison.Ordinal)
                    || appCrates.Contains(ProjectComponentsFile.Normalize(c.CrateName))
                    || chosen.Contains(ProjectComponentsFile.ChoiceKey(c)))
                .ToList();
            LogProjectToolbox($"{linked.Count} linked control(s) in {path}, {shown.Count} shown (crate {runtime.ProjectCrate})");
            NativeToolboxInstaller.SetProjectComponents(DesignerText.ProjectToolboxTabName(runtime.ProjectName ?? runtime.ProjectCrate ?? "Project"), shown);
        }

        /// <summary>Logs what the project tab is built from, once per change (the Toolbox shows no message of its own).</summary>
        private void LogProjectToolbox(string state)
        {
            if (!string.Equals(state, _loggedProjectToolbox, StringComparison.Ordinal))
            {
                _loggedProjectToolbox = state;
                KubunoViewsLogHost.Current.WriteLine("[designer] Toolbox project tab: " + state + ".");
            }
        }

        /// <summary>The project controls of the last design build that come from OTHER crates (what "Choisir des éléments…" lists).</summary>
        internal IReadOnlyList<ComponentMeta> OtherCrateComponents()
        {
            var runtime = _runtimeSource?.Current;
            if (runtime?.RegistryPath is not { } path)
            {
                return Array.Empty<ComponentMeta>();
            }

            return ProjectComponentsFile.ReadLinked(path).Where(c => !string.Equals(c.CrateName, runtime.ProjectCrate, StringComparison.Ordinal)).ToList();
        }

        /// <summary>The key under which this project's Toolbox choices are kept, and the chosen set.</summary>
        internal (string? Key, HashSet<string> Chosen) ToolboxChoiceState()
        {
            var key = _runtimeSource?.Current?.ProjectKey;
            return (key, ToolboxChoices.Read(key));
        }

        /// <summary>Saves the chosen controls of other crates and refreshes the project tab.</summary>
        internal void SetToolboxChoices(IEnumerable<string> chosen)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ToolboxChoices.Write(_runtimeSource?.Current?.ProjectKey, chosen);
            UpdateProjectToolbox();
        }

        /// <summary>"Choisir des éléments…" (<see cref="IDesignerMenuActions.ChooseToolboxItems"/>).</summary>
        public void ChooseToolboxItems()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var (key, chosen) = ToolboxChoiceState();
            var dialog = new ChooseToolboxItemsDialog(OtherCrateComponents(), chosen);
            if (dialog.ShowModal() == true && key is not null)
            {
                SetToolboxChoices(dialog.Chosen);
            }
        }

        // ---- The component tray ----

        /// <summary>The view's non-visual components, from the Outline's element list and the registry.</summary>
        private void UpdateComponentTray(IReadOnlyList<OutlineNode>? roots = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_componentTray is null)
            {
                return;
            }

            if (roots is not null)
            {
                _trayCandidates = Flatten(roots).ToList();
            }

            var items = _trayCandidates
                .Where(c => Registry.Find(c.Tag) is { NonVisual: true })
                .Select(c => new ComponentTrayItem(c.Id, c.Tag, c.Name))
                .ToList();
            _componentTray.SetItems(items);
            _componentTray.SetSelection(_selectionSync?.CurrentElementIds ?? Array.Empty<string>());
        }

        private List<(string Id, string Tag, string? Name)> _trayCandidates = new List<(string, string, string?)>();

        private static IEnumerable<(string Id, string Tag, string? Name)> Flatten(IEnumerable<OutlineNode> nodes)
        {
            foreach (var node in nodes)
            {
                var tag = node.Detail ?? node.Name;
                yield return (node.ElementId, tag, node.Detail is null ? null : node.Name);
                foreach (var child in Flatten(node.Children))
                {
                    yield return child;
                }
            }
        }

        private void OnTrayItemSelected(object? sender, string elementId)
        {
            if (_selectionSync is { } sync)
            {
                _ = sync.SelectElementAsync(elementId);
            }
        }

        private void OnTrayItemActivated(object? sender, string elementId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var tag = _trayCandidates.FirstOrDefault(c => c.Id == elementId).Tag;
            if (tag is not null && Registry.Find(tag)?.DefaultEventFor(false) is { } defaultEvent)
            {
                CreateOrShowHandler(elementId, defaultEvent.Name, null);
            }
        }

        private void OnTrayDeleteRequested(object? sender, string elementId)
        {
#pragma warning disable VSSDK007 // see the constructor's own identical precedent/comment.
            ThreadHelper.JoinableTaskFactory.RunAsync(() => ApplyEncodedOpsAsync(new object[] { new { kind = "removeElement", elementId } }, "Delete")).FileAndForget("Kubuno/Designer/TrayDelete");
#pragma warning restore VSSDK007
        }
    }

    /// <summary>
    /// The controls of other crates a project shows in its Toolbox tab ("Choisir des éléments…", docs/EVENTS.md
    /// EVT-7b), kept per project in the user's Visual Studio settings (like WinForms' choices, per user).
    /// </summary>
    internal static class ToolboxChoices
    {
        private const string Collection = @"Kubuno\ToolboxChoices";

        public static HashSet<string> Read(string? projectKey)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(projectKey) || Store() is not { } store || !store.CollectionExists(Collection))
            {
                return set;
            }

            var value = store.GetString(Collection, Sanitize(projectKey!), string.Empty);
            foreach (var item in value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                set.Add(item);
            }

            return set;
        }

        public static void Write(string? projectKey, IEnumerable<string> chosen)
        {
            if (string.IsNullOrEmpty(projectKey) || Store() is not { } store)
            {
                return;
            }

            if (!store.CollectionExists(Collection))
            {
                store.CreateCollection(Collection);
            }

            store.SetString(Collection, Sanitize(projectKey!), string.Join(";", chosen.Distinct(StringComparer.Ordinal)));
        }

        private static string Sanitize(string key) => key.Replace('\\', '/').ToLowerInvariant();

        private static Microsoft.VisualStudio.Settings.WritableSettingsStore? Store()
        {
            try
            {
                return new Microsoft.VisualStudio.Shell.Settings.ShellSettingsManager(ServiceProvider.GlobalProvider)
                    .GetWritableSettingsStore(Microsoft.VisualStudio.Settings.SettingsScope.UserSettings);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
            {
                return null;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Kubuno.Desktop.Views.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.Desktop.Designer.PropertyBrowser
{
    /// <summary>
    /// Publishes the designer's selection to Visual Studio's native Properties window (F4), the supported
    /// way any document window does it: through the <see cref="ITrackSelection"/> service of the pane's
    /// own window frame (<c>STrackSelection</c>), with a <see cref="SelectionContainer"/> whose
    /// <see cref="SelectionContainer.SelectedObjects"/> is the selected element and whose
    /// <see cref="SelectionContainer.SelectableObjects"/> lists every element of the view - the latter is
    /// what fills the combo box at the top of the Properties window (<c>x:Name</c> + type, like WinForms).
    /// Picking an element in that combo comes back as <see cref="SelectionContainer.SelectedObjectsChanged"/>
    /// and is forwarded to <see cref="ElementPicked"/> so the designer selects it on the surface and in the
    /// XML too. Visual Studio itself switches the Properties window to this frame's selection whenever the
    /// frame is activated, so nothing is needed on focus changes.
    /// </summary>
    public sealed class PropertiesWindowPublisher : IDisposable
    {
        private readonly Func<ITrackSelection?> _trackSelection;
        private SelectionContainer? _container;
        private bool _publishing;

        public PropertiesWindowPublisher(Func<ITrackSelection?> trackSelection)
        {
            _trackSelection = trackSelection ?? throw new ArgumentNullException(nameof(trackSelection));
        }

        /// <summary>Raised (UI thread) when the user picks another element in the Properties window's combo box.</summary>
        public event EventHandler<string>? ElementPicked;

        /// <summary>The element currently shown, if any.</summary>
        public KbviewElementObject? Selected { get; private set; }

        /// <summary>
        /// Shows <paramref name="selected"/> (null: nothing selected) with <paramref name="selectable"/> in
        /// the combo box. <paramref name="selected"/> is added to the selectable list when missing (the
        /// Properties window requires the selected object to be one of them).
        /// </summary>
        public void Publish(KbviewElementObject? selected, IReadOnlyList<KbviewElementObject> selectable)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Publish(selected is null ? Array.Empty<KbviewElementObject>() : new[] { selected }, selectable);
        }

        /// <summary>
        /// Shows several elements at once (a multi-selection, docs/DESIGNER.md §13), the primary first: the
        /// Properties window then lists their common properties, blanks a value that differs between them, sets
        /// an edited value on every one of them, and leaves its element combo box empty - exactly its behavior
        /// with several WinForms controls selected.
        /// </summary>
        public void Publish(IReadOnlyList<KbviewElementObject> selectedObjects, IReadOnlyList<KbviewElementObject> selectable)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var list = selectable.ToList();
            foreach (var selectedObject in selectedObjects.Reverse())
            {
                var sameElement = list.FindIndex(o => string.Equals(o.ElementId, selectedObject.ElementId, StringComparison.Ordinal));
                if (sameElement >= 0)
                {
                    list[sameElement] = selectedObject;
                }
                else
                {
                    list.Insert(0, selectedObject);
                }
            }

            var selected = selectedObjects.Count > 0 ? selectedObjects[0] : null;

            if (_container is not null)
            {
                _container.SelectedObjectsChanged -= OnSelectedObjectsChanged;
            }

            var container = new SelectionContainer(selectableReadOnly: true, selectedReadOnly: false)
            {
                SelectableObjects = list,
                SelectedObjects = selectedObjects.Cast<object>().ToArray(),
            };
            container.SelectedObjectsChanged += OnSelectedObjectsChanged;
            _container = container;
            Selected = selected;

            var trackSelection = _trackSelection();
            if (trackSelection is null)
            {
                return;
            }

            _publishing = true;
            try
            {
                trackSelection.OnSelectChange(container);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                KubunoViewsLogHost.Current.WriteException("[designer] publishing the selection to the Properties window failed", ex);
            }
            finally
            {
                _publishing = false;
            }
        }

        /// <summary>Re-reads every visible value (after the buffer changed) without republishing the selection.</summary>
        public static void RefreshPropertyBrowser()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsUIShell)) is IVsUIShell shell)
            {
                // DISPID_UNKNOWN (-1): refresh every property.
                shell.RefreshPropertyBrowser(-1);
            }
        }

        private void OnSelectedObjectsChanged(object? sender, EventArgs e)
        {
            if (_publishing || sender is not SelectionContainer container)
            {
                return;
            }

            var picked = container.SelectedObjects?.OfType<KbviewElementObject>().FirstOrDefault();
            if (picked is not null)
            {
                Selected = picked;
                ElementPicked?.Invoke(this, picked.ElementId);
            }
        }

        public void Dispose()
        {
            if (_container is not null)
            {
                _container.SelectedObjectsChanged -= OnSelectedObjectsChanged;
                _container = null;
            }
        }
    }
}

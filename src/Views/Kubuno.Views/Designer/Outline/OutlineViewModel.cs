using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Kubuno.Views.Designer.Selection;

namespace Kubuno.Views.Designer.Outline
{
    /// <summary>
    /// The Document Outline tool window's whole state (docs/DESIGNER.md §1: "a WPF tree view bound to
    /// the existing LSP response"; §6's DSG-8 row: "click -&gt; select in both views"). Kept free of any
    /// WPF type so it is plain-unit-testable, the same shape as
    /// <c>UI.DesignerSplitViewModel</c> in this library (see its own doc comment); <see cref="OutlineView"/>
    /// is the only consumer.
    ///
    /// Implements <see cref="IOutlineSelectionTarget"/> so <see cref="Selection.SelectionSyncService"/>
    /// can drive its highlight (a surface/XML-caret-originated selection) WITHOUT this becoming a fourth
    /// place a selection could originate from by accident - <see cref="Select"/> never raises
    /// <see cref="NodeActivated"/>, only <see cref="ActivateNode"/> (a real user click, via
    /// <see cref="OutlineView"/>) does - see <see cref="IOutlineSelectionTarget.Select"/>'s own doc for
    /// why that split matters (loop prevention).
    /// </summary>
    public sealed class OutlineViewModel : INotifyPropertyChanged, IOutlineSelectionTarget
    {
        private readonly Dictionary<string, OutlineNodeViewModel> _byId = new Dictionary<string, OutlineNodeViewModel>(StringComparer.Ordinal);
        private IReadOnlyList<OutlineNodeViewModel> _roots = Array.Empty<OutlineNodeViewModel>();
        private string? _selectedElementId;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Raised when the user activates a node (a click - <see cref="OutlineView"/>'s own doing). A caller (this library's INTEGRATION.md) forwards this to <see cref="Selection.SelectionSyncService.SelectFromOutlineAsync"/>.</summary>
        public event EventHandler<string?>? NodeActivated;

        public IReadOnlyList<OutlineNodeViewModel> Roots => _roots;

        /// <summary>Rebuilds the whole tree from a fresh <c>textDocument/documentSymbol</c> response (already turned into <see cref="OutlineNode"/>s by <see cref="DocumentSymbolTreeBuilder"/>) - re-applies the current highlight afterwards if the previously-selected id still exists in the new tree.</summary>
        public void Load(IReadOnlyList<OutlineNode> nodes)
        {
            if (nodes is null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            _byId.Clear();
            _roots = nodes.Select(BuildViewModel).ToList();

            if (_selectedElementId is not null)
            {
                var toReapply = _selectedElementId;
                _selectedElementId = null; // Force ApplyHighlight to actually (re-)mark the node below.
                ApplyHighlight(toReapply);
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Roots)));
        }

        /// <inheritdoc />
        public void Select(string? elementId)
        {
            if (_selectedElementId == elementId)
            {
                return;
            }

            ApplyHighlight(elementId);
        }

        /// <summary>Called by <see cref="OutlineView"/> when the user selects a node - the one place THIS view originates a selection change.</summary>
        public void ActivateNode(string elementId)
        {
            if (elementId is null)
            {
                throw new ArgumentNullException(nameof(elementId));
            }

            ApplyHighlight(elementId);
            NodeActivated?.Invoke(this, elementId);
        }

        private OutlineNodeViewModel BuildViewModel(OutlineNode node)
        {
            var children = node.Children.Select(BuildViewModel).ToList();
            var viewModel = new OutlineNodeViewModel(node, children);
            _byId[node.ElementId] = viewModel;
            return viewModel;
        }

        private void ApplyHighlight(string? elementId)
        {
            if (_selectedElementId is not null && _byId.TryGetValue(_selectedElementId, out var previous))
            {
                previous.IsSelected = false;
            }

            _selectedElementId = elementId;

            if (elementId is not null && _byId.TryGetValue(elementId, out var next))
            {
                next.IsSelected = true;
            }
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Kubuno.Rust.Logic.SolutionExplorer;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Rust.SolutionExplorer
{
    /// <summary>
    /// The "Dependencies" node of a <c>.rsproj</c>, laid out like the one of an SDK-style .NET project:
    /// category nodes (<see cref="DependencyGroupTreeItem"/>) holding the dependencies
    /// (<see cref="DependencyTreeItem"/>), which expand into their own resolved dependencies, plus a
    /// child node per error (like .NET's diagnostic nodes). Filled from a
    /// <see cref="DependencyTreeModel"/> (<see cref="ProjectDependenciesSource"/>), merged in place on
    /// every refresh so expanded nodes stay expanded.
    /// </summary>
    internal sealed class DependenciesTreeItem : KubunoTreeItem, IContextMenuPattern, IPivotItemProviderPattern, IRefreshPattern
    {
        private readonly ObservableCollection<KubunoTreeItem> _children = new ObservableCollection<KubunoTreeItem>();
        private readonly Action _ensureLoaded;
        private DependencyTreeModel _model = DependencyTreeModel.Empty;
        private bool _loaded;

        public DependenciesTreeItem(Action ensureLoaded, ProjectDependenciesSource owner)
            : base(order: -1)
        {
            _ensureLoaded = ensureLoaded;
            Owner = owner;
        }

        public ProjectDependenciesSource Owner { get; }

        public override string Text => DependenciesText.Dependencies;

        public override string ToolTipText => _model.Diagnostics.Count > 0 ? string.Join(Environment.NewLine, _model.Diagnostics) : DependenciesText.DependenciesToolTip;

        public override ImageMoniker IconMoniker => Moniker(
            _model.Diagnostics.Count > 0 ? DependencyMonikerNames.RootError
            : _model.HasProblem ? DependencyMonikerNames.RootWarning
            : DependencyMonikerNames.Root);

        public override bool HasItems => !_loaded || _children.Count > 0;

        public override IEnumerable Items
        {
            get
            {
                _ensureLoaded();
                return _children;
            }
        }

        public override int Priority => -1;

        public IContextMenuController ContextMenuController => DependenciesNodeContextMenu.ForNode(Owner, category: null);

        /// <summary>"Scope to This" / "New Solution Explorer View": the scoped view is rooted at this same node.</summary>
        public object CreatePivotRootItem(IAttachedRelationship relationship) => this;

        /// <summary>Solution Explorer's Refresh button, with the node selected: re-read cargo metadata.</summary>
        public System.Threading.Tasks.Task RefreshAsync()
        {
            Owner.Reload();
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public void CancelLoad()
        {
        }

        public void SetModel(DependencyTreeModel model)
        {
            bool hadItems = HasItems;
            _loaded = true;
            _model = model;

            var fresh = new List<(string Key, Func<KubunoTreeItem> Create, Action<KubunoTreeItem> Update)>();
            for (int i = 0; i < model.Diagnostics.Count; i++)
            {
                var message = model.Diagnostics[i];
                var order = i - 100;
                fresh.Add(("diag|" + message, () => new DependencyDiagnosticTreeItem(message, order), _ => { }));
            }

            for (int i = 0; i < model.Groups.Count; i++)
            {
                var group = model.Groups[i];
                var order = i;
                fresh.Add(("group|" + group.Category, () => new DependencyGroupTreeItem(group, model, Owner, order), node => ((DependencyGroupTreeItem)node).Update(group, model, order)));
            }

            TreeMerger.Merge(_children, fresh);
            RaiseDisplayChanged();
            if (hadItems != HasItems)
            {
                RaisePropertyChanged(nameof(HasItems));
            }
        }
    }

    /// <summary>A category node: "Procedural macros", "Toolchain", "Crates", "Projects", "Git".</summary>
    internal sealed class DependencyGroupTreeItem : KubunoTreeItem, IContextMenuPattern, IPivotItemProviderPattern
    {
        private readonly ObservableCollection<KubunoTreeItem> _items = new ObservableCollection<KubunoTreeItem>();
        private readonly ProjectDependenciesSource _owner;
        private DependencyGroup _group;

        public DependencyGroupTreeItem(DependencyGroup group, DependencyTreeModel model, ProjectDependenciesSource owner, int order)
            : base(order)
        {
            _group = group;
            _owner = owner;
            Update(group, model, order);
        }

        public override string Text => _group.Text;

        public override ImageMoniker IconMoniker => Moniker(DependencyMonikerNames.Group(_group.Category));

        public override bool HasItems => _items.Count > 0;

        public override IEnumerable Items => _items;

        public IContextMenuController ContextMenuController => DependenciesNodeContextMenu.ForNode(_owner, _group.Category);

        public object CreatePivotRootItem(IAttachedRelationship relationship) => this;

        public void Update(DependencyGroup group, DependencyTreeModel model, int order)
        {
            _group = group;
            Order = order;
            DependencyTreeItem.MergeItems(_items, group.Items, model, _owner);
            RaiseDisplayChanged();
        }
    }

    /// <summary>
    /// One dependency (or the toolchain, or one of its sysroot crates). Expands lazily into the
    /// dependencies the resolve graph gives it; F4 shows <see cref="DependencyBrowseObject"/>.
    /// </summary>
    internal sealed class DependencyTreeItem : KubunoTreeItem, IContextMenuPattern, IBrowsablePattern, IPivotItemProviderPattern
    {
        private readonly ObservableCollection<KubunoTreeItem> _children = new ObservableCollection<KubunoTreeItem>();
        private readonly ProjectDependenciesSource _owner;
        private bool _realized;

        public DependencyTreeItem(DependencyItem item, DependencyTreeModel model, ProjectDependenciesSource owner, int order)
            : base(order)
        {
            Item = item;
            Model = model;
            _owner = owner;
        }

        public DependencyItem Item { get; private set; }

        public DependencyTreeModel Model { get; private set; }

        public override string Text => Item.Text;

        public override string ToolTipText => Item.ToolTip;

        public override ImageMoniker IconMoniker => Moniker(DependencyMonikerNames.Item(Item));

        // An optional dependency no feature enables is dimmed, not flagged.
        public override bool IsCut => Item.State == DependencyState.Inactive;

        public override ImageMoniker OverlayIconMoniker => Item.IsOutdated && !Item.IsYanked ? Moniker(DependencyMonikerNames.UpdateOverlay) : default;

        public override bool HasItems => _realized ? _children.Count > 0 : Model.HasChildren(Item);

        public override IEnumerable Items
        {
            get
            {
                if (!_realized)
                {
                    _realized = true;
                    MergeItems(_children, Model.ChildrenOf(Item), Model, _owner);
                }

                return _children;
            }
        }

        public IContextMenuController ContextMenuController => DependenciesNodeContextMenu.ForItem(_owner, this);

        public object GetBrowseObject() => new DependencyBrowseObject(Item);

        public object CreatePivotRootItem(IAttachedRelationship relationship) => this;

        /// <summary>Updates <paramref name="target"/> in place from <paramref name="items"/> (same node for the same dependency).</summary>
        public static void MergeItems(ObservableCollection<KubunoTreeItem> target, IReadOnlyList<DependencyItem> items, DependencyTreeModel model, ProjectDependenciesSource owner)
        {
            var fresh = new List<(string Key, Func<KubunoTreeItem> Create, Action<KubunoTreeItem> Update)>();
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var order = i;
                fresh.Add((item.MergeKey, () => new DependencyTreeItem(item, model, owner, order), node => ((DependencyTreeItem)node).Update(item, model, order)));
            }

            TreeMerger.Merge(target, fresh);
        }

        /// <summary>Re-applies the node's data (the registry status arrived, or a refresh re-read it).</summary>
        public void Refresh() => RaiseDisplayChanged();

        private void Update(DependencyItem item, DependencyTreeModel model, int order)
        {
            bool hadItems = HasItems;
            Item = item;
            Model = model;
            Order = order;
            if (_realized)
            {
                MergeItems(_children, model.ChildrenOf(item), model, _owner);
            }

            RaiseDisplayChanged();
            if (hadItems != HasItems)
            {
                RaisePropertyChanged(nameof(HasItems));
            }
        }
    }

    /// <summary>An error under "Dependencies" (e.g. <c>cargo metadata</c> failed), like .NET's diagnostic nodes.</summary>
    internal sealed class DependencyDiagnosticTreeItem : KubunoTreeItem
    {
        private readonly string _message;

        public DependencyDiagnosticTreeItem(string message, int order)
            : base(order)
        {
            _message = message;
        }

        public override string Text
        {
            get
            {
                // The first meaningful line; the whole message is the tooltip.
                var line = _message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(l => l.Trim().Length > 0) ?? _message;
                return line.Length > 200 ? line.Substring(0, 200) + "..." : line;
            }
        }

        public override string ToolTipText => _message;

        public override ImageMoniker IconMoniker => Moniker(DependencyMonikerNames.Diagnostic);
    }

    /// <summary>Keyed in-place update of a child collection (kept nodes keep their expansion state).</summary>
    public static class TreeMerger
    {
        public static void Merge(ObservableCollection<KubunoTreeItem> target, IReadOnlyList<(string Key, Func<KubunoTreeItem> Create, Action<KubunoTreeItem> Update)> fresh)
        {
            var existing = new Dictionary<string, KubunoTreeItem>(StringComparer.Ordinal);
            foreach (var node in target)
            {
                if (node.MergeKey != null && !existing.ContainsKey(node.MergeKey))
                {
                    existing[node.MergeKey] = node;
                }
            }

            var wanted = new List<KubunoTreeItem>(fresh.Count);
            foreach (var (key, create, update) in fresh)
            {
                if (existing.TryGetValue(key, out var node))
                {
                    existing.Remove(key);
                    update(node);
                }
                else
                {
                    node = create();
                    node.MergeKey = key;
                }

                wanted.Add(node);
            }

            for (int i = target.Count - 1; i >= 0; i--)
            {
                if (!wanted.Contains(target[i]))
                {
                    target.RemoveAt(i);
                }
            }

            for (int i = 0; i < wanted.Count; i++)
            {
                var index = target.IndexOf(wanted[i]);
                if (index < 0)
                {
                    target.Insert(i, wanted[i]);
                }
                else if (index != i)
                {
                    target.Move(index, i);
                }
            }
        }
    }
}

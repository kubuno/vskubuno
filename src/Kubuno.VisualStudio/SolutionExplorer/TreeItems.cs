using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using Kubuno.Cargo.Metadata;
using Kubuno.VisualStudio.Core.SolutionExplorer;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.VisualStudio.SolutionExplorer
{
    /// <summary>
    /// Base of every node this extension attaches to Solution Explorer (docs/RSPROJ.md lot 8), shaped
    /// like Roslyn's own symbol tree items: display text + image monikers, its own children as an
    /// attached collection (the provider hands the item back as its own <see cref="IAttachedCollectionSource"/>),
    /// and a priority/order so siblings keep source order instead of being sorted alphabetically.
    /// </summary>
    internal abstract class KubunoTreeItem : ITreeDisplayItem, ITreeDisplayItemWithImages, IAttachedCollectionSource, IPrioritizedComparable, IInteractionPatternProvider, INotifyPropertyChanged
    {
        private static readonly Dictionary<string, ImageMoniker> MonikerCache = new Dictionary<string, ImageMoniker>(StringComparer.Ordinal);

        protected KubunoTreeItem(int order)
        {
            Order = order;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Position among its siblings (source order, or group order).</summary>
        public int Order { get; set; }

        public abstract string Text { get; }

        public virtual string ToolTipText => Text;

        public string? StateToolTipText => null;

        public object? ToolTipContent => ToolTipText;

        public FontWeight FontWeight => FontWeights.Normal;

        public FontStyle FontStyle => FontStyles.Normal;

        public bool IsCut => false;

        public abstract ImageMoniker IconMoniker { get; }

        public virtual ImageMoniker ExpandedIconMoniker => IconMoniker;

        public ImageMoniker OverlayIconMoniker => default;

        public ImageMoniker StateIconMoniker => default;

        public object SourceItem => this;

        public virtual bool HasItems => false;

        public virtual IEnumerable Items => Array.Empty<object>();

        public virtual int Priority => 0;

        /// <summary>How Solution Explorer discovers what a node supports (display, invocation...), as with Roslyn's items.</summary>
        public TPattern? GetPattern<TPattern>()
            where TPattern : class => this as TPattern;

        /// <summary>Also the UI Automation name of the node (Solution Explorer uses ToString for it).</summary>
        public override string ToString() => Text;

        public int CompareTo(object obj) => obj is KubunoTreeItem other ? Order.CompareTo(other.Order) : 0;

        /// <summary>The Kubuno control icon (<see cref="ControlIcons"/>, KubunoControls.imagemanifest) of a <c>.kbview</c> element tag.</summary>
        internal static ImageMoniker ControlIcon(string? tag) => new ImageMoniker { Guid = ControlIcons.ImagesGuid, Id = ControlIcons.IdFor(tag) };

        /// <summary>A <c>KnownMonikers</c> property by name (see <see cref="SymbolMonikerNames"/>), cached.</summary>
        protected static ImageMoniker Moniker(string name)
        {
            lock (MonikerCache)
            {
                if (!MonikerCache.TryGetValue(name, out var moniker))
                {
                    var property = typeof(KnownMonikers).GetProperty(name, BindingFlags.Public | BindingFlags.Static)
                        ?? typeof(KnownMonikers).GetProperty(SymbolMonikerNames.Fallback, BindingFlags.Public | BindingFlags.Static);
                    moniker = property != null ? (ImageMoniker)property.GetValue(null) : KnownMonikers.QuestionMark;
                    MonikerCache[name] = moniker;
                }

                return moniker;
            }
        }

        protected void RaisePropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        protected void RaiseDisplayChanged()
        {
            RaisePropertyChanged(nameof(Text));
            RaisePropertyChanged(nameof(ToolTipText));
            RaisePropertyChanged(nameof(IconMoniker));
            RaisePropertyChanged(nameof(ExpandedIconMoniker));
        }
    }

    /// <summary>A Rust item or a <c>.kbview</c> element under its file; double-click navigates to it.</summary>
    internal sealed class SymbolTreeItem : KubunoTreeItem, IInvocationPattern
    {
        private readonly ObservableCollection<SymbolTreeItem> _children = new ObservableCollection<SymbolTreeItem>();

        public SymbolTreeItem(string filePath, SolutionSymbol symbol, int order)
            : base(order)
        {
            FilePath = filePath;
            Symbol = symbol;
            SymbolTreeMerger.Merge(_children, filePath, symbol.Children);
        }

        public string FilePath { get; }

        public SolutionSymbol Symbol { get; private set; }

        public override string Text => Symbol.DisplayText;

        public override string ToolTipText => Symbol.Kind == SolutionSymbolKind.ViewElement
            ? $"<{Symbol.ElementTag}> - line {Symbol.Line + 1}"
            : $"{Symbol.Name}{(string.IsNullOrEmpty(Symbol.Detail) ? string.Empty : " - " + Symbol.Detail)} ({Symbol.Visibility.ToString().ToLowerInvariant()}, line {Symbol.Line + 1})";

        // .kbview elements: the Kubuno control icon of their tag (the same as in the designer's Toolbox);
        // Rust symbols: Visual Studio's own catalog glyphs, like Roslyn.
        public override ImageMoniker IconMoniker => Symbol.Kind == SolutionSymbolKind.ViewElement
            ? ControlIcon(Symbol.ElementTag)
            : Moniker(SymbolMonikerNames.For(Symbol));

        public override bool HasItems => _children.Count > 0;

        public override IEnumerable Items => _children;

        public IInvocationController InvocationController => SymbolInvocationController.Instance;

        public bool CanPreview => false;

        /// <summary>Takes the values of a re-parse of the same symbol, keeping this node (and its expansion state).</summary>
        public void Update(SolutionSymbol symbol)
        {
            bool hadItems = HasItems;
            Symbol = symbol;
            SymbolTreeMerger.Merge(_children, FilePath, symbol.Children);
            RaiseDisplayChanged();
            if (hadItems != HasItems)
            {
                RaisePropertyChanged(nameof(HasItems));
            }
        }
    }

    /// <summary>Double-click / Enter on a symbol node: open the file at the symbol.</summary>
    internal sealed class SymbolInvocationController : IInvocationController
    {
        public static readonly SymbolInvocationController Instance = new SymbolInvocationController();

        public bool Invoke(IEnumerable<object> items, InputSource inputSource, bool preview)
        {
            foreach (var item in items)
            {
                if (item is SymbolTreeItem symbol)
                {
                    SymbolNavigator.Navigate(symbol.FilePath, symbol.Symbol.Line, symbol.Symbol.Column);
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Updates a child list in place from a fresh parse: a symbol matching an existing node (same
    /// kind, name and detail) reuses it, so a refresh after saving keeps expanded nodes expanded.
    /// </summary>
    internal static class SymbolTreeMerger
    {
        public static void Merge(ObservableCollection<SymbolTreeItem> target, string filePath, IReadOnlyList<SolutionSymbol> fresh)
        {
            var available = new List<SymbolTreeItem>(target);
            for (int i = 0; i < fresh.Count; i++)
            {
                var symbol = fresh[i];
                var existing = available.Find(item => item.Symbol.MergeKey == symbol.MergeKey);
                if (existing != null)
                {
                    available.Remove(existing);
                    existing.Order = i;
                    existing.Update(symbol);
                    int index = target.IndexOf(existing);
                    if (index != i)
                    {
                        target.Move(index, i);
                    }
                }
                else
                {
                    target.Insert(i, new SymbolTreeItem(filePath, symbol, i));
                }
            }

            while (target.Count > fresh.Count)
            {
                target.RemoveAt(target.Count - 1);
            }
        }
    }

    /// <summary>
    /// The "Dependencies" node of a <c>.rsproj</c> (like the "Dependencies" node of an SDK-style C#
    /// project) - the tree itself is read-only (from <c>cargo metadata</c>), but its own context menu
    /// ("Dépendance Cargo (crate)...") and a crate node's ("Supprimer") mutate <c>Cargo.toml</c>
    /// through <c>cargo add</c>/<c>cargo remove</c> (<see cref="Commands.AddCargoDependencyCommand"/>),
    /// never by hand - <see cref="DependenciesNodeContextMenu"/> is the
    /// <see cref="Microsoft.Internal.VisualStudio.PlatformUI.IContextMenuPattern"/> plumbing both use.
    /// </summary>
    internal sealed class DependenciesTreeItem : KubunoTreeItem, Microsoft.Internal.VisualStudio.PlatformUI.IContextMenuPattern
    {
        private readonly ObservableCollection<DependencyGroupTreeItem> _groups = new ObservableCollection<DependencyGroupTreeItem>();
        private readonly Action _ensureLoaded;
        private readonly IVsHierarchy _hierarchy;
        private bool _loaded;

        public DependenciesTreeItem(Action ensureLoaded, IVsHierarchy hierarchy)
            : base(order: -1)
        {
            _ensureLoaded = ensureLoaded;
            _hierarchy = hierarchy;
        }

        public override string Text => "Dependencies";

        public override string ToolTipText => "Cargo dependencies declared by Cargo.toml (read-only, from cargo metadata).";

        public override ImageMoniker IconMoniker => KnownMonikers.ReferenceGroup;

        public override bool HasItems => !_loaded || _groups.Count > 0;

        public override IEnumerable Items
        {
            get
            {
                _ensureLoaded();
                return _groups;
            }
        }

        public override int Priority => -1;

        public Microsoft.Internal.VisualStudio.PlatformUI.IContextMenuController ContextMenuController =>
            DependenciesNodeContextMenu.ForAdd(_hierarchy);

        public void SetGroups(IReadOnlyList<CargoDependencyGroup> groups)
        {
            bool hadItems = HasItems;
            _loaded = true;
            _groups.Clear();
            for (int i = 0; i < groups.Count; i++)
            {
                _groups.Add(new DependencyGroupTreeItem(groups[i], i, _hierarchy));
            }

            if (hadItems != HasItems)
            {
                RaisePropertyChanged(nameof(HasItems));
            }
        }
    }

    internal sealed class DependencyGroupTreeItem : KubunoTreeItem
    {
        private readonly List<DependencyTreeItem> _dependencies = new List<DependencyTreeItem>();
        private readonly CargoDependencyGroup _group;

        public DependencyGroupTreeItem(CargoDependencyGroup group, int order, IVsHierarchy hierarchy)
            : base(order)
        {
            _group = group;
            for (int i = 0; i < group.Dependencies.Count; i++)
            {
                _dependencies.Add(new DependencyTreeItem(group.Dependencies[i], i, hierarchy));
            }
        }

        public override string Text => _group.DisplayText;

        public override ImageMoniker IconMoniker => KnownMonikers.PackageFolderClosed;

        public override ImageMoniker ExpandedIconMoniker => KnownMonikers.PackageFolderOpened;

        public override bool HasItems => _dependencies.Count > 0;

        public override IEnumerable Items => _dependencies;
    }

    internal sealed class DependencyTreeItem : KubunoTreeItem, Microsoft.Internal.VisualStudio.PlatformUI.IContextMenuPattern
    {
        private readonly CargoDependency _dependency;
        private readonly IVsHierarchy _hierarchy;

        public DependencyTreeItem(CargoDependency dependency, int order, IVsHierarchy hierarchy)
            : base(order)
        {
            _dependency = dependency;
            _hierarchy = hierarchy;
        }

        public override string Text => CargoDependencyGroups.DisplayText(_dependency);

        public override string ToolTipText =>
            (_dependency.Path ?? _dependency.Source ?? _dependency.Name)
            + (_dependency.Target != null ? " [" + _dependency.Target + "]" : string.Empty);

        public override ImageMoniker IconMoniker => _dependency.Path != null ? KnownMonikers.Reference : KnownMonikers.PackageReference;

        public Microsoft.Internal.VisualStudio.PlatformUI.IContextMenuController ContextMenuController =>
            DependenciesNodeContextMenu.ForRemove(_hierarchy, _dependency.Rename ?? _dependency.Name);
    }
}

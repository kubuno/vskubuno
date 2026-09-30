using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using Kubuno.Rust.Logic.SolutionExplorer;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Rust.SolutionExplorer
{
    /// <summary>
    /// Base of every node this extension attaches to Solution Explorer (docs/RSPROJ.md lot 8), shaped
    /// like Roslyn's own symbol tree items: display text + image monikers, its own children as an
    /// attached collection (the provider hands the item back as its own <see cref="IAttachedCollectionSource"/>),
    /// and a priority/order so siblings keep source order instead of being sorted alphabetically.
    /// </summary>
    public abstract class KubunoTreeItem : ITreeDisplayItem, ITreeDisplayItemWithImages, IAttachedCollectionSource, IPrioritizedComparable, IInteractionPatternProvider, INotifyPropertyChanged
    {
        private static readonly Dictionary<string, ImageMoniker> MonikerCache = new Dictionary<string, ImageMoniker>(StringComparer.Ordinal);

        protected KubunoTreeItem(int order)
        {
            Order = order;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Position among its siblings (source order, or group order).</summary>
        public int Order { get; set; }

        /// <summary>Identity across refreshes (<see cref="TreeMerger"/>), so a kept node keeps its expansion state.</summary>
        public string? MergeKey { get; set; }

        public abstract string Text { get; }

        public virtual string ToolTipText => Text;

        public virtual string? StateToolTipText => null;

        public object? ToolTipContent => ToolTipText;

        public FontWeight FontWeight => FontWeights.Normal;

        public FontStyle FontStyle => FontStyles.Normal;

        public virtual bool IsCut => false;

        public abstract ImageMoniker IconMoniker { get; }

        public virtual ImageMoniker ExpandedIconMoniker => IconMoniker;

        public virtual ImageMoniker OverlayIconMoniker => default;

        public virtual ImageMoniker StateIconMoniker => default;

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

        /// <summary>A <c>KnownMonikers</c> property by name (see <see cref="SymbolMonikerNames"/>), cached.</summary>
        public static ImageMoniker Moniker(string name)
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
            RaisePropertyChanged(nameof(ToolTipContent));
            RaisePropertyChanged(nameof(IconMoniker));
            RaisePropertyChanged(nameof(ExpandedIconMoniker));
            RaisePropertyChanged(nameof(OverlayIconMoniker));
            RaisePropertyChanged(nameof(StateIconMoniker));
            RaisePropertyChanged(nameof(StateToolTipText));
            RaisePropertyChanged(nameof(IsCut));
        }
    }

    /// <summary>A Rust item, or an element of another language's file (its provider's icon), under its file; double-click navigates to it.</summary>
    internal sealed class SymbolTreeItem : KubunoTreeItem, IInvocationPattern
    {
        private readonly ObservableCollection<SymbolTreeItem> _children = new ObservableCollection<SymbolTreeItem>();

        public SymbolTreeItem(string filePath, SolutionSymbol symbol, int order, Extensibility.ISolutionSymbolProvider? provider)
            : base(order)
        {
            FilePath = filePath;
            Symbol = symbol;
            Provider = provider;
            SymbolTreeMerger.Merge(_children, filePath, symbol.Children, provider);
        }

        public string FilePath { get; }

        /// <summary>The layer that provides this file's symbols, or null for a Rust file.</summary>
        public Extensibility.ISolutionSymbolProvider? Provider { get; }

        public SolutionSymbol Symbol { get; private set; }

        public override string Text => Symbol.DisplayText;

        public override string ToolTipText => Provider is not null
            ? Provider.GetToolTip(Symbol)
            : $"{Symbol.Name}{(string.IsNullOrEmpty(Symbol.Detail) ? string.Empty : " - " + Symbol.Detail)} ({Symbol.Visibility.ToString().ToLowerInvariant()}, line {Symbol.Line + 1})";

        // Other files' elements: their provider's icon (the desktop layer's Kubuno control icons for .kbview elements);
        // Rust symbols: Visual Studio's own catalog glyphs, like Roslyn.
        public override ImageMoniker IconMoniker => Provider is not null
            ? Provider.GetIcon(Symbol)
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
            SymbolTreeMerger.Merge(_children, FilePath, symbol.Children, Provider);
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
                    SymbolNavigator.Navigate(symbol.FilePath, symbol.Symbol.Line, symbol.Symbol.Column, symbol.Provider);
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
        public static void Merge(ObservableCollection<SymbolTreeItem> target, string filePath, IReadOnlyList<SolutionSymbol> fresh, Extensibility.ISolutionSymbolProvider? provider)
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
                    target.Insert(i, new SymbolTreeItem(filePath, symbol, i, provider));
                }
            }

            while (target.Count > fresh.Count)
            {
                target.RemoveAt(target.Count - 1);
            }
        }
    }
}

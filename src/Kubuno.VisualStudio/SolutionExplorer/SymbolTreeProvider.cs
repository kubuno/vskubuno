using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Utilities;

namespace Kubuno.VisualStudio.SolutionExplorer
{
    /// <summary>
    /// Attaches Kubuno nodes to Solution Explorer (docs/RSPROJ.md lot 8), through the same seam Roslyn
    /// uses for the type/member nodes under a C# file (<c>RootSymbolTreeItemSourceProvider</c>): an
    /// exported <see cref="IAttachedCollectionSourceProvider"/> asked, for every node, whether it has
    /// extra children for the <see cref="KnownRelationships.Contains"/> relationship. Sources are
    /// cached per node and disposed (file watchers stopped) when the node goes away.
    /// </summary>
    internal abstract class HierarchyItemSourceProviderBase : AttachedCollectionSourceProvider<object>
    {
        private readonly ConditionalWeakTable<object, IAttachedCollectionSource> _sources = new ConditionalWeakTable<object, IAttachedCollectionSource>();

        protected override IAttachedCollectionSource? CreateCollectionSource(object item, string relationshipName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (relationshipName != KnownRelationships.Contains)
            {
                return null;
            }

            if (item is KubunoTreeItem own)
            {
                return OwnsTreeItems ? own : null;
            }

            if (item is not IVsHierarchyItem hierarchyItem)
            {
                return null;
            }

            if (_sources.TryGetValue(item, out var cached))
            {
                return cached;
            }

            var source = CreateForHierarchyItem(hierarchyItem);
            if (source == null)
            {
                return null;
            }

            _sources.Add(item, source);
            if (hierarchyItem is ISupportDisposalNotification disposal && source is IDisposable disposable)
            {
                PropertyChangedEventHandler? handler = null;
                handler = (_, e) =>
                {
                    if (e.PropertyName == nameof(ISupportDisposalNotification.IsDisposed) && disposal.IsDisposed)
                    {
                        disposal.PropertyChanged -= handler;
                        disposable.Dispose();
                    }
                };
                disposal.PropertyChanged += handler;
            }

            return source;
        }

        protected override IEnumerable<IAttachedRelationship> GetRelationships(object item) => Enumerable.Empty<IAttachedRelationship>();

        /// <summary>Whether this provider answers for the children of our own nodes (exactly one provider must).</summary>
        protected abstract bool OwnsTreeItems { get; }

        protected abstract IAttachedCollectionSource? CreateForHierarchyItem(IVsHierarchyItem item);
    }

    /// <summary>
    /// A <c>.rs</c> file gets its Rust items (<see cref="RustSymbolQuery"/>), a <c>.kbview</c> file its
    /// element tree (<see cref="KbviewSymbolQuery"/>) - in a <c>.rsproj</c> as well as in Open Folder,
    /// since both hand out <see cref="IVsHierarchyItem"/>s. Ordered after the hierarchy's own
    /// children, so a nested code-behind file comes first (like <c>Form1.Designer.cs</c> above the
    /// <c>Form1</c> class node). Also the children source of every node this extension creates.
    /// </summary>
    [Export(typeof(IAttachedCollectionSourceProvider))]
    [Name(nameof(SymbolTreeProvider))]
    [Order(After = HierarchyItemsProviderNames.Contains)]
    internal sealed class SymbolTreeProvider : HierarchyItemSourceProviderBase
    {
        private readonly RustSymbolQuery _rustQuery = new RustSymbolQuery();
        private readonly KbviewSymbolQuery _kbviewQuery = new KbviewSymbolQuery();

        protected override bool OwnsTreeItems => true;

        protected override IAttachedCollectionSource? CreateForHierarchyItem(IVsHierarchyItem item)
        {
            if (item.HierarchyIdentity.IsRoot)
            {
                return null;
            }

            var path = item.CanonicalName;
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            string extension;
            try
            {
                extension = Path.GetExtension(path);
            }
            catch (ArgumentException)
            {
                return null;
            }

            ISymbolQuery? query =
                string.Equals(extension, ".rs", StringComparison.OrdinalIgnoreCase) ? _rustQuery
                : string.Equals(extension, ".kbview", StringComparison.OrdinalIgnoreCase) ? _kbviewQuery
                : null;
            return query != null && Path.IsPathRooted(path) ? new FileSymbolsSource(item, path, query) : null;
        }
    }

    /// <summary>
    /// A <c>.rsproj</c> project node gets a read-only "Dependencies" node from <c>cargo metadata</c>,
    /// ordered before the project's folders and files like the "Dependencies" node of a C# project.
    /// </summary>
    [Export(typeof(IAttachedCollectionSourceProvider))]
    [Name(nameof(DependenciesTreeProvider))]
    [Order(Before = HierarchyItemsProviderNames.Contains)]
    internal sealed class DependenciesTreeProvider : HierarchyItemSourceProviderBase
    {
        private const string RustProjectCapability = "RustProjectSystem";

        protected override bool OwnsTreeItems => false;

        protected override IAttachedCollectionSource? CreateForHierarchyItem(IVsHierarchyItem item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var identity = item.HierarchyIdentity;
            if (!(identity.IsRoot || (identity.IsNestedItem && identity.NestedItemID == (uint)VSConstants.VSITEMID.Root)))
            {
                return null;
            }

            var project = identity.NestedHierarchy ?? identity.Hierarchy;
            return project != null && project.IsCapabilityMatch(RustProjectCapability)
                ? new ProjectDependenciesSource(item, project)
                : null;
        }
    }
}

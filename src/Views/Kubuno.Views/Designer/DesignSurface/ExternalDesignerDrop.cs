using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>Where an external drop landed on a view (see <see cref="IExternalDropSource.Plan"/>).</summary>
    public sealed class ExternalDropRequest
    {
        public ExternalDropRequest(string documentPath, string documentText, string parentId, int? index, double? x, double? y)
        {
            DocumentPath = documentPath;
            DocumentText = documentText;
            ParentId = parentId;
            Index = index;
            X = x;
            Y = y;
        }

        /// <summary>The <c>.kbview</c> file.</summary>
        public string DocumentPath { get; }

        /// <summary>The view's current text (the editor buffer, not the disk).</summary>
        public string DocumentText { get; }

        /// <summary>The container under the drop point (stable id, <c>""</c> = the root).</summary>
        public string ParentId { get; }

        /// <summary>The insertion index among its children (null: append).</summary>
        public int? Index { get; }

        /// <summary>The drop point, DIP, local to the container - null for a non-positioned container or a keyboard insertion.</summary>
        public double? X { get; }

        public double? Y { get; }

        /// <summary>The designer's component registry (which property of a control a dropped member binds), when known.</summary>
        public Registry.ComponentRegistry? Registry { get; set; }
    }

    /// <summary>One <c>insertFragment</c> an external drop applies.</summary>
    public sealed class ExternalDropInsertion
    {
        public ExternalDropInsertion(string parentId, int index, string xml)
        {
            ParentId = parentId;
            Index = index;
            Xml = xml;
        }

        public string ParentId { get; }

        public int Index { get; }

        public string Xml { get; }
    }

    /// <summary>What an external drop inserts: every fragment is computed against the same version of the view and applied as ONE undo unit.</summary>
    public sealed class ExternalDropResult
    {
        public ExternalDropResult(IReadOnlyList<ExternalDropInsertion> insertions, string description, string? selectElementId)
        {
            Insertions = insertions;
            Description = description;
            SelectElementId = selectElementId;
        }

        public IReadOnlyList<ExternalDropInsertion> Insertions { get; }

        /// <summary>Attributes the drop sets on existing elements (a member dropped onto a control binds it, docs/DESIGNER.md "Data bindings").</summary>
        public IReadOnlyList<(string ElementId, string Name, string Value)> AttributeEdits { get; set; } = Array.Empty<(string, string, string)>();

        public string Description { get; }

        public string? SelectElementId { get; }
    }

    /// <summary>Something dragged from outside the Toolbox (the Data Sources window) that plans its own insertion.</summary>
    public interface IExternalDropSource
    {
        /// <summary>The insertions for <paramref name="request"/>, or null with <paramref name="error"/> (shown in the status bar).</summary>
        ExternalDropResult? Plan(ExternalDropRequest request, out string? error);
    }

    /// <summary>
    /// Lets another feature of the extension drop something richer than one Toolbox component onto a <c>.kbview</c> designer
    /// (docs/DATA.md DATA-6: a table from the Data Sources window becomes several components and controls).
    /// <para>The drop target of the design surface lives in the surface process and only understands Toolbox items
    /// (<see cref="Toolbox.ToolboxItemFormat"/>). So an external drag carries, next to its own payload, a Toolbox item naming a
    /// placeholder component (<see cref="MarkerComponent"/>): the surface gives the usual feedback (copy cursor, placement
    /// marker) and, on drop, answers the usual <c>insertChild</c> of that placeholder at the drop point. The drag's owner calls
    /// <see cref="BeginDrag"/> / <see cref="EndDrag"/> around its <c>DoDragDrop</c>; the editing coordinator of the view hands
    /// that <c>insertChild</c> to <see cref="TryTakePending"/> first, and when a drag is pending, asks the source to plan the real
    /// insertion at the same place instead - so the placeholder is never written.</para>
    /// Everything here runs on the UI thread.
    /// </summary>
    public static class ExternalDesignerDrop
    {
        /// <summary>The Toolbox component an external drag carries for the surface's feedback (known to every registry, accepted by any container).</summary>
        public const string MarkerComponent = "Label";

        /// <summary>How long after its <c>DoDragDrop</c> returned a dropped source still claims the surface's answer (it arrives asynchronously).</summary>
        private static readonly TimeSpan Grace = TimeSpan.FromSeconds(10);

        private static readonly List<WeakReference<IExternalDropTarget>> Targets = new List<WeakReference<IExternalDropTarget>>();
        private static IExternalDropSource? _pending;
        private static bool _dragging;
        private static Stopwatch? _droppedAt;

        /// <summary>A drag of <paramref name="source"/> starts (call right before <c>DoDragDrop</c>).</summary>
        public static void BeginDrag(IExternalDropSource source)
        {
            _pending = source ?? throw new ArgumentNullException(nameof(source));
            _dragging = true;
            _droppedAt = null;
        }

        /// <summary>The drag ended; <paramref name="dropped"/> when the target accepted it (the surface's answer may still be on its way).</summary>
        public static void EndDrag(bool dropped)
        {
            _dragging = false;
            if (dropped && _pending != null)
            {
                _droppedAt = Stopwatch.StartNew();
            }
            else
            {
                _pending = null;
                _droppedAt = null;
            }
        }

        /// <summary>The pending external source for a surface drop of <paramref name="xml"/> (consumed), or null for an ordinary Toolbox drop.</summary>
        internal static IExternalDropSource? TryTakePending(string? xml)
        {
            if (_pending is null || xml is null || !xml.TrimStart().StartsWith("<" + MarkerComponent, StringComparison.Ordinal))
            {
                return null;
            }

            if (!_dragging && (_droppedAt is null || _droppedAt.Elapsed > Grace))
            {
                _pending = null;
                _droppedAt = null;
                return null;
            }

            var source = _pending;
            _pending = null;
            _droppedAt = null;
            _dragging = false;
            return source;
        }

        /// <summary>The open designers' documents (full paths), most recently registered first.</summary>
        public static IReadOnlyList<string> OpenDocuments
        {
            get
            {
                Prune();
                return Targets.Select(t => t.TryGetTarget(out var target) ? target.DocumentPath : null).Where(p => p != null).Cast<string>().ToList();
            }
        }

        /// <summary>What the bindings of the designer of <paramref name="documentPath"/> can name (null: no open designer for it).</summary>
        public static Bindings.BindingSourceSchema? BindingSourcesOf(string documentPath)
        {
            Prune();
            foreach (var reference in Targets)
            {
                if (reference.TryGetTarget(out var target) && string.Equals(target.DocumentPath, documentPath, StringComparison.OrdinalIgnoreCase))
                {
                    return target.BindingSources();
                }
            }

            return null;
        }

        /// <summary>
        /// Inserts <paramref name="source"/> into the designer of <paramref name="documentPath"/> (at a free place: a keyboard / double-click
        /// insertion). False when that document has no open designer.
        /// </summary>
        public static bool InsertInto(string documentPath, IExternalDropSource source)
        {
            Prune();
            foreach (var reference in Targets)
            {
                if (reference.TryGetTarget(out var target) && string.Equals(target.DocumentPath, documentPath, StringComparison.OrdinalIgnoreCase))
                {
                    target.InsertExternal(source);
                    return true;
                }
            }

            return false;
        }

        internal static void Register(IExternalDropTarget target)
        {
            Prune();
            Targets.Insert(0, new WeakReference<IExternalDropTarget>(target));
        }

        internal static void Unregister(IExternalDropTarget target)
        {
            Targets.RemoveAll(r => !r.TryGetTarget(out var t) || ReferenceEquals(t, target));
        }

        private static void Prune() => Targets.RemoveAll(r => !r.TryGetTarget(out _));
    }

    /// <summary>A designer that accepts external insertions (the editing coordinator of an open view).</summary>
    internal interface IExternalDropTarget
    {
        /// <summary>The view's file, or null when it is not known (yet).</summary>
        string? DocumentPath { get; }

        void InsertExternal(IExternalDropSource source);

        /// <summary>What the view's bindings can name (docs/DESIGNER.md "Data bindings"): the Data Sources window lists its view-model members.</summary>
        Bindings.BindingSourceSchema BindingSources();
    }
}

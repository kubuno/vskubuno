using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Kubuno.Views.Logging;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>
    /// External drops (<see cref="ExternalDesignerDrop"/>, docs/DATA.md DATA-6): a table or a column dragged from the Data Sources
    /// window. The surface answers the drag's placeholder Toolbox item with an ordinary <c>insertChild</c> at the drop point; this
    /// half turns it into the source's own plan - several <c>insertFragment</c> ops computed against the same buffer version and
    /// applied as one undo unit by the one edit pipeline (<see cref="ApplyEncodedOpsAsync"/>).
    /// </summary>
    internal sealed partial class DesignSurfaceEditingCoordinator : IExternalDropTarget
    {
        private static readonly Regex XAttribute = new Regex("\\bX=\"(-?[0-9.]+)\"", RegexOptions.Compiled);
        private static readonly Regex YAttribute = new Regex("\\bY=\"(-?[0-9.]+)\"", RegexOptions.Compiled);

        /// <summary>The view's file (UI thread).</summary>
        string? IExternalDropTarget.DocumentPath
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var uri = _documentUri ?? TryGetDocumentUri();
                return uri is not null && Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile ? parsed.LocalPath : null;
            }
        }

        /// <summary>Called first for a surface <c>insertChild</c>: true when it was the placeholder of an external drag (then handled here).</summary>
        private bool TryHandleExternalDrop(DesignSurfaceDragDropOp op)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (op.Kind != DesignSurfaceDragDropOpKind.InsertChild || ExternalDesignerDrop.TryTakePending(op.Xml) is not { } source)
            {
                return false;
            }

            double? x = null;
            double? y = null;
            if (op.Xml is { } xml && XAttribute.Match(xml) is { Success: true } mx && YAttribute.Match(xml) is { Success: true } my &&
                double.TryParse(mx.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) &&
                double.TryParse(my.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var py))
            {
                x = px;
                y = py;
            }

            RunExternal(source, op.ParentId ?? string.Empty, op.Index, x, y);
            return true;
        }

        /// <summary>What the view's bindings can name (the Data Sources window's view-model node).</summary>
        Bindings.BindingSourceSchema IExternalDropTarget.BindingSources()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetBindingSources(string.Empty);
        }

        /// <summary>A keyboard / double-click insertion from outside: at a free place of the root.</summary>
        void IExternalDropTarget.InsertExternal(IExternalDropSource source)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            RunExternal(source, string.Empty, null, null, null);
        }

        private void RunExternal(IExternalDropSource source, string parentId, int? index, double? x, double? y)
        {
#pragma warning disable VSSDK007 // see the constructor's own identical precedent/comment.
            ThreadHelper.JoinableTaskFactory.RunAsync(() => ApplyExternalAsync(source, parentId, index, x, y)).FileAndForget("Kubuno/Designer/ExternalDrop");
#pragma warning restore VSSDK007
        }

        private async Task ApplyExternalAsync(IExternalDropSource source, string parentId, int? index, double? x, double? y)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var path = ((IExternalDropTarget)this).DocumentPath ?? string.Empty;
            ExternalDropResult? plan;
            string? error;
            try
            {
                plan = source.Plan(new ExternalDropRequest(path, GetCurrentText(), parentId, index, x, y) { Registry = Registry }, out error);
            }
            catch (Exception ex)
            {
                KubunoViewsLogHost.Current.WriteException("[designer] external drop failed", ex);
                return;
            }

            if (plan is null || (plan.Insertions.Count == 0 && plan.AttributeEdits.Count == 0))
            {
                if (!string.IsNullOrEmpty(error))
                {
                    ShowStatus(error!);
                }

                return;
            }

            // The attributes first (a drop onto a control binds it), then the insertions: one request, one undo unit.
            var ops = plan.AttributeEdits.Select(e => (object)new { kind = "setAttribute", elementId = e.ElementId, name = e.Name, value = e.Value })
                .Concat(plan.Insertions.Select(i => (object)new { kind = "insertFragment", parentId = i.ParentId, index = i.Index, xml = i.Xml }))
                .ToList();
            if (await ApplyEncodedOpsAsync(ops, plan.Description) && plan.SelectElementId is { } select)
            {
                await SelectInsertedAsync(select);
            }
        }
    }
}

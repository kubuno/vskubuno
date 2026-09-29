namespace Kubuno.VisualStudio.Designer.DesignSurface
{
    /// <summary>
    /// Creates one <see cref="IDesignSurfaceHost"/> per open designer pane (a design surface is
    /// per-document, unlike e.g. the language server, which is shared - docs/DESIGNER.md §3 spawns one
    /// <c>kubuno-views-designer</c> process per embedded surface). A factory, rather than a single
    /// shared instance, so <see cref="DesignSurfaceHostFactoryHost.Current"/> can be swapped for DSG-7's
    /// real factory without any change to <see cref="UI.DesignerSplitView"/> or
    /// <see cref="EditorFactory.DesignerWindowPane"/>.
    /// </summary>
    public interface IDesignSurfaceHostFactory
    {
        /// <summary>A host for one pane; <paramref name="document"/> (null outside Visual Studio) lets the factory pick the runtime of the document's project (docs/DESIGNER.md section 15).</summary>
        IDesignSurfaceHost Create(DesignSurfaceDocument? document);
    }
}

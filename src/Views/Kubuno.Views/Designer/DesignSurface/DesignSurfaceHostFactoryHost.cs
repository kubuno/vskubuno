using System;
using System.Collections.Generic;

namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>
    /// Static gateway to the active <see cref="IDesignSurfaceHostFactory"/> - the one seam DSG-7 needs
    /// to touch to go from a placeholder Design pane to the real embedded surface. Nothing in this
    /// library (the editor factory, the window pane, the split view) constructs
    /// <see cref="PlaceholderDesignSurfaceHost"/> directly; they all go through <see cref="For"/>,
    /// so a target layer only has to set <see cref="Current"/> or add a target factory
    /// (<see cref="AddTargetFactory"/>) rather than edit <see cref="EditorFactory.DesignerWindowPane"/> or
    /// <see cref="UI.DesignerSplitView"/>.
    ///
    /// Same shape as <c>Kubuno.Views.Options.KubunoViewsOptionsHost</c>/
    /// <c>KubunoViewsLogHost</c>: a mutable static property with a safe, working default, because the
    /// window pane can be constructed before whatever sets a real factory has run.
    /// </summary>
    /// <remarks>
    /// Several targets share the designer (docs/WEB-VIEWS.md section 4.1): the desktop layer sets <see cref="Current"/> (the
    /// Rust surface, the default for every view), the web layer adds a factory that claims the views of web projects
    /// (WV-9b). <see cref="For"/> picks per document: the first target factory that handles it, else <see cref="Current"/>.
    /// </remarks>
    public static class DesignSurfaceHostFactoryHost
    {
        private static readonly object s_gate = new object();
        private static IDesignSurfaceHostFactory _current = PlaceholderDesignSurfaceHostFactory.Instance;
        private static readonly List<ITargetDesignSurfaceHostFactory> s_targets = new List<ITargetDesignSurfaceHostFactory>();

        /// <summary>The default factory (documents no target factory claims).</summary>
        public static IDesignSurfaceHostFactory Current
        {
            get => _current;
            set => _current = value ?? PlaceholderDesignSurfaceHostFactory.Instance;
        }

        /// <summary>Adds a factory that renders the documents it <see cref="ITargetDesignSurfaceHostFactory.Handles"/> (checked before <see cref="Current"/>).</summary>
        public static void AddTargetFactory(ITargetDesignSurfaceHostFactory factory)
        {
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            lock (s_gate)
            {
                if (!s_targets.Contains(factory))
                {
                    s_targets.Add(factory);
                }
            }
        }

        /// <summary>Removes a factory added with <see cref="AddTargetFactory"/> (tests).</summary>
        public static void RemoveTargetFactory(ITargetDesignSurfaceHostFactory factory)
        {
            lock (s_gate)
            {
                s_targets.Remove(factory);
            }
        }

        /// <summary>The factory for <paramref name="document"/>: the first target factory that handles it, else <see cref="Current"/>.</summary>
        public static IDesignSurfaceHostFactory For(DesignSurfaceDocument? document)
        {
            if (document is not null)
            {
                ITargetDesignSurfaceHostFactory[] targets;
                lock (s_gate)
                {
                    targets = s_targets.ToArray();
                }

                foreach (var target in targets)
                {
                    try
                    {
                        if (target.Handles(document))
                        {
                            return target;
                        }
                    }
                    catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
                    {
                        Kubuno.Views.Logging.KubunoViewsLogHost.Current.WriteException("[designer] a target design surface factory failed to inspect " + document.Path, ex);
                    }
                }
            }

            return _current;
        }
    }

    /// <summary>
    /// A design surface factory of one target (the web's WebView2 surface): it renders only the documents it claims, the
    /// others go to <see cref="DesignSurfaceHostFactoryHost.Current"/>.
    /// </summary>
    public interface ITargetDesignSurfaceHostFactory : IDesignSurfaceHostFactory
    {
        /// <summary>Whether this factory renders <paramref name="document"/> (e.g. a view of a web project). Fast: no network.</summary>
        bool Handles(DesignSurfaceDocument document);
    }
}

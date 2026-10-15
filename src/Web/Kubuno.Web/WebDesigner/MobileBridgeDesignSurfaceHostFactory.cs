using System;
using System.IO;
using Kubuno.Shared.Logging;
using Kubuno.Views.Designer.DesignSurface;
using Kubuno.Web.Logic.WebDesigner;

namespace Kubuno.Web.WebDesigner
{
    /// <summary>
    /// Bridges the <c>.kbview</c> / <c>.kbcontrol</c> views of a <b>mobile</b> Rust crate (a <c>kubuno-mobile-*</c> package,
    /// e.g. <c>kubuno-mobile-render</c>) to the web <see cref="WebDesignSurfaceHost"/>, so a mobile view is edited in Visual
    /// Studio with the <b>web</b> designer's hover/selection (the design surface the user prefers) instead of the desktop's
    /// Rust surface. It renders through the bundled design host (host elements only), which is all a primitive mobile view
    /// needs today.
    /// </summary>
    /// <remarks>
    /// This is the "reuse the web surface" bridge (Path B, step 1): the mobile portable renderer (<c>vello</c>) does not host
    /// its own design surface yet. When <c>Kubuno.Mobile</c> ships a native <see cref="IProtocolDesignSurfaceHost"/> over the
    /// vello renderer (same stable-id <see cref="DesignSurfaceProtocol"/>), this factory is removed and the mobile layer adds
    /// its own, exactly as the desktop and web layers do. The bridge lives in the web layer because only it can see
    /// <see cref="WebDesignSurfaceHost"/> (internal); <c>Kubuno.Mobile</c> never references <c>Kubuno.Web</c>.
    /// Registered after <see cref="WebDesignSurfaceHostFactory"/>, so a real web view still wins.
    /// </remarks>
    internal sealed class MobileBridgeDesignSurfaceHostFactory : ITargetDesignSurfaceHostFactory
    {
        public bool Handles(DesignSurfaceDocument document) => MobileCrateRoot(document.Path) is not null;

        public IDesignSurfaceHost Create(DesignSurfaceDocument? document)
        {
            var root = document is null ? null : MobileCrateRoot(document.Path);
            if (document is null || root is null)
            {
                // Not a mobile view after all (moved meanwhile): the default (desktop Rust) surface.
                return DesignSurfaceHostFactoryHost.Current.Create(document);
            }

            KubunoLog.WriteLine($"[mobile-bridge] {Path.GetFileName(document.Path)}: web design surface (bundled host), mobile crate {root}.");
            // The bundled host folder is both where the page is served from AND where its kbview-registry.web.json is,
            // so ForBundledHost reads the same host elements the surface renders (the Toolbox would be empty otherwise).
            var bundled = WebDesignSurfaceHostFactory.BundledFolder();
            return new WebDesignSurfaceHost(document, WebViewProject.ForBundledHost(root, bundled), bundled, formFactor: "mobile");
        }

        /// <summary>
        /// The directory of the nearest ancestor <c>Cargo.toml</c> whose <c>[package] name</c> starts with
        /// <c>kubuno-mobile-</c>; null when the nearest crate is not a mobile one (a desktop crate, a web project, or no
        /// crate at all). Never throws.
        /// </summary>
        private static string? MobileCrateRoot(string? viewPath)
        {
            if (string.IsNullOrEmpty(viewPath))
            {
                return null;
            }

            string? dir;
            try
            {
                dir = Path.GetDirectoryName(Path.GetFullPath(viewPath));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }

            while (!string.IsNullOrEmpty(dir))
            {
                var cargo = Path.Combine(dir, "Cargo.toml");
                if (File.Exists(cargo))
                {
                    // The nearest crate decides: a mobile package claims the view, any other one declines it.
                    return IsMobilePackage(cargo) ? dir : null;
                }

                var parent = Path.GetDirectoryName(dir);
                if (string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                dir = parent;
            }

            return null;
        }

        /// <summary>Whether <paramref name="cargoPath"/>'s <c>[package] name</c> starts with <c>kubuno-mobile-</c>.</summary>
        private static bool IsMobilePackage(string cargoPath)
        {
            string text;
            try
            {
                text = File.ReadAllText(cargoPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return false;
            }

            var inPackage = false;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    // A workspace-only manifest has no [package]; a crate manifest does.
                    inPackage = line.StartsWith("[package]", StringComparison.Ordinal);
                    continue;
                }

                if (inPackage && line.StartsWith("name", StringComparison.Ordinal))
                {
                    var open = line.IndexOf('"');
                    if (open >= 0)
                    {
                        var close = line.IndexOf('"', open + 1);
                        if (close > open)
                        {
                            var name = line.Substring(open + 1, close - open - 1);
                            return name.StartsWith("kubuno-mobile-", StringComparison.Ordinal);
                        }
                    }

                    return false;
                }
            }

            return false;
        }
    }
}

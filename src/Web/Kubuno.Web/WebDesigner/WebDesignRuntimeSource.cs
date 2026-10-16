using System;
using Kubuno.Views.Designer.DesignSurface;

namespace Kubuno.Web.WebDesigner
{
    /// <summary>
    /// What renders a web view's preview (docs/WEB-VIEWS.md section 4.2), as the designer's runtime bar shows it: the
    /// project's own dev server (no bar), or the design host bundled with the extension while the dev server is not
    /// running (« Démarrer le serveur de développement »), while it starts (« Annuler »), or after it failed to start
    /// (« Réessayer »). One per pane; the action is the pane's (<see cref="WebDesignSurfaceHost"/>).
    /// </summary>
    internal sealed class WebDesignRuntimeSource : IDesignSurfaceRuntimeSource, IDesignSurfaceRuntimeText
    {
        private readonly Action<DesignSurfaceRuntimeState> _runAction;
        private DesignSurfaceRuntime? _current;

        public WebDesignRuntimeSource(Action<DesignSurfaceRuntimeState> runAction)
        {
            _runAction = runAction;
            State = DesignSurfaceRuntimeState.Project;
        }

        public DesignSurfaceRuntime? Current => _current;

        public DesignSurfaceRuntimeState State { get; private set; }

        public string? Detail { get; private set; }

        public event EventHandler? Changed;

        /// <summary>True while the preview has not chosen yet (looking for the dev server): no bar.</summary>
        public bool Connecting { get; private set; } = true;

        /// <summary>The bundled design host is missing from the extension (and no dev server runs): nothing can render.</summary>
        public bool NoBundledHost { get; private set; }

        private bool _hostRuntimeMissing;

        /// <summary>
        /// The project is a module whose dev server serves the design page from <c>@kubuno/host-runtime</c>, and that package is
        /// not in its <c>node_modules</c>: the bar says what to install instead of showing a page that cannot load.
        /// </summary>
        public bool HostRuntimeMissing
        {
            get => _hostRuntimeMissing;
            set
            {
                if (_hostRuntimeMissing != value)
                {
                    _hostRuntimeMissing = value;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public string? BarMessage
        {
            get
            {
                if (Connecting)
                {
                    return null;
                }

                if (HostRuntimeMissing && State != DesignSurfaceRuntimeState.Project)
                {
                    return NoBundledHost
                        ? T(
                            "Preview unavailable: this module's dev server needs @kubuno/host-runtime for the design page (npm install -D @kubuno/host-runtime in the frontend folder, then Retry), and the extension's bundled design host is missing.",
                            "Aperçu indisponible : le serveur de développement de ce module a besoin de @kubuno/host-runtime pour la page de conception (npm install -D @kubuno/host-runtime dans le dossier frontend, puis Réessayer), et l'hôte de conception intégré à l'extension est absent.")
                        : T(
                            "Preview: bundled runtime - this module's dev server cannot show the design page: @kubuno/host-runtime is not installed (npm install -D @kubuno/host-runtime in the frontend folder, then Retry). The project's own controls show as placeholders.",
                            "Aperçu : runtime intégré - le serveur de développement de ce module ne peut pas afficher la page de conception : @kubuno/host-runtime n'est pas installé (npm install -D @kubuno/host-runtime dans le dossier frontend, puis Réessayer). Les contrôles du projet sont des espaces réservés.");
                }

                if (NoBundledHost && State != DesignSurfaceRuntimeState.Project)
                {
                    return T(
                        "Preview unavailable: the project's dev server is not running and the extension's bundled design host (tools\\web-design-host) is missing.",
                        "Aperçu indisponible : le serveur de développement du projet ne tourne pas et l'hôte de conception intégré à l'extension (tools\\web-design-host) est absent.");
                }

                return State switch
                {
                    DesignSurfaceRuntimeState.NotBuilt => T(
                        "Preview: bundled runtime - the project's dev server is not running (npm run dev); the project's own controls show as placeholders.",
                        "Aperçu : runtime intégré - le serveur de développement du projet ne tourne pas (npm run dev) ; les contrôles du projet sont des espaces réservés."),
                    DesignSurfaceRuntimeState.Building => T(
                        "Preview: starting the project's dev server (npm run dev)...",
                        "Aperçu : démarrage du serveur de développement du projet (npm run dev)…"),
                    DesignSurfaceRuntimeState.Failed => T(
                        "Preview: bundled runtime - the project's dev server did not start (see Output > Kubuno).",
                        "Aperçu : runtime intégré - le serveur de développement du projet n'a pas démarré (voir Sortie > Kubuno)."),
                    _ => null,
                };
            }
        }

        public string? BarAction => Connecting ? null : HostRuntimeMissing && State != DesignSurfaceRuntimeState.Project ? T("Retry", "Réessayer") : State switch
        {
            DesignSurfaceRuntimeState.NotBuilt => T("Start the dev server", "Démarrer le serveur de développement"),
            DesignSurfaceRuntimeState.Building => T("Cancel", "Annuler"),
            DesignSurfaceRuntimeState.Failed => T("Retry", "Réessayer"),
            _ => null,
        };

        public void RunAction() => _runAction(State);

        public void ReportRejected(string message)
        {
            Detail = message;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>The preview runs on the project's dev server at <paramref name="page"/>.</summary>
        public void SetProject(Uri page) => Set(DesignSurfaceRuntimeState.Project, new DesignSurfaceRuntime(page.AbsoluteUri, isProjectRuntime: true), null, noBundled: false);

        /// <summary>The preview runs on the bundled design host (<paramref name="state"/>: not running, starting, failed).</summary>
        public void SetBundled(DesignSurfaceRuntimeState state, string bundledFolder, bool available, string? detail = null) =>
            Set(state, new DesignSurfaceRuntime(bundledFolder, isProjectRuntime: false), detail, noBundled: !available);

        private void Set(DesignSurfaceRuntimeState state, DesignSurfaceRuntime runtime, string? detail, bool noBundled)
        {
            if (!Connecting && State == state && Detail == detail && NoBundledHost == noBundled && runtime.IsSameAs(_current))
            {
                return;
            }

            Connecting = false;
            State = state;
            Detail = detail;
            NoBundledHost = noBundled;
            _current = runtime;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private static string T(string english, string french) => Kubuno.Views.Designer.DesignerText.IsFrench ? french : english;
    }
}

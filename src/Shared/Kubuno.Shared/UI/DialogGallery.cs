using System;
using System.Collections.Generic;

namespace Kubuno.Shared.UI
{
    /// <summary>
    /// The dialogs Tools &gt; "Kubuno: Dialog Gallery" lists (docs/ARCHITECTURE.md, "Themed dialogs"): every layer
    /// registers its own dialogs with sample data (usually from its <c>KubunoLayer.InitializeOnUIThread</c>), so the
    /// gallery re-checks every dialog of the extension in each Visual Studio theme without knowing any of them.
    /// Layers register a provider, evaluated only when the gallery opens: building the sample data (and loading the
    /// assemblies the dialogs live in) must stay out of the package's UI-thread load time.
    /// </summary>
    public static class DialogGallery
    {
        private static readonly object SyncRoot = new();
        private static readonly List<Func<IEnumerable<KeyValuePair<string, Func<bool?>>>>> Providers = new();

        /// <summary>
        /// The registered dialogs, in registration order: display name and "show it modally" (UI thread). Evaluates
        /// every registered provider, so it is read when the gallery opens, never during the package load.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, Func<bool?>>> Entries
        {
            get
            {
                Func<IEnumerable<KeyValuePair<string, Func<bool?>>>>[] providers;
                lock (SyncRoot)
                {
                    providers = Providers.ToArray();
                }

                var entries = new List<KeyValuePair<string, Func<bool?>>>();
                foreach (var provider in providers)
                {
                    entries.AddRange(provider());
                }

                return entries;
            }
        }

        /// <summary>Adds dialogs to the gallery, built by <paramref name="provider"/> each time the gallery opens.</summary>
        public static void Register(Func<IEnumerable<KeyValuePair<string, Func<bool?>>>> provider)
        {
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            lock (SyncRoot)
            {
                Providers.Add(provider);
            }
        }

        /// <summary>Adds dialogs to the gallery.</summary>
        public static void Register(IEnumerable<KeyValuePair<string, Func<bool?>>> entries)
        {
            var copy = new List<KeyValuePair<string, Func<bool?>>>(entries);
            Register(() => copy);
        }

        /// <summary>Adds one dialog to the gallery.</summary>
        public static void Register(string name, Func<bool?> show) => Register(new[] { new KeyValuePair<string, Func<bool?>>(name, show) });
    }
}

using System;
using System.Collections.Generic;

namespace Kubuno.Core.UI
{
    /// <summary>
    /// The dialogs Tools &gt; "Kubuno: Dialog Gallery" lists (docs/ARCHITECTURE.md, "Themed dialogs"): every layer
    /// registers its own dialogs with sample data (usually from its <c>KubunoLayer.InitializeOnUIThread</c>), so the
    /// gallery re-checks every dialog of the extension in each Visual Studio theme without knowing any of them.
    /// </summary>
    public static class DialogGallery
    {
        private static readonly object SyncRoot = new();
        private static readonly List<KeyValuePair<string, Func<bool?>>> Registered = new();

        /// <summary>The registered dialogs, in registration order: display name and "show it modally" (UI thread).</summary>
        public static IReadOnlyList<KeyValuePair<string, Func<bool?>>> Entries
        {
            get
            {
                lock (SyncRoot)
                {
                    return Registered.ToArray();
                }
            }
        }

        /// <summary>Adds dialogs to the gallery.</summary>
        public static void Register(IEnumerable<KeyValuePair<string, Func<bool?>>> entries)
        {
            lock (SyncRoot)
            {
                Registered.AddRange(entries);
            }
        }

        /// <summary>Adds one dialog to the gallery.</summary>
        public static void Register(string name, Func<bool?> show) => Register(new[] { new KeyValuePair<string, Func<bool?>>(name, show) });
    }
}

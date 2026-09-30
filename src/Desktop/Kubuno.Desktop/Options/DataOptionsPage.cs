using System;
using System.ComponentModel;
using Kubuno.VisualStudio.Core.Data;
using Kubuno.VisualStudio.Views.Options;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.VisualStudio.Options
{
    /// <summary>
    /// Tools &gt; Options &gt; Kubuno &gt; Data (docs/DATA.md §9, DATA-5): the Data Explorer and query window settings, in Visual
    /// Studio 2026's unified settings (<c>kubuno.data.*</c>). Read through <see cref="Current"/>.
    /// </summary>
    public sealed class DataOptionsPage : KubunoDialogPage
    {
        public const int DefaultShowDataRows = 200;
        public const int DefaultMaxQueryRows = 1000;
        public const int DefaultQueryTimeoutSeconds = 30;

        [Category("Data Explorer")]
        [DisplayName("Rows shown by Show Table Data")]
        [Description("How many rows \"Show Table Data\" reads from a table or view.")]
        [DefaultValue(DefaultShowDataRows)]
        [UnifiedSetting("kubuno.data.explorer.showDataRows")]
        public int ShowDataRows { get; set; } = DefaultShowDataRows;

        [Category("Data Explorer")]
        [DisplayName("Default credential storage")]
        [Description("Where the Add Connection dialog proposes to store a new connection string: Windows Credential Manager or the user secrets file.")]
        [DefaultValue(CredentialStoreKind.CredentialManager)]
        [UnifiedSetting("kubuno.data.explorer.defaultCredentialStore")]
        public CredentialStoreKind DefaultCredentialStore { get; set; } = CredentialStoreKind.CredentialManager;

        [Category("Query window")]
        [DisplayName("Maximum rows per result")]
        [Description("A query result is cut after this many rows (the status bar says so).")]
        [DefaultValue(DefaultMaxQueryRows)]
        [UnifiedSetting("kubuno.data.query.maxRows")]
        public int MaxQueryRows { get; set; } = DefaultMaxQueryRows;

        [Category("Query window")]
        [DisplayName("Query timeout (seconds)")]
        [Description("How long a query may run before it is cancelled.")]
        [DefaultValue(DefaultQueryTimeoutSeconds)]
        [UnifiedSetting("kubuno.data.query.timeoutSeconds")]
        public int QueryTimeoutSeconds { get; set; } = DefaultQueryTimeoutSeconds;

        [Category("Diagnostics")]
        [DisplayName("Log data helper requests")]
        [Description("Log each request to kubuno-data-tool (method and parameters, with connection strings, passwords and SQL text removed) in the Kubuno Output pane.")]
        [DefaultValue(false)]
        [UnifiedSetting("kubuno.data.diagnostics.logRequests")]
        public bool LogRequests { get; set; }

        /// <summary>The page of the loaded package (UI thread), or a page with the defaults.</summary>
        internal static DataOptionsPage Current
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                try
                {
                    if (KubunoPackage.Instance?.GetDialogPage(typeof(DataOptionsPage)) is DataOptionsPage page)
                    {
                        return page;
                    }
                }
                catch (Exception)
                {
                    // Fall back to the defaults.
                }

                return new DataOptionsPage();
            }
        }

        /// <summary>The values clamped to sane ranges (a hand-edited settings file may hold anything).</summary>
        internal int EffectiveShowDataRows => Clamp(ShowDataRows, 1, 100000, DefaultShowDataRows);

        internal int EffectiveMaxQueryRows => Clamp(MaxQueryRows, 1, 1000000, DefaultMaxQueryRows);

        internal int EffectiveQueryTimeoutSeconds => Clamp(QueryTimeoutSeconds, 1, 86400, DefaultQueryTimeoutSeconds);

        private static int Clamp(int value, int min, int max, int fallback) => value < min || value > max ? fallback : value;
    }
}

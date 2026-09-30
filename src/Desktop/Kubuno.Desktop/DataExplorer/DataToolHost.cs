using System;
using System.IO;
using Kubuno.Desktop.Logic.Data;
using Kubuno.Core.Logging;
using Kubuno.Desktop.Options;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Desktop.DataExplorer
{
    /// <summary>
    /// The package-wide <c>kubuno-data-tool</c> helper (docs/DATA.md §9): one long-lived process, started by the first
    /// request of any data feature and stopped with the package. Every data feature (Data Explorer, query windows, and
    /// later the Data Sources wizard, migrations and SQL IntelliSense) goes through <see cref="Service"/>; nothing here
    /// needs the UI thread. The helper's stderr and (option "Log data helper requests") the redacted requests go to the
    /// "Kubuno" Output pane.
    /// </summary>
    internal static class DataToolHost
    {
        private static readonly object Sync = new object();
        private static DataToolClient? _client;
        private static DataToolService? _service;
        private static volatile bool _logRequests;

        /// <summary>The typed API of the helper (created on first use; the process itself starts with the first request).</summary>
        public static DataToolService Service
        {
            get
            {
                lock (Sync)
                {
                    if (_service is null)
                    {
                        var launcher = new ProcessDataToolLauncher(LocateExe)
                        {
                            StandardErrorLine = line => KubunoLog.WriteLine("kubuno-data-tool: " + line),
                        };
                        _client = new DataToolClient(launcher, KubunoLog.WriteLine) { TraceRequests = () => _logRequests };
                        _service = new DataToolService(_client);
                    }

                    return _service;
                }
            }
        }

        /// <summary>Follows the "Log data helper requests" option (UI thread: reads the options page).</summary>
        public static void RefreshOptions()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _logRequests = DataOptionsPage.Current.LogRequests;
        }

        /// <summary>Stops the helper (package disposal).</summary>
        public static void Shutdown()
        {
            DataToolClient? client;
            lock (Sync)
            {
                client = _client;
                _client = null;
                _service = null;
            }

            client?.Dispose();
        }

        private static string? LocateExe()
        {
            string? installDirectory = null;
            try
            {
                installDirectory = Path.GetDirectoryName(typeof(DataToolHost).Assembly.Location);
            }
            catch (Exception)
            {
                // Dynamic assembly: only the dev build folder remains.
            }

            string? exe = DataToolLocator.Locate(installDirectory);
            if (exe is null)
            {
                KubunoLog.WriteLine($"Kubuno: {DataToolLocator.ExeName} not found in the extension's tools folder nor in {DataToolLocator.DevBuildDirectory}.");
            }
            else
            {
                KubunoLog.WriteLine($"Kubuno: starting {exe} --stdio.");
            }

            return exe;
        }
    }
}

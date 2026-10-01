using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.Web.ProjectSystem
{
    /// <summary>
    /// The dev core's console in the Output window (docs/WEB.md, "F5"): F5 starts the core with its standard output and
    /// error redirected to <c>dev-core\logs\core-console.log</c> (the native debugger's <c>&gt; file 2&gt;&amp;1</c>), and this
    /// follows the file into the "Kubuno Core Web (serveur)" pane - the core's tracing log, including what the modules
    /// it supervises print. ANSI colours are removed and the secrets of the launch (the database password) masked.
    /// Lines are written on the UI thread only.
    /// </summary>
    internal sealed class CoreConsoleTail
    {
        /// <summary>The pane's GUID (stable, so Visual Studio reuses the same pane between sessions).</summary>
        private static readonly Guid PaneGuid = new Guid("8d3c2a51-7f0e-4b9a-a6c4-2e5b9f1d7c30");
        private static readonly Regex Ansi = new Regex(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.CultureInvariant);

        private readonly string _path;
        private readonly IReadOnlyList<string> _masks;
        private readonly StringBuilder _partial = new();
        private long _offset;
        private bool _cleared;

        public CoreConsoleTail(string path, IEnumerable<string> masks)
        {
            _path = path;
            _masks = masks.Where(mask => mask.Length >= 4).Distinct().OrderByDescending(mask => mask.Length).ToList();
        }

        public const string PaneTitle = "Kubuno Core Web (serveur)";

        /// <summary>Reads what the core wrote since the last call and appends the complete lines (all of them when <paramref name="final"/>).</summary>
        public async Task PumpAsync(bool final = false)
        {
            string text;
            try
            {
                if (!File.Exists(_path))
                {
                    return;
                }

                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length < _offset)
                {
                    _offset = 0;
                }

                if (stream.Length == _offset && !final)
                {
                    return;
                }

                stream.Seek(_offset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
                text = await reader.ReadToEndAsync().ConfigureAwait(false);
                _offset = stream.Length;
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            _partial.Append(text);
            var buffered = _partial.ToString();
            var cut = final ? buffered.Length : buffered.LastIndexOf('\n') + 1;
            if (cut <= 0)
            {
                return;
            }

            var complete = buffered.Substring(0, cut);
            _partial.Remove(0, cut);
            var cleaned = Ansi.Replace(complete, string.Empty).Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            foreach (var mask in _masks)
            {
                cleaned = cleaned.Replace(mask, "***");
            }

            if (final && !cleaned.EndsWith(Environment.NewLine, StringComparison.Ordinal) && cleaned.Length > 0)
            {
                cleaned += Environment.NewLine;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var pane = GetPane();
            if (pane is null)
            {
                return;
            }

            if (!_cleared)
            {
                pane.Clear();
                pane.Activate();
                _cleared = true;
            }

            pane.OutputStringThreadSafe(cleaned);
        }

        /// <summary>Brings the pane to the front of the Output window (UI thread).</summary>
        public static void Activate()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            GetPane()?.Activate();
        }

        private static IVsOutputWindowPane? GetPane()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsOutputWindow)) is not IVsOutputWindow window)
            {
                return null;
            }

            var guid = PaneGuid;
            if (ErrorHandler.Failed(window.GetPane(ref guid, out var pane)) || pane is null)
            {
                window.CreatePane(ref guid, PaneTitle, fInitVisible: 1, fClearWithSolution: 0);
                window.GetPane(ref guid, out pane);
            }

            return pane;
        }
    }
}

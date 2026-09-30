using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Kubuno.VisualStudio.Designer.Editing;
using Kubuno.VisualStudio.Designer.Editing.Infrastructure;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;

namespace Kubuno.VisualStudio.Designer.Handlers.Infrastructure
{
    /// <summary>
    /// The real <see cref="IWorkspaceFileHost"/>, over Visual Studio's running document table: a document open in
    /// any editor (the <c>.rs</c> code-behind in a code window, the <c>.kbview</c> in the designer) is edited through
    /// its buffer - never by reopening it, so no hidden editor is left dirty - and a closed one on disk. Also
    /// collects the texts of the open code-behind files a handler request sends as <c>openFiles</c>, so the language
    /// server computes its offsets against what the editors hold, unsaved changes included. UI thread only.
    /// </summary>
    public sealed class VsWorkspaceFileHost : IWorkspaceFileHost
    {
        private readonly IServiceProvider _serviceProvider;

        public VsWorkspaceFileHost(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public IEditableTextBuffer? TryGetOpenBuffer(string fileUri)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var buffer = FindTextBuffer(ToPath(fileUri));
            return buffer is null ? null : new BufferEditApplier(buffer);
        }

        public string? ReadFile(string fileUri)
        {
            try
            {
                return File.ReadAllText(ToPath(fileUri));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }

        public void WriteFile(string fileUri, string text)
        {
            // Keep a UTF-8 BOM when the file had one (Rust sources normally have none).
            var path = ToPath(fileUri);
            var hadBom = File.Exists(path) && HasUtf8Bom(path);
            File.WriteAllText(path, text, new UTF8Encoding(hadBom));
        }

        /// <summary>
        /// The texts of the open documents in <paramref name="folder"/> with one of <paramref name="extensions"/>
        /// (<c>.rs</c>), keyed by <c>file://</c> URI - the <c>openFiles</c> of a handler request.
        /// </summary>
        public IDictionary<string, string> OpenTexts(string folder, params string[] extensions)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var normalizedFolder = Normalize(folder);
            foreach (var info in new RunningDocumentTable(_serviceProvider))
            {
                var moniker = info.Moniker;
                if (string.IsNullOrEmpty(moniker) || !IsFilePath(moniker))
                {
                    continue;
                }

                var extension = Path.GetExtension(moniker);
                if (!Array.Exists(extensions, e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase)) ||
                    !string.Equals(Normalize(Path.GetDirectoryName(moniker) ?? string.Empty), normalizedFolder, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ToTextBuffer(info.DocData) is { } buffer)
                {
                    result[new Uri(moniker).AbsoluteUri] = buffer.CurrentSnapshot.GetText();
                }
            }

            return result;
        }

        private ITextBuffer? FindTextBuffer(string path)
        {
            var docData = new RunningDocumentTable(_serviceProvider).FindDocument(path);
            return ToTextBuffer(docData);
        }

        private ITextBuffer? ToTextBuffer(object? docData)
        {
            if (docData is not IVsTextBuffer vsBuffer || _serviceProvider.GetService(typeof(SComponentModel)) is not IComponentModel componentModel)
            {
                return null;
            }

            return componentModel.GetService<IVsEditorAdaptersFactoryService>()?.GetDataBuffer(vsBuffer);
        }

        private static string ToPath(string fileUri) => new Uri(fileUri).LocalPath;

        private static bool IsFilePath(string moniker) => moniker.Length > 2 && (moniker[1] == ':' || moniker.StartsWith(@"\\", StringComparison.Ordinal));

        private static string Normalize(string path) => path.Replace('/', '\\').TrimEnd('\\');

        private static bool HasUtf8Bom(string path)
        {
            using var stream = File.OpenRead(path);
            var bom = new byte[3];
            return stream.Read(bom, 0, 3) == 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF;
        }
    }
}

using System;
using Kubuno.Views.Designer.Editing;
using Kubuno.Views.Designer.Editing.Infrastructure;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace Kubuno.Views.Designer.Handlers.Infrastructure
{
    /// <summary>
    /// The real <see cref="IHandlerDocumentHost"/>: opens (or reuses) a document through
    /// <see cref="VsShellUtilities.OpenDocument"/> the same way any VSSDK "open this file and put the
    /// caret somewhere" command does, bridges its <c>IVsTextLines</c> to a real
    /// <c>Microsoft.VisualStudio.Text.ITextBuffer</c> via <c>IVsEditorAdaptersFactoryService</c>
    /// (`INTEGRATION.md` §8 point 1's own precedent for that exact bridge), and wraps it in
    /// <see cref="Editing.Infrastructure.BufferEditApplier"/> so <see cref="HandlerCreationService"/>
    /// only ever talks to the shared <see cref="IEditableTextBuffer"/> seam - no VS-specific buffer type
    /// leaks past this class.
    ///
    /// Not unit-tested here - needs a live `devenv.exe` (a real `IVsUIShellOpenDocument`/`IVsTextView`),
    /// the same reasoning <see cref="UI.CodeWindowHost"/>/<see cref="EditorFactory.DesignerWindowPane"/>'s
    /// own doc comments already give for staying out of tests/Kubuno.Desktop.Tests/Designer; a
    /// manual check in the experimental instance is this class's test strategy, per docs/DESIGNER.md's
    /// DSG-10 row ("manual check for the VS-side open/insert/caret-jump"). Every method here must run on
    /// the UI thread - see <see cref="HandlerCreationService"/>'s own doc comment for why its caller is
    /// responsible for that.
    /// </summary>
    public sealed class VsHandlerDocumentHost : IHandlerDocumentHost
    {
        private static readonly Guid LogicalViewTextView = VSConstants.LOGVIEWID_TextView;

        private readonly IServiceProvider _serviceProvider;

        public VsHandlerDocumentHost(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public IEditableTextBuffer OpenBuffer(string fileUri)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var (_, textView) = OpenDocument(fileUri);
            ErrorHandler.ThrowOnFailure(textView.GetBuffer(out var textLines));
            var dataBuffer = GetEditorAdaptersFactoryService().GetDataBuffer(textLines);
            if (dataBuffer is null)
            {
                throw new InvalidOperationException($"Could not obtain an ITextBuffer for '{fileUri}'.");
            }

            return new BufferEditApplier(dataBuffer);
        }

        public void NavigateTo(string fileUri, LspPosition position)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var (windowFrame, textView) = OpenDocument(fileUri);
            ErrorHandler.ThrowOnFailure(windowFrame.Show());
            // IVsTextView.SetCaretPos/EnsureSpanVisible take the same 0-based line/character shape LSP
            // positions already use for ASCII content (every handler name and the "fn " stub text this
            // package generates is ASCII-only - kubuno-views-ls's `handler_insert` module never emits
            // non-ASCII identifiers), so no UTF-16 surrogate-pair remapping is needed here.
            ErrorHandler.ThrowOnFailure(textView.SetCaretPos(position.Line, position.Character));
            var span = new TextSpan
            {
                iStartLine = position.Line,
                iStartIndex = position.Character,
                iEndLine = position.Line,
                iEndIndex = position.Character,
            };
            ErrorHandler.ThrowOnFailure(textView.EnsureSpanVisible(span));
        }

        private (IVsWindowFrame WindowFrame, IVsTextView TextView) OpenDocument(string fileUri)
        {
            var path = new Uri(fileUri).LocalPath;
            VsShellUtilities.OpenDocument(
                _serviceProvider,
                path,
                LogicalViewTextView,
                out _,
                out _,
                out var windowFrame,
                out var textView);

            if (textView is null)
            {
                throw new InvalidOperationException($"Could not open a text view for '{path}'.");
            }

            return (windowFrame, textView);
        }

        private IVsEditorAdaptersFactoryService GetEditorAdaptersFactoryService()
        {
            var componentModel = (IComponentModel)_serviceProvider.GetService(typeof(SComponentModel));
            if (componentModel is null)
            {
                throw new InvalidOperationException("SComponentModel is unavailable.");
            }

            return componentModel.GetService<IVsEditorAdaptersFactoryService>();
        }
    }
}

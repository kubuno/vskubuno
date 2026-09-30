using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Rust.Logic.SolutionExplorer;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;
using Newtonsoft.Json.Linq;

namespace Kubuno.Rust.Extensibility
{
    // Where a Kubuno target (desktop views today, web modules and mobile apps next) plugs its own behavior into the
    // Rust editor and Solution Explorer, without the Rust layer knowing the target (docs/ARCHITECTURE.md, "Layers (as
    // built)"). Each is a MEF contract: the layer above exports an implementation from its own assembly, and the Rust
    // MEF parts import them all ([ImportMany]) - so they work as soon as Visual Studio composes the parts, before (and
    // without) the Kubuno package loading, exactly like the Rust features themselves.

    /// <summary>
    /// A language embedded in Rust source (the desktop layer's SQL in query strings, docs/DATA.md DATA-8) that brings its
    /// own completion: rust-analyzer's completion stays out of its text, and its sessions commit with its own characters.
    /// </summary>
    public interface IRustEmbeddedLanguage
    {
        /// <summary>True when <paramref name="point"/> is inside text this language completes itself.</summary>
        bool OwnsPosition(SnapshotPoint point);

        /// <summary>The characters that commit an item of <paramref name="session"/>, or null when the session is not this language's.</summary>
        string? CommitCharactersFor(IAsyncCompletionSession session);
    }

    /// <summary>
    /// What a rename (or Find All References) of a Rust symbol implies outside the Rust code rust-analyzer knows - the
    /// desktop layer's <c>.kbview</c> attributes and <c>handlers!</c> strings naming a view handler (docs/EVENTS.md 5.5).
    /// Called off the UI thread with the LSP request's parameters (<c>textDocument</c>, <c>position</c>).
    /// </summary>
    public interface IRustReferenceParticipant
    {
        /// <summary>The extra edits renaming the symbol at the request's position to <paramref name="newName"/> implies, or null.</summary>
        Task<RustEditContribution?> GetRenameEditAsync(JToken requestParameters, string newName, CancellationToken cancellationToken);

        /// <summary>The extra places the symbol at the request's position is used (as the edits a rename would make there), or null.</summary>
        Task<RustEditContribution?> GetReferencesAsync(JToken requestParameters, CancellationToken cancellationToken);
    }

    /// <summary>An LSP <c>WorkspaceEdit</c> a <see cref="IRustReferenceParticipant"/> adds, and the line it logs.</summary>
    public sealed class RustEditContribution
    {
        public RustEditContribution(JObject edit, string description)
        {
            Edit = edit;
            Description = description;
        }

        /// <summary>The edit (<c>changes</c> or <c>documentChanges</c>).</summary>
        public JObject Edit { get; }

        /// <summary>What it is, for the "Kubuno" Output pane (e.g. "handler 'on_click' in the views").</summary>
        public string Description { get; }
    }

    /// <summary>
    /// The symbol tree Solution Explorer shows under the files of another language inside a Rust project (docs/RSPROJ.md
    /// lot 8) - the desktop layer's <c>.kbview</c> element trees. The Rust layer keeps the tree plumbing (lazy loading,
    /// file watching, in-place merging) and asks the provider for the symbols, their icons and where a double-click goes.
    /// </summary>
    public interface ISolutionSymbolProvider
    {
        /// <summary>The file extension it answers for, with its dot (<c>.kbview</c>).</summary>
        string FileExtension { get; }

        /// <summary>The symbols of one file (any thread; never on the UI thread when it can be avoided).</summary>
        Task<IReadOnlyList<SolutionSymbol>> QueryAsync(string path, CancellationToken cancellationToken);

        /// <summary>The icon of one of its symbols (UI thread).</summary>
        ImageMoniker GetIcon(SolutionSymbol symbol);

        /// <summary>The tooltip of one of its symbols.</summary>
        string GetToolTip(SolutionSymbol symbol);

        /// <summary>The logical view a double-click opens a closed file in (<c>VSConstants.LOGVIEWID</c>).</summary>
        Guid NavigationLogicalView { get; }

        /// <summary>The text view of an opened file whose caret a double-click moves, or null while it is not ready yet (asked again for a couple of seconds; UI thread).</summary>
        IVsTextView? GetTextView(IVsWindowFrame frame);
    }
}

using System.Collections.Generic;
using Kubuno.Desktop.Designer.Editing;

namespace Kubuno.Desktop.Designer.Handlers
{
    /// <summary>
    /// A <c>kubuno/createHandler</c> result's <c>location</c> field - a plain "go to definition"
    /// (<c>lsp_types::Location</c>'s own <c>{uri, range}</c> shape), returned instead of an edit when the
    /// event already names a handler.
    /// </summary>
    public sealed class HandlerLocation
    {
        public HandlerLocation(string uri, LspRange range)
        {
            Uri = uri;
            Range = range;
        }

        public string Uri { get; }

        public LspRange Range { get; }
    }

    /// <summary>
    /// A <c>kubuno/createHandler</c> result's <c>edit</c> field - a minimal <c>WorkspaceEdit</c>
    /// (<c>{changes: {uri: [TextEdit]}}</c>, the standard LSP shape <c>lsp_types::WorkspaceEdit</c>
    /// already serializes to). Reuses DSG-2/DSG-5's own <see cref="TextEditDto"/> for each file's edit
    /// list - the exact <c>{range, newText}</c> pairs <c>kubuno_views::edit</c> already returns, so this
    /// bridge introduces no second edit type.
    /// </summary>
    public sealed class HandlerWorkspaceEdit
    {
        public HandlerWorkspaceEdit(IReadOnlyDictionary<string, IReadOnlyList<TextEditDto>>? changes)
        {
            Changes = changes;
        }

        public IReadOnlyDictionary<string, IReadOnlyList<TextEditDto>>? Changes { get; }
    }

    /// <summary>
    /// The whole <c>kubuno/createHandler</c> response (docs/DESIGNER.md §6/§8, DSG-10). Exactly one of
    /// <see cref="Location"/>/<see cref="Edit"/> is non-null on a successful call - see
    /// <c>kubuno-views-ls</c>'s <c>handler_insert::CreateHandlerResult</c> doc comment for the "already
    /// exists -&gt; location only" short circuit this mirrors.
    /// </summary>
    public sealed class CreateHandlerResponse
    {
        public CreateHandlerResponse(string handlerName, HandlerLocation? location, HandlerWorkspaceEdit? edit)
        {
            HandlerName = handlerName;
            Location = location;
            Edit = edit;
        }

        public string HandlerName { get; }

        public HandlerLocation? Location { get; }

        public HandlerWorkspaceEdit? Edit { get; }
    }
}

using System.ComponentModel.Composition;
using System.Diagnostics.CodeAnalysis;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Utilities;

namespace Kubuno.VisualStudio.Views.LanguageService
{
    /// <summary>
    /// MEF exports that declare the "kbview" content type and associate it with the <c>.kbview</c>
    /// file extension. <see cref="KubunoViewsLanguageClient"/> targets this content type; the
    /// TextMate grammar registered by <c>kbview-languages.pkgdef</c> (see INTEGRATION.md for how the
    /// VSIX must merge it into its own <c>languages.pkgdef</c>) is matched independently, by file
    /// extension (see <c>Grammars/kbview.tmLanguage.json</c>'s <c>fileTypes</c>), so it colorizes the
    /// same files even before the language client activates.
    ///
    /// Base content type: <see cref="CodeRemoteContentDefinition.CodeRemoteContentTypeName"/> ("code")
    /// - the exact base the sibling VSIX project's Rust content type already uses successfully for an
    /// LSP-participating content type (see <c>Kubuno.VisualStudio.LanguageService.ContentDefinition</c>).
    /// Visual Studio's in-box XML editor also exposes a content type named "XML" that would add
    /// bracket-matching/outlining for free if used as an *additional* base - but that could not be
    /// verified from this library alone (no VS instance was run for this task, and an unresolved
    /// content-type base name can break the whole MEF composition, not just this feature). If a later
    /// integration pass confirms "XML" composes cleanly in an experimental instance, add
    /// <c>[BaseDefinition("XML")]</c> alongside the one below; until then, "code" is the safe, proven
    /// choice.
    /// </summary>
    public static class ContentDefinition
    {
        [Export]
        [Name(KbviewConstants.ContentType)]
        [BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
        [SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:FieldsMustBePrivate", Justification = "MEF requires a public static field for a ContentTypeDefinition export.")]
        public static ContentTypeDefinition KbviewContentTypeDefinition = null!;

        [Export]
        [FileExtension(KbviewConstants.FileExtension)]
        [ContentType(KbviewConstants.ContentType)]
        [SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:FieldsMustBePrivate", Justification = "MEF requires a public static field for a FileExtensionToContentTypeDefinition export.")]
        public static FileExtensionToContentTypeDefinition KbviewFileExtensionDefinition = null!;
    }
}

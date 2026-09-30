using System.ComponentModel.Composition;
using System.Diagnostics.CodeAnalysis;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Utilities;

namespace Kubuno.Rust.LanguageService
{
    /// <summary>
    /// MEF exports that declare the "rust" content type and associate it with the .rs file
    /// extension. <see cref="RustLanguageClient"/> targets this content type; the TextMate
    /// grammar registered in languages.pkgdef is matched independently, by file extension
    /// (see Grammars/rust.tmLanguage.json's "fileTypes"), so it colorizes the same files even
    /// before the language client activates.
    /// </summary>
    public static class ContentDefinition
    {
        [Export]
        [Name(Constants.RustContentType)]
        [BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
        [SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:FieldsMustBePrivate", Justification = "MEF requires a public static field for a ContentTypeDefinition export.")]
        public static ContentTypeDefinition RustContentTypeDefinition = null!;

        [Export]
        [FileExtension(Constants.RustFileExtension)]
        [ContentType(Constants.RustContentType)]
        [SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:FieldsMustBePrivate", Justification = "MEF requires a public static field for a FileExtensionToContentTypeDefinition export.")]
        public static FileExtensionToContentTypeDefinition RustFileExtensionDefinition = null!;
    }
}

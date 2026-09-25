using System.ComponentModel.Composition;
using System.Diagnostics.CodeAnalysis;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Utilities;

namespace Kubuno.VisualStudio.LanguageService
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

        // Duplicate .kbview -> "kbview" registration, mirroring Kubuno.VisualStudio.Views'
        // LanguageService/ContentDefinition.cs exactly (same Name/BaseDefinition/FileExtension),
        // in THIS assembly rather than that one. Root-caused live: opening a .kbview file in an
        // experimental instance was claimed by VS's own XML editor (4 "prefix 'x' not defined"
        // errors - the format allows x:Name with no xmlns:x, by design), and the "Kubuno" Output
        // pane never logged a single kubuno-views-ls line, unlike the identically-shaped Rust
        // client which logs reliably - i.e. Kubuno.VisualStudio.Views.dll's own MEF content-type
        // export was not taking effect even with the INTEGRATION.md-documented fallback
        // <Asset Type="Microsoft.VisualStudio.MefComponent" d:ProjectName="Kubuno.VisualStudio.Views" .../>
        // in source.extension.vsixmanifest. VS's content type registry accepts the same content
        // type Name declared from multiple MEF parts (used deliberately here to add a second,
        // known-working registration point: this assembly, which Rust's own working mapping above
        // already proves VS actually scans for this VSIX), so this does not conflict with Views.dll's
        // own copy - whichever one composes wins, and now at least one reliably does.
        [Export]
        [Name(Kubuno.VisualStudio.Views.KbviewConstants.ContentType)]
        [BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
        [SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:FieldsMustBePrivate", Justification = "MEF requires a public static field for a ContentTypeDefinition export.")]
        public static ContentTypeDefinition KbviewContentTypeDefinition = null!;

        [Export]
        [FileExtension(Kubuno.VisualStudio.Views.KbviewConstants.FileExtension)]
        [ContentType(Kubuno.VisualStudio.Views.KbviewConstants.ContentType)]
        [SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:FieldsMustBePrivate", Justification = "MEF requires a public static field for a FileExtensionToContentTypeDefinition export.")]
        public static FileExtensionToContentTypeDefinition KbviewFileExtensionDefinition = null!;
    }
}

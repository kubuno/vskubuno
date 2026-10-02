using System.ComponentModel.Composition;
using System.Diagnostics.CodeAnalysis;
using Microsoft.VisualStudio.Utilities;

namespace Kubuno.Desktop.Resources.Editor
{
    /// <summary>
    /// Gives <c>.kbres</c> buffers Visual Studio's XML content type, so the code view of a resource file (View Code, or
    /// a culture file opened on its own) has XML colouring, outlining and completion. The .kbres format is plain XML.
    /// </summary>
    public static class KbresContentDefinition
    {
        [Export]
        [FileExtension(".kbres")]
        [ContentType("XML")]
        [SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:FieldsMustBePrivate", Justification = "MEF requires a public static field for a FileExtensionToContentTypeDefinition export.")]
        public static FileExtensionToContentTypeDefinition KbresFileExtensionDefinition = null!;
    }
}

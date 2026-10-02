using System.ComponentModel.Composition;
using System.Diagnostics.CodeAnalysis;
using Microsoft.VisualStudio.Utilities;

namespace Kubuno.Views.Settings.Editor
{
    /// <summary>
    /// Gives <c>.kbsettings</c> buffers Visual Studio's XML content type, so the code view of a settings file (View Code)
    /// has XML colouring, outlining and completion. The .kbsettings format is plain XML.
    /// </summary>
    public static class KbsettingsContentDefinition
    {
        [Export]
        [FileExtension(".kbsettings")]
        [ContentType("XML")]
        [SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:FieldsMustBePrivate", Justification = "MEF requires a public static field for a FileExtensionToContentTypeDefinition export.")]
        public static FileExtensionToContentTypeDefinition KbsettingsFileExtensionDefinition = null!;
    }
}

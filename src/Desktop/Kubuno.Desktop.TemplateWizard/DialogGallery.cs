using System;
using System.Collections.Generic;

namespace Kubuno.Desktop.TemplateWizard
{
    /// <summary>
    /// This assembly's dialogs with sample data, for Kubuno.Core's "Kubuno: Dialog Gallery" developer
    /// command (docs/ARCHITECTURE.md, "Themed dialogs"): re-checking every dialog in each Visual Studio theme.
    /// </summary>
    public static class TemplateWizardDialogGallery
    {
        /// <summary>Display name and "show it modally" for each dialog (UI thread).</summary>
        public static IReadOnlyList<KeyValuePair<string, Func<bool?>>> Entries { get; } = new[]
        {
            new KeyValuePair<string, Func<bool?>>("Kubuno Inherited Control", () => BaseClassDialog.Ask("RoundButton") != null),
        };
    }
}

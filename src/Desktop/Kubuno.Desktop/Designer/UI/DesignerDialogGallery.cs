using System;
using System.Collections.Generic;
using Kubuno.Desktop.Designer.Registry;

namespace Kubuno.Desktop.Designer.UI
{
    /// <summary>
    /// This assembly's dialogs with sample data, for Kubuno.VisualStudio's "Kubuno: Dialog Gallery" developer
    /// command (docs/ARCHITECTURE.md, "Themed dialogs"): re-checking every dialog in each Visual Studio theme.
    /// </summary>
    public static class DesignerDialogGallery
    {
        /// <summary>Display name and "show it modally" for each dialog (UI thread).</summary>
        public static IReadOnlyList<KeyValuePair<string, Func<bool?>>> Entries { get; } = new[]
        {
            new KeyValuePair<string, Func<bool?>>(
                DesignerText.IsFrench ? "Choisir des éléments de la boîte à outils" : "Choose Toolbox Items",
                () => new ChooseToolboxItemsDialog(Array.Empty<ComponentMeta>(), new HashSet<string>()).ShowModal()),
            new KeyValuePair<string, Func<bool?>>(
                DesignerText.DesignSizeTitle,
                () => DesignSizeDialog.TryAsk(800, 600, out _, out _)),
        };
    }
}

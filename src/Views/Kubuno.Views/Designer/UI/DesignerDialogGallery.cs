using System;
using System.Collections.Generic;
using Kubuno.Views.Designer.Registry;

namespace Kubuno.Views.Designer.UI
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
            // docs/DESIGNER.md "Data bindings" (sample schema; no language server: no live sample).
            new KeyValuePair<string, Func<bool?>>(
                Bindings.BindingStrings.DialogTitle + " (DataBindingDialog)",
                () => new Bindings.DataBindingDialog(
                    new Bindings.BindingEditModel("Text", "{Binding Title, Mode=TwoWay, StringFormat='{0} !'}", "Stockage"),
                    Bindings.BindingSamples.Schema(),
                    Bindings.BindingShape.Text,
                    "String",
                    Array.Empty<Bindings.BindingIssue>(),
                    preview: null).ShowModal()),
            // docs/STORAGE-COMPONENTS.md §5.2: the Registry key picker of a <RegistryKey>'s Path (read-only).
            new KeyValuePair<string, Func<bool?>>(
                DesignerText.IsFrench ? "Sélectionner une clé du Registre (RegistryKey.Path)" : "Select a Registry Key (RegistryKey.Path)",
                () => new RegistryKeyPickerDialog("CurrentUser", "Default", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced").ShowModal()),
        };
    }
}
